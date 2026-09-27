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
///   2. iptal edilmiş mi → reddet; ama zinciri YALNIZCA dönüşmüş (Rotated) token
///      pencere dışında tekrarlanırsa düşür (sebep-kapılı; aşağıya bakın)
///   3. süresi dolmuş mu
///   4. kullanıcı var mı, durumu giriş yapmaya uygun mu
///   5. cihaz banlı mı
///   6. token, "her yerden çıkış" damgasından eski mi
///
/// ⚠️ CLAIM'LER VERİTABANINDAN TAZE OKUNUYOR — eski token'ın claim'leri kopyalanmıyor.
/// Kopyalansaydı rol değişikliği (moderatörlükten alınma gibi) yenileme boyunca taşınır
/// ve kullanıcı yetkisini süresiz sürdürürdü. Rolün token'da donması iki yönde FARKLI
/// sonuç verir: YÜKSELTMEde token eski DÜŞÜK rolü taşıdığı için yetkili uçlar geçici 403
/// döner (zararsız — yenileme ya da yeniden giriş çözer); DÜŞÜRMEde ise token eski YÜKSEK
/// rolü taşımaya devam eder ve yetki token ömrü (120 dk) boyunca korunurdu — bu 403 değil,
/// bir güvenlik açığıydı. Artık rol değişimi de <c>ChangeUserRoleHandler</c>'da "her yerden
/// çıkış" primitifini (<c>TumOturumlariDusurAsync</c>) çağırdığı için o pencere kaynağında
/// kapandı. Claim'lerin BURADA taze okunması yine şart: yeniden giriş yapan kullanıcı doğru
/// rolü almalı, yoksa eski claim'ler yenileme boyunca taşınırdı.
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
           üretir, cihaz kaydı ve ban eşleşmesi sessizce tutmazdı. Bu yüzden iki uç da
           (ve push cihaz kaydı) aynı fonksiyonu çağırıyor: HwidKurali. */
        var hwid = HwidKurali.Normalize(request.HwidHash)
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
            /* HIRSIZLIK MI, ÖLÜ BİR TOKEN'IN MASUM TEKRARI MI?

               İptal edilmiş bir token'ın yeniden sunulması TEK BİR durumda hırsızlık
               delilidir: token DÖNÜŞÜMLE (Rotated) iptal edildiyse VE "iyi niyetli
               tekrar" penceresi geçtiyse. Zinciri yalnızca o zaman düşürüyoruz.

               Neden yalnızca Rotated? Çünkü diğer iptal sebepleri — çıkış, parola
               değişimi, yaptırım, hesap silme — zaten BİLİNÇLİ "her yerden çıkış"
               işlemleridir: TumOturumlariDusurAsync o andaki tüm token'ları iptal edip
               TokensValidFromUtc damgasını ileri almıştır. Böyle bir token sonradan
               sunulsa erişim ÜRETMEZ; hırsızlık değil, ölü bir token'ın tekrarıdır ve
               reddedilmesi yeterli. Onu da zincir düşürmeye saydığımız eski hâlde,
               kullanıcının sıfırlama/çıkış SONRASI açtığı TAZE oturumlar da topluca
               düşüyor ve günlükte yanıltıcı biçimde "hırsızlık tespit edildi" yazıyordu.

               Rotated + pencere İÇİ ise (iki sekme / iki eşzamanlı istek aynı token'ı
               yarıştırdı) yine düşürmüyoruz: istek başarısız dönüyor, istemcinin tek-uçuş
               kuyruğu zaten yeni token'a sahip. Karar RefreshTokenService içindeki saf
               GercekYenidenKullanim'da; buradaki üç dal (düşür / düşürme, ama her hâlde
               reddet) yalnızca onu uyguluyor. */
            if (RefreshTokenService.GercekYenidenKullanim(satir.RevokeReason, iptalAni, now))
            {
                var sahibi = await _db.Users.SingleOrDefaultAsync(u => u.Id == satir.UserId, ct);
                if (sahibi is not null)
                {
                    await _refresh.TumOturumlariDusurAsync(
                        sahibi, RefreshTokenRevokeReason.ReuseDetected, ct);
                    await _db.SaveChangesAsync(ct);
                }
            }

            /* ⚠️ YANIT SÖZLEŞMESİ DEĞİŞMEZ: düşürülsün ya da düşürülmesin, iptal edilmiş
               token her hâlde AYNI gövdeli 401 (InvalidToken) ile reddedilir. Mobil,
               gövdeli/gövdesiz 401 ayrımına bağlı (bkz. api.js) — mesaj/kod/durum değişirse
               o ayrım bozulur. */
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
}
