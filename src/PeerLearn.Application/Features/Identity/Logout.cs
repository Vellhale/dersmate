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
            // Her yerden çıkış: mevcut ilkel (tüm aktif token iptali + damga) çağrılıyor.
            var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == satir.UserId, ct);
            if (user is not null)
            {
                await _refresh.TumOturumlariDusurAsync(user, RefreshTokenRevokeReason.SignedOut, ct);
                await _db.SaveChangesAsync(ct);
            }

            return Unit.Value;
        }

        // Tek cihaz: yalnızca sunulan token. Zaten iptalliyse (dönüşüm, önceki çıkış,
        // yaptırım...) dokunma — idempotent.
        if (satir.RevokedAtUtc is null)
        {
            satir.RevokedAtUtc = _clock.UtcNow;
            satir.RevokeReason = RefreshTokenRevokeReason.SignedOut;
            await _db.SaveChangesAsync(ct);
        }

        return Unit.Value;
    }
}
