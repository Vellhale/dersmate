using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Domain.Communication;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Features.Communication.Bildirimler;

/// <summary>
/// Push cihazının "bağlı" sayılma kuralı: bu (UserId, HwidHash) için CreatedAtUtc'ye göre
/// EN YENİ yenileme token'ı aktif mi (iptal edilmemiş VE süresi dolmamış).
/// </summary>
/// <remarks>
/// ─── NEDEN "EN YENİ", NEDEN "HERHANGİ BİR AKTİF" DEĞİL ───────────────────────
/// Login aynı cihazın eski token'larını iptal etmiyor ve uygulamayı yeniden kuran kullanıcı
/// geride 60 gün yaşayan artık token'lar bırakıyor. "Herhangi bir aktif token" kuralında,
/// çıkış yapılmış (en yeni token'ı iptal edilmiş) bir telefon, eski bir artık yüzünden
/// bağlı sayılır ve bildirim almaya devam ederdi — kilit ekranında başkasının mesajı.
///
/// ─── NEDEN OTURUM BAĞI VAR ──────────────────────────────────────────────────
/// Cihaz satırı çıkışta vb. siliniyor; bu kural o silmelerden biri kaçtığında (çevrimdışı
/// çıkış, yarış) ikinci savunma hattı. Erişim token'ında oturum kimliği yok, bu yüzden
/// cihaz belirli bir yenileme token'ına bağlanamıyor; bağ HWID üzerinden kuruluyor.
///
/// ─── TEK TANIM, İKİ KULLANIM ────────────────────────────────────────────────
/// Kayıt ucu (PUT /push/devices: "bu cihaz şu an bağlı mı") ve dağıtıcı (her turda
/// "alıcının bağlı cihazları") aynı soruyu soruyor. Kural tek bir ifade ağacında
/// (<see cref="Tanim"/>) yazılı; iki kullanım ondan türetiliyor. İkinci bir kopya yazılsaydı
/// biri "en yeni", diğeri "herhangi" diye kayabilirdi ve kayıt ucu kabul ettiği cihaza
/// dağıtıcı hiç göndermezdi (ya da tersi) — hata vermeden.
///
/// SQL: korelasyonlu alt sorgu, <c>ORDER BY "CreatedAtUtc" DESC, "Id" DESC LIMIT 1</c>;
/// IX_RefreshTokens_KullaniciCihaz (UserId, DeviceHwidHash, CreatedAtUtc) — FİLTRESİZ,
/// çünkü en yeni satır iptal edilmiş olabilir ve görünmesi şart.
/// </remarks>
public static class OturumBagi
{
    /// <summary>
    /// Kuralın kendisi. Parametreler: token tablosu, kullanıcı, HWID, şimdiki an.
    /// Sonuç tek elemanlı (ya da boş) bir bool sorgusu: en yeni token'ın aktifliği.
    /// </summary>
    /// <remarks>
    /// "Aktif" tanımı RefreshToken.AktifMi ile aynı: <c>RevokedAtUtc IS NULL AND ExpiresAtUtc &gt; now</c>.
    /// Eşit CreatedAtUtc'de Id ikinci sıralama: sonuç deterministik olsun.
    /// </remarks>
    private static readonly Expression<Func<IQueryable<RefreshToken>, Guid, string, DateTime, IQueryable<bool>>> Tanim =
        (tokenlar, kullaniciId, hwid, now) => tokenlar
            .Where(t => t.UserId == kullaniciId && t.DeviceHwidHash == hwid)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ThenByDescending(t => t.Id)
            .Select(t => t.RevokedAtUtc == null && t.ExpiresAtUtc > now);

    /// <summary>
    /// Kayıt ucu için: bu cihaz şu an bağlı mı? Cihazın hiç token'ı yoksa false.
    /// </summary>
    public static Task<bool> BagliMiAsync(
        IQueryable<RefreshToken> tokenlar, Guid kullaniciId, string hwidHash, DateTime nowUtc, CancellationToken ct)
    {
        var p = new Parametreler(kullaniciId, hwidHash, nowUtc);
        var govde = Yerlestir(
            tokenlar.Expression,
            Expression.Field(Expression.Constant(p), nameof(Parametreler.KullaniciId)),
            Expression.Field(Expression.Constant(p), nameof(Parametreler.Hwid)),
            Expression.Field(Expression.Constant(p), nameof(Parametreler.Now)));

        return tokenlar.Provider.CreateQuery<bool>(govde).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Dağıtıcı için süzgeç: <c>db.PushDevices.Where(OturumBagi.BagliCihaz(db.RefreshTokens, now))</c>.
    /// </summary>
    public static Expression<Func<PushDevice, bool>> BagliCihaz(IQueryable<RefreshToken> tokenlar, DateTime nowUtc)
    {
        var cihaz = Expression.Parameter(typeof(PushDevice), "d");
        var p = new Parametreler(Guid.Empty, string.Empty, nowUtc);
        var sorgu = Yerlestir(
            tokenlar.Expression,
            Expression.Property(cihaz, nameof(PushDevice.UserId)),
            Expression.Property(cihaz, nameof(PushDevice.HwidHash)),
            Expression.Field(Expression.Constant(p), nameof(Parametreler.Now)));

        // Boş alt sorgu (token hiç yok) FirstOrDefault → false: bağlı değil.
        var ilk = Expression.Call(typeof(Queryable), nameof(Queryable.FirstOrDefault), [typeof(bool)], sorgu);
        return Expression.Lambda<Func<PushDevice, bool>>(ilk, cihaz);
    }

    /// <summary>
    /// Tanımın gövdesinde parametreleri verilen ifadelerle değiştirir.
    /// </summary>
    /// <remarks>
    /// Değerler bir nesnenin ALANI olarak veriliyor (sabit değil): EF alan erişimini SQL
    /// parametresine çeviriyor. Sabit verilseydi her farklı "now" SQL'e sabit olarak girer,
    /// her çağrı yeni bir sorgu planı ve EF önbelleğinde yeni bir kayıt açardı.
    /// </remarks>
    private static Expression Yerlestir(Expression tokenlar, Expression kullaniciId, Expression hwid, Expression now)
    {
        var degistirici = new ParametreDegistirici(new Dictionary<ParameterExpression, Expression>
        {
            [Tanim.Parameters[0]] = tokenlar,
            [Tanim.Parameters[1]] = kullaniciId,
            [Tanim.Parameters[2]] = hwid,
            [Tanim.Parameters[3]] = now,
        });
        return degistirici.Visit(Tanim.Body);
    }

    /// <summary>Kapanış nesnesi: EF alanlarını SQL parametresi olarak okur.</summary>
    private sealed class Parametreler(Guid kullaniciId, string hwid, DateTime now)
    {
        public readonly Guid KullaniciId = kullaniciId;
        public readonly string Hwid = hwid;
        public readonly DateTime Now = now;
    }

    private sealed class ParametreDegistirici(IReadOnlyDictionary<ParameterExpression, Expression> esleme) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => esleme.TryGetValue(node, out var yerine) ? yerine : base.VisitParameter(node);
    }
}
