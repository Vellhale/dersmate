using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Identity;

/// <summary>
/// Yenileme token'ı üretme, dönüştürme ve toplu iptal. Dört çağıranı var: giriş,
/// yenileme ucu, parola değişimi ve hesap silme.
/// </summary>
/// <remarks>
/// AYRI BİR SERVİS OLMASININ SEBEBİ, "yenileme token'ını iptal et" işinin dört farklı
/// akışta tekrarlanması. Tekrarlanan her kopya, birinde unutulunca SESSİZ bir güvenlik
/// boşluğu bırakırdı — silinmiş hesabın token'ının çalışmaya devam etmesi gibi.
///
/// ⚠️ EKONOMİ KURALLARI BURAYA UYGULANMAZ. CLAUDE.md'deki "dağıtık kilit + transaction +
/// ConcurrencyRetry" üçlüsü PUAN YAZAN yollar için; bu tablo puana dokunmuyor. Buraya
/// Redis kilidi koymak, kilidin gerçekten gerektiği yerlerdeki anlamını da zayıflatırdı.
/// </remarks>
public sealed class RefreshTokenService
{
    /// <summary>
    /// Dönüşmüş bir token'ın "iyi niyetli tekrar" sayılacağı pencere.
    /// </summary>
    /// <remarks>
    /// ⛔ BU PENCERE OLMADAN İKİ SEKME BİRBİRİNİ DIŞARI ATAR.
    ///
    /// Yeniden kullanım tespiti şöyle çalışıyor: iptal edilmiş bir token yeniden
    /// sunulursa hırsızlık varsayılıp kullanıcının TÜM zinciri iptal ediliyor. Ama aynı
    /// desen masum bir durumda da oluşuyor: iki sekme (ya da mobilde iki eşzamanlı istek)
    /// aynı anda yenilemeye kalkarsa, ikincisi birincinin az önce dönüştürdüğü token'ı
    /// sunar. Pencere olmasaydı bu, kullanıcıyı hiçbir şey yapmadığı hâlde her yerden
    /// atardı — ve teşhisi çok zor olurdu, çünkü günlükte "hırsızlık tespit edildi"
    /// yazardı.
    ///
    /// Pencere içinde: zincir İPTAL EDİLMİYOR, istek yalnızca başarısız dönüyor. İstemci
    /// tek-uçuş (single-flight) kuyruğu sayesinde zaten yeni token'a sahip olacak.
    /// Pencere dışında: gerçek hırsızlık varsayılıyor.
    ///
    /// 30 saniye, ağ gecikmesi ve yeniden denemeye yeten, çalınan bir token'ın işe
    /// yaramasına yetmeyen bir aralık.
    /// </remarks>
    public const int DonusumTekrarPenceresiSaniye = 30;

    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public RefreshTokenService(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>
    /// Yeni yenileme token'ı üretir ve satırını EKLER (SaveChanges ÇAĞIRMAZ — çağıran
    /// kendi transaction'ında toplasın diye).
    /// </summary>
    /// <returns>Ham token — bir daha asla okunamaz, yalnızca hash'i saklanıyor.</returns>
    public string Uret(Guid userId, string? cihazHwid, out RefreshToken satir)
    {
        var ham = RefreshTokenRules.Generate();
        var now = _clock.UtcNow;

        satir = new RefreshToken
        {
            UserId = userId,
            TokenHash = RefreshTokenRules.Hash(ham),
            ExpiresAtUtc = now.AddDays(RefreshTokenRules.ValidityDays),
            DeviceHwidHash = cihazHwid,
        };

        _db.RefreshTokens.Add(satir);
        return ham;
    }

    /// <summary>
    /// Ham token'ı bulur. Bulunamazsa null. İptal/süre kontrolü ÇAĞIRANDA — çünkü iptal
    /// edilmiş bir satırın bulunması, yeniden kullanım tespitinin girdisidir.
    /// </summary>
    public Task<RefreshToken?> BulAsync(string hamToken, CancellationToken ct)
    {
        /* Hash SORGUNUN DIŞINDA hesaplanıyor. İfade ağacının içine yazılsaydı EF onu
           SQL'e çeviremez ve çalışma anında patlardı — derleme sessiz geçerdi. */
        var hash = RefreshTokenRules.Hash(hamToken);
        return _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
    }

    /// <summary>
    /// Kullanıcının TÜM aktif yenileme token'larını iptal eder ve
    /// <see cref="User.TokensValidFromUtc"/> damgasını ileri alır — yani elindeki erişim
    /// token'ları da anında geçersizleşir. SaveChanges ÇAĞIRMAZ.
    /// </summary>
    /// <remarks>
    /// ⚠️ İKİSİ BİRLİKTE ANLAMLI, tek başına hiçbiri yetmez:
    ///   • yalnızca damga: saldırgan elindeki yenileme token'ıyla saniyeler içinde taze
    ///     erişim token'ı alır;
    ///   • yalnızca iptal: eldeki erişim token'ı ömrü dolana kadar (120 dk) çalışmaya
    ///     devam eder.
    /// Bu yüzden tek metotta toplandı; ayrı ayrı çağrılabilir olsalardı biri unutulurdu.
    /// </remarks>
    public async Task TumOturumlariDusurAsync(
        User user,
        RefreshTokenRevokeReason sebep,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;

        /* Filtre, kısmi index'in filtresini BİREBİR tekrarlıyor
           ("RevokedAtUtc" IS NULL). Başka bir ifadeyle yazılırsa index sessizce
           devreden çıkar — sonuç doğru döner, tablo taranır. */
        var aktifler = await _db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAtUtc == null)
            .ToListAsync(ct);

        foreach (var t in aktifler)
        {
            t.RevokedAtUtc = now;
            t.RevokeReason = sebep;
        }

        /* Damga "şimdi": bu andan ÖNCE üretilmiş erişim token'ları ölüyor.

           ⚠️ Saat kayması payı BİLEREK verilmedi. Pay verilseydi (ör. now + 1dk)
           yaptırımın ısırması o kadar gecikirdi; ban akışı da bu metodu çağırıyor. */
        user.TokensValidFromUtc = now;
    }

