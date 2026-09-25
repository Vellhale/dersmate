using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Identity;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Features.Identity;

/// <param name="RefreshToken">İstemcinin elindeki ham yenileme token'ı.</param>
/// <param name="TumCihazlar">
/// <c>false</c> (varsayılan): yalnızca SUNULAN token iptal edilir — "bu cihazdan çık".
/// <c>true</c>: kullanıcının tüm oturumları düşürülür ve damga ileri alınır —
/// "her yerden çık" (eldeki erişim token'ları da anında ölür).
/// </param>
public sealed record LogoutCommand(string? RefreshToken, bool TumCihazlar = false)
    : IRequest<Unit>;

/// <summary>
/// SUNUCU TARAFLI ÇIKIŞ. İstemcinin token'ı silmesi tek başına yetmiyordu: silinen değer
/// yeniden ele geçirilirse (tarayıcı geçmişi, yedek, disk artığı) yenileme token'ı 60 gün
/// boyunca taze erişim token'ı üretmeye devam ederdi. Bu uç, token'ı SUNUCUDA iptal eder.
/// </summary>
/// <remarks>
/// ─── NEDEN AYRI BİR KOMUT (RefreshSession'a EKLENMEDİ) ──────────────────────────────
/// İptal ilkelleri <see cref="RefreshTokenService"/>'te; buradaki komut onları ÇAĞIRIYOR,
/// yenileme yolundaki kodu değiştirmiyor. Çıkış ve yenileme farklı niyetler; tek uçta
/// toplamak, birinin kuralını (ör. yeniden-kullanım penceresi) diğerine sızdırırdı.
///
/// ─── İDEMPOTENS VE SIZDIRMAMA ───────────────────────────────────────────────────────
/// Çıkış kesin sonuç vermeli: token bulunamazsa ya da zaten iptalliyse HATA ATMIYORUZ.
/// Aksi halde istemci "çıkış başarısız" görüp oturumu ekranda tutabilir; ayrıca farklı
/// yanıtlar, bir token'ın gerçek olup olmadığını sunan kişiye ifşa ederdi. Uç her durumda
/// aynı (çağıran taraf 204 döner).
///
/// ─── SEBEP <see cref="RefreshTokenRevokeReason.SignedOut"/>, Rotated DEĞİL ──────────
/// Tek cihaz iptalinde sebep bilinçle SignedOut. İptalli token /refresh'e yeniden
/// sunulursa yeniden-kullanım tespiti (RefreshSessionHandler) yalnızca <c>Rotated</c> +
/// 30 sn penceresini "masum tekrar" sayıyor; SignedOut o pencereye GİRMEZ. Yani çıkıştan
/// sonra aynı token'ı sunan (büyük olasılıkla çalınmış bir kopya) tüm zinciri düşürür —
/// istenen davranış.
///
/// ─── EKONOMİ/KİLİT ÜÇLÜSÜ UYGULANMAZ ────────────────────────────────────────────────
/// Bu tablo puana dokunmuyor; Redis kilidi + açık transaction + ConcurrencyRetry üçlüsü
/// PUAN YAZAN yollar için (gerekçe: RefreshTokenService sınıf açıklaması).
///
/// ⚠️ ERİŞİM TOKEN'I ARTIĞI. Tek cihaz iptalinde eldeki erişim token'ı ömrü dolana kadar
/// (≤2 saat) çalışır — kısa erişim + iptal edilebilir yenileme tasarımının bilinen ve
/// kabul edilmiş sınırı. "Her yerden çık" (<see cref="LogoutCommand.TumCihazlar"/>) damgayı
/// ileri aldığı için erişim token'ını da anında öldürür.
/// </remarks>
public sealed class LogoutHandler : IRequestHandler<LogoutCommand, Unit>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly RefreshTokenService _refresh;

    public LogoutHandler(IAppDbContext db, IClock clock, RefreshTokenService refresh)
    {
        _db = db;
        _clock = clock;
        _refresh = refresh;
    }

    public async Task<Unit> Handle(LogoutCommand request, CancellationToken ct)
    {
        var ham = request.RefreshToken?.Trim();
        if (string.IsNullOrEmpty(ham))
        {
            // Sunacak token yok (ör. "beni hatırla" kapalı oturum): istemci zaten yereli
            // siliyor, sunucuda iptal edilecek bir şey yok.
            return Unit.Value;
        }

        var satir = await _refresh.BulAsync(ham, ct);
        if (satir is null)
        {
            // Bilinmeyen token: idempotent, sessizce başarı.
            return Unit.Value;
        }

        if (request.TumCihazlar)
        {
            // Her yerden çıkış: mevcut ilkel (tüm aktif token iptali + damga + tüm push
            // cihaz kayıtlarının silinmesi) çağrılıyor.
            var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == satir.UserId, ct);
            if (user is not null)
            {
                await _refresh.TumOturumlariDusurAsync(user, RefreshTokenRevokeReason.SignedOut, ct);
                await _db.SaveChangesAsync(ct);
            }

            return Unit.Value;
        }

        var now = _clock.UtcNow;

        /* Bu çıkışın kapsadığı cihazlar: sunulan token'ın ve (varsa) halef zincirinin
           HWID'leri. Push kaydı bunlar üzerinden silinecek. */
        var cihazlar = new HashSet<string>(StringComparer.Ordinal);
        if (satir.DeviceHwidHash is { } hwid)
        {
            cihazlar.Add(hwid);
        }

        var degisti = false;

        // Tek cihaz: yalnızca sunulan token. Zaten iptalliyse (dönüşüm, önceki çıkış,
        // yaptırım...) YENİDEN iptal edilmez — idempotent.
        if (satir.RevokedAtUtc is null)
        {
            satir.RevokedAtUtc = now;
            satir.RevokeReason = RefreshTokenRevokeReason.SignedOut;
            degisti = true;
        }
        else if (satir.RevokeReason == RefreshTokenRevokeReason.Rotated)
        {
            degisti = await HalefleriIptalEtAsync(satir, now, cihazlar, ct);
        }

        /* ⛔ PUSH SİLMESİ İPTAL DURUMUNDAN BAĞIMSIZ (2026-09-25). Yukarıdaki `if`in içine
           yazılsaydı, zaten iptal edilmiş bir token'la yapılan çıkış (yanıtı yolda kaybolmuş
           bir yenilemeden sonra telefonun elindeki Rotated token) cihaz kaydını yerinde
           bırakırdı ve çıkış yapılmış telefonun kilit ekranına bildirim gitmeye devam ederdi.

           (UserId, HwidHash) ile: kullanıcı-cihaz başına tek satır var (tekil index). HWID'i
           olmayan eski token'da silinecek bir şey bilinemez; atlanır. Web oturumlarının push
           kaydı yok, orada 0 satır.

           ExecuteDelete, token iptalinin SaveChanges'inden ÖNCE: bildirimi kesen adım,
           sonrasında düşebilecek bir yazıma bağlı kalmasın. Tersi sırada kalan bir satırı da
           dağıtıcının oturum bağı süzgeci yakalardı; bu sıra yalnızca daha erken kesiyor. */
        if (cihazlar.Count > 0)
        {
            var kullaniciId = satir.UserId;
            var hwidler = cihazlar.ToList();
            await _db.PushDevices
                .Where(d => d.UserId == kullaniciId && hwidler.Contains(d.HwidHash))
                .ExecuteDeleteAsync(ct);
        }

        if (degisti)
        {
            await _db.SaveChangesAsync(ct);
        }

        return Unit.Value;
    }

    /// <summary>
    /// Zincirde izlenecek en fazla halef. Gerçek durumda 1: telefon Rotated token'ı tutuyor,
    /// sunucuda tek bir aktif halef var. Sınır, uzun ya da bozuk bir zincirin tek bir çıkış
    /// isteğini onlarca sorguya çevirmesine karşı.
    /// </summary>
    private const int ZincirSiniri = 16;

    /// <summary>
    /// Sunulan token DÖNÜŞMÜŞSE (Rotated), <see cref="RefreshToken.ReplacedByTokenId"/>
    /// zincirini izler ve AKTİF halefleri SignedOut ile iptal eder. Haleflerin HWID'lerini
    /// <paramref name="cihazlar"/>'a ekler (push silmesi için).
    /// </summary>
    /// <returns>En az bir halef iptal edildiyse true.</returns>
    /// <remarks>
    /// ─── NEDEN (2026-09-25) ─────────────────────────────────────────────────────
    /// Yenileme yanıtı yolda kaybolursa sunucu token'ı dönüştürmüş, telefon ise eskisini
    /// tutuyor olur. O telefondan çıkış yapılınca sunulan token zaten iptal (Rotated) ve eski
    /// davranış "idempotent, dokunma" idi — sunucuda kimsenin elinde olmayan AKTİF halef 60
    /// gün yaşardı. Push'tan önce bu yalnızca sahipsiz bir token'dı; push'la birlikte o
    /// halef oturum bağını (bu cihazın EN YENİ token'ı aktif mi) geçirir ve çıkış yapılmış
    /// telefona bildirim gitmesine yol açardı.
    ///
    /// SEBEP SignedOut: SignedOut'la iptal edilmiş token'ın /refresh'e tekrar sunulması zincir
    /// DÜŞÜRMEZ (yalnızca Rotated düşürür, RefreshTokenService.GercekYenidenKullanim). Yani
    /// bu iptal, kullanıcının başka cihazlarda sonradan açtığı taze oturumlara dokunmaz.
    ///
    /// ⚠️ Rotated token'ı sunan saldırgan (çalınmış eski kopya) bununla yalnızca o zincirin
    /// halefini kapatabilir; aynı token'ı /refresh'e sunsa zaten kullanıcının TÜM zincirini
    /// düşürürdü (hırsızlık tespiti). Yeni bir yetki açılmıyor.
    /// </remarks>
    private async Task<bool> HalefleriIptalEtAsync(
        RefreshToken satir, DateTime now, HashSet<string> cihazlar, CancellationToken ct)
    {
        var iptalEdildi = false;
        var gorulen = new HashSet<Guid> { satir.Id };
        var sonraki = satir.ReplacedByTokenId;

        for (var adim = 0; sonraki is { } halefId && adim < ZincirSiniri; adim++)
        {
            // Döngüye karşı (bozuk veri): aynı satıra ikinci kez gelinmez.
            if (!gorulen.Add(halefId))
            {
                break;
            }

            var halef = await _db.RefreshTokens.SingleOrDefaultAsync(t => t.Id == halefId, ct);

            // Halef başka kullanıcıya ait olamaz; olursa (bozuk veri) zincire dokunma.
            if (halef is null || halef.UserId != satir.UserId)
            {
                break;
            }

            if (halef.DeviceHwidHash is { } hwid)
            {
                cihazlar.Add(hwid);
            }

            if (halef.RevokedAtUtc is null)
            {
                // Aktif token'ın halefi olmaz: zincirin ucu burası.
                halef.RevokedAtUtc = now;
                halef.RevokeReason = RefreshTokenRevokeReason.SignedOut;
                iptalEdildi = true;
                break;
            }

            // Halef de dönüşmüşse zincir devam ediyor; başka sebeple iptalse uç burası.
            sonraki = halef.RevokeReason == RefreshTokenRevokeReason.Rotated ? halef.ReplacedByTokenId : null;
        }

        return iptalEdildi;
    }
}
