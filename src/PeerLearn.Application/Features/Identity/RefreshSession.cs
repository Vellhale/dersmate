using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Identity;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Features.Identity;

/// <param name="RefreshToken">Ham yenileme token'ı (istemcinin sakladığı değer).</param>
/// <param name="HwidHash">
/// Cihaz parmak izi. Giriş akışındaki gibi ZORUNLU — cihaz banı bu uçtan atlanamasın diye.
/// </param>
public sealed record RefreshSessionCommand(string RefreshToken, string? HwidHash)
    : IRequest<LoginResult>;

/// <summary>
/// Erişim token'ını yeniler: yenileme token'ını dönüştürür ve TAZE claim'lerle yeni bir
/// erişim token'ı üretir.
/// </summary>
/// <remarks>
/// ⛔ BU HANDLER, <c>AccountStatusMiddleware</c>'İN YAPTIĞI HER KONTROLÜ TEKRARLAMAK
/// ZORUNDA — ve bunu unutmak sessiz bir güvenlik boşluğu olurdu.
///
/// Sebebi şu: yenileme isteği tanımı gereği ÖLMÜŞ (ya da hiç olmayan) bir erişim
/// token'ıyla gelir, yani uç <c>[AllowAnonymous]</c> olmak zorunda. Middleware ise
/// yalnızca kimliği doğrulanmış isteklerde çalışıyor. Yani banlı bir kullanıcı, ban
/// yediği anda erişim token'ını kaybeder ama yenileme token'ı elinde kalır; bu uçta ban
/// kontrolü olmasaydı sınırsızca taze token basıp içeride kalırdı. Ban "çalışıyor"
/// görünürdü, çünkü diğer bütün uçlar onu reddediyor olurdu.
///
/// Kontrol listesi (sırası önemli, ucuzdan pahalıya):
///   1. token var mı
///   2. iptal edilmiş mi → yeniden kullanım tespiti (aşağıdaki pencere)
///   3. süresi dolmuş mu
///   4. kullanıcı var mı, durumu giriş yapmaya uygun mu
///   5. cihaz banlı mı
///   6. token, "her yerden çıkış" damgasından eski mi
///
/// ⚠️ CLAIM'LER VERİTABANINDAN TAZE OKUNUYOR — eski token'ın claim'leri kopyalanmıyor.
/// Kopyalansaydı rol değişikliği (moderatörlükten alınma gibi) yenileme boyunca taşınır
/// ve kullanıcı yetkisini süresiz sürdürürdü. Bugün rolün token'da donması bilinen bir
/// sorun (arayüzde panel açık kalıp istekler 403 dönüyor); kısa erişim + taze yenileme
/// bunu İYİLEŞTİRİYOR, ama yalnızca claim'ler yeniden okunursa.
/// </remarks>
public sealed class RefreshSessionHandler : IRequestHandler<RefreshSessionCommand, LoginResult>
{
    private readonly IAppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IClock _clock;
    private readonly RefreshTokenService _refresh;

    public RefreshSessionHandler(
        IAppDbContext db,
        ITokenService tokens,
        IClock clock,
        RefreshTokenService refresh)
    {
        _db = db;
        _tokens = tokens;
        _clock = clock;
        _refresh = refresh;
    }

    public async Task<LoginResult> Handle(RefreshSessionCommand request, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        /* HWID normalizasyonu Login ile BİREBİR AYNI olmak zorunda (trim → küçük harf →
           ilk 128 karakter). Farklı normalize edilseydi aynı cihaz iki farklı değer
           üretir, cihaz kaydı ve ban eşleşmesi sessizce tutmazdı. */
        var hwid = Normalize(request.HwidHash)
                   ?? throw new AppException(ErrorCodes.HwidRequired, "Cihaz kimliği (HWID) zorunludur.");

        var ham = request.RefreshToken?.Trim();
        if (string.IsNullOrEmpty(ham))
        {
            throw new AppException(ErrorCodes.InvalidToken, "Oturum bulunamadı, tekrar giriş yapın.", statusCode: 401);
        }

        var satir = await _refresh.BulAsync(ham, ct);
        if (satir is null)
        {
            throw new AppException(ErrorCodes.InvalidToken, "Oturum bulunamadı, tekrar giriş yapın.", statusCode: 401);
        }

        // ── 2. İptal edilmiş token yeniden sunuldu ──────────────────────────────
        if (satir.RevokedAtUtc is { } iptalAni)
        {
            /* İYİ NİYETLİ TEKRAR MI, HIRSIZLIK MI?

               İki sekme (ya da mobilde iki eşzamanlı istek) aynı anda yenilemeye
               kalkarsa ikincisi, birincinin az önce dönüştürdüğü token'ı sunar. Bu
               masum durum ile gerçek hırsızlık aynı desene sahip; ayırt eden tek şey
               ZAMAN. Pencere içindeyse zinciri İPTAL ETMİYORUZ — istek yalnızca
               başarısız dönüyor ve istemcinin tek-uçuş kuyruğu zaten yeni token'a
               sahip oluyor.

               Pencere olmasaydı iki sekmesi açık her kullanıcı, hiçbir şey yapmadığı
               hâlde her yerden atılırdı; üstelik günlükte "hırsızlık tespit edildi"
               yazacağı için teşhisi de yanıltıcı olurdu. */
            var pencereIcinde =
                satir.RevokeReason == RefreshTokenRevokeReason.Rotated &&
                iptalAni.AddSeconds(RefreshTokenService.DonusumTekrarPenceresiSaniye) > now;

            if (!pencereIcinde)
            {
                var sahibi = await _db.Users.SingleOrDefaultAsync(u => u.Id == satir.UserId, ct);
                if (sahibi is not null)
                {
                    await _refresh.TumOturumlariDusurAsync(
                        sahibi, RefreshTokenRevokeReason.ReuseDetected, ct);
                    await _db.SaveChangesAsync(ct);
                }
            }

            throw new AppException(ErrorCodes.InvalidToken,
                "Oturumun süresi doldu, tekrar giriş yapın.", statusCode: 401);
        }

        // ── 3. Süre ─────────────────────────────────────────────────────────────
        if (satir.ExpiresAtUtc <= now)
        {
            throw new AppException(ErrorCodes.InvalidToken,
                "Oturumun süresi doldu, tekrar giriş yapın.", statusCode: 401);
        }

        // ── 4. Kullanıcı ve durumu ──────────────────────────────────────────────
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == satir.UserId, ct);
        if (user is null)
        {
            throw new AppException(ErrorCodes.InvalidToken, "Oturum bulunamadı, tekrar giriş yapın.", statusCode: 401);
        }