    /// <summary>
    /// Erişim token'ı damgadan eski mi? <c>true</c> ise reddedilmeli.
    /// </summary>
    /// <remarks>
    /// ⚠️ İKİ FARKLI HASSASİYET KARŞILAŞTIRILIYOR ve yön önemli. <c>iat</c> claim'i Unix
    /// SANİYESİ (kesirli kısım atılmış), damga ise mikrosaniyeli bir timestamptz. Damga
    /// olduğu gibi karşılaştırılsaydı, damganın yazıldığı saniyenin başında üretilmiş bir
    /// token — yani damgadan ÖNCEKİ, ölmesi gereken token — aynı saniyeye yuvarlandığı
    /// için hayatta kalırdı. 120 dakika daha.
    ///
    /// Bu yüzden damga bir sonraki tam saniyeye YUKARI yuvarlanıyor: o saniye içinde
    /// üretilmiş her token reddediliyor. Ters yönde bir yanlış (damgadan hemen sonra
    /// üretilmiş bir token'ın reddedilmesi) bu üründe oluşamaz, çünkü bu metodu çağıran
    /// dört akışın (parola değişimi, hesap silme, yaptırım, çıkış) hiçbiri aynı anda yeni
    /// token ÜRETMİYOR — hepsi kullanıcıyı dışarı atıyor.
    /// </remarks>
    public static bool TokenDamgadanEski(DateTime tokenUretimAni, DateTime? damga)
    {
        if (damga is null)
        {
            return false;
        }

        var d = damga.Value;
        var tamSaniye = new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, DateTimeKind.Utc);
        var esik = d == tamSaniye ? tamSaniye : tamSaniye.AddSeconds(1);

        return tokenUretimAni < esik;
    }
}