        /* Login'deki switch ile AYNI durumlar — ve aynı sebeple açıkça yazılıyor:
           yeni bir UserStatus eklendiğinde sessizce düşüp geçmek, yenilemenin
           yanlışlıkla açık kalması demek olurdu.

           ⚠️ Deleted BURADA AYRICA ELE ALINMAK ZORUNDA. AccountStatusMiddleware
           Deleted'ı geçiriyor (yalnızca satır yokluğu, Banned ve Suspended'ı kesiyor);
           hesap silme de satırı SİLMİYOR, anonimleştiriyor. Yani bu kontrol olmasaydı
           silinmiş hesap süresiz yenilenirdi. */
        switch (user.Status)
        {
            case UserStatus.Banned:
                throw new AppException(ErrorCodes.UserBanned, "Hesabınız kalıcı olarak engellendi.", statusCode: 403);
            case UserStatus.Suspended when user.SuspendedUntilUtc is null || user.SuspendedUntilUtc > now:
                throw new AppException(ErrorCodes.UserBanned, "Hesabınız geçici olarak askıda.", statusCode: 403);
            case UserStatus.Deleted:
                throw new AppException(ErrorCodes.InvalidToken, "Oturum bulunamadı, tekrar giriş yapın.", statusCode: 401);
            case UserStatus.PendingVerification:
                throw new AppException(ErrorCodes.EmailNotVerified,
                    "Önce e-postanızı doğrulayın.", statusCode: 403);
        }

        // ── 5. Cihaz banı ───────────────────────────────────────────────────────
        /* Üçlü koşul Login'den BİREBİR kopyalandı. Eksik kopyalanırsa (ör. süre
           kontrolü unutulursa) süresi dolmuş bir ban sonsuza dek uygulanır ya da
           tersine, aktif bir ban bu uçtan atlanır. */
        var cihazBanli = await _db.HwidBans.AnyAsync(b =>
            b.HwidHash == hwid && b.IsActive &&
            (b.ExpiresAtUtc == null || b.ExpiresAtUtc > now), ct);

        if (cihazBanli)
        {
            throw new AppException(ErrorCodes.DeviceBanned, "Bu cihaz engellenmiş.", statusCode: 403);
        }

        // ── 6. "Her yerden çıkış" damgası ───────────────────────────────────────
        /* Token satırının ÜRETİLDİĞİ an damgadan eskiyse, bu token o çıkıştan önce
           verilmiş demektir. Normalde damgayı ileri alan akış token'ları da iptal
           ediyor (TumOturumlariDusurAsync ikisini birlikte yapıyor), yani buraya
           düşülmemeli — kontrol ikinci savunma hattı olarak duruyor. */
        if (RefreshTokenService.TokenDamgadanEski(satir.CreatedAtUtc, user.TokensValidFromUtc))
        {
            throw new AppException(ErrorCodes.InvalidToken,
                "Oturumun süresi doldu, tekrar giriş yapın.", statusCode: 401);
        }

        // ── Dönüşüm ─────────────────────────────────────────────────────────────
        var yeniHam = _refresh.Uret(user.Id, hwid, out var yeniSatir);

        satir.RevokedAtUtc = now;
        satir.RevokeReason = RefreshTokenRevokeReason.Rotated;
        satir.ReplacedByTokenId = yeniSatir.Id;
        satir.LastUsedAtUtc = now;

        await _db.SaveChangesAsync(ct);

        return new LoginResult(
            _tokens.CreateAccessToken(user),
            user.Id,
            user.DisplayName,
            user.Role.ToString(),
            user.CanModerate,
            yeniHam);
    }

    private static string? Normalize(string? hwid)
    {
        var trimmed = hwid?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed[..Math.Min(trimmed.Length, 128)];
    }
}
