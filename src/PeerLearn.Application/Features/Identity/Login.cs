using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Identity;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Features.Identity;

/// <param name="HwidHash">
/// İstemci cihaz parmak izinin SHA-256 hex hash'i (opsiyonel ama istemciler göndermeli).
/// HWID ban kontrolü ve cihaz izleme (Modül 4.3) bu değer üzerinden çalışır.
/// </param>
/// <param name="RememberMe">
/// İşaretliyse yenileme token'ı verilir ve oturum 60 gün yaşar; işaretli değilse
/// verilmez ve oturum erişim token'ının ömrüyle (2 saat) sınırlı kalır.
/// </param>
/// <remarks>
/// ⚠️ VARSAYILAN <c>true</c> — ve bu, geriye dönük uyumluluk için ZORUNLU. 14 PowerShell
/// paketi ve mevcut istemciler bu alanı hiç göndermiyor; varsayılan <c>false</c> olsaydı
/// hepsi sessizce yenileme token'sız oturum açar ve özellik çalışmıyor görünürdü.
///
/// Ortak bilgisayar senaryosunda kullanıcı kutuyu BOŞALTIR; o zaman tarayıcıda 60 gün
/// yaşayacak bir taşıyıcı hiç oluşmaz.
/// </remarks>
public sealed record LoginCommand(
    string Email,
    string Password,
    string? HwidHash,
    bool RememberMe = true) : IRequest<LoginResult>;

/// <param name="Role">RBAC rolü ("Student" | "Moderator" | "Admin").</param>
/// <param name="IsAdmin">
/// Arayüzün "Yönetim sekmesini göster" kararı. Role'den TÜRETİLİR, ayrıca saklanmaz.
/// Adı geriye dönük uyumluluk için korundu (mevcut React kodu ve e2e betikleri bunu okuyor);
/// anlamı "rolü Admin" değil, "yönetim paneline erişebilir" — moderatör de true alır.
/// </param>
/// <param name="RefreshToken">
/// Erişim token'ı öldüğünde yenisini almaya yarayan, dönüşümlü ve iptal edilebilir
/// taşıyıcı (<see cref="RefreshTokenRules"/>). Ham değer YALNIZCA BURADA görünür;
/// sunucuda yalnızca hash'i saklanıyor.
/// </param>
/// <remarks>
/// ⚠️ ALAN <b>EKLENDİ</b>, hiçbir alan taşınmadı ya da yeniden adlandırılmadı — ve bu
/// bilinçli. <c>AccessToken</c> alanını bir alt nesneye taşımak daha derli toplu
/// görünürdü ama 14 PowerShell paketi kurulum adımında <c>$login.accessToken</c> okuyor;
/// hepsi aynı anda ve sebebini söylemeyen bir hatayla düşerdi. Aynı sınıftan bir olay
/// <c>yasal-surum.ps1</c>'i doğurmuştu.
/// </remarks>
public sealed record LoginResult(
    string AccessToken,
    Guid UserId,
    string DisplayName,
    string Role,
    bool IsAdmin,
    string? RefreshToken);

public sealed class LoginHandler : IRequestHandler<LoginCommand, LoginResult>
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly IClock _clock;
    private readonly RefreshTokenService _refresh;

    public LoginHandler(
        IAppDbContext db,
        IPasswordHasher hasher,
        ITokenService tokens,
        IClock clock,
        RefreshTokenService refresh)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _clock = clock;
        _refresh = refresh;
    }

    // Kullanıcı bulunamadığında da hash doğrulaması koşulur (timing yan kanalını kapatır).
    private static string? _timingEqualizerHash;

    public async Task<LoginResult> Handle(LoginCommand request, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        // HWID ZORUNLUDUR: opsiyonel olsaydı ban'li cihaz alanı boş bırakarak kontrolü atlardı.
        // (İstemci parmak izi doğası gereği taklit edilebilir; bu kontrol caydırıcı katmandır,
        // tek güvence değildir — sahte HWID gönderen özel istemciler dispute/yaptırımla yakalanır.)
        var hwid = Normalize(request.HwidHash)
                   ?? throw new AppException(ErrorCodes.HwidRequired, "Cihaz kimliği (HWID) zorunludur.");

        var deviceBanned = await _db.HwidBans.AnyAsync(b =>
            b.HwidHash == hwid && b.IsActive &&
            (b.ExpiresAtUtc == null || b.ExpiresAtUtc > now), ct);

        if (deviceBanned)
        {
            throw new AppException(ErrorCodes.DeviceBanned, "Bu cihaz engellenmiş.", statusCode: 403);
        }

        var user = await _db.Users
            .SingleOrDefaultAsync(u => u.Email == request.Email.Trim(), ct);

        if (user is null)
        {
            _timingEqualizerHash ??= _hasher.Hash("timing-equalizer-not-a-real-password");
            _hasher.Verify(_timingEqualizerHash, request.Password);
            throw new AppException(ErrorCodes.InvalidCredentials, "E-posta veya şifre hatalı.", statusCode: 401);
        }

        if (!_hasher.Verify(user.PasswordHash, request.Password))
        {
            // Kullanıcı var/yok bilgisi sızdırılmaz: iki durumda da aynı hata.
            throw new AppException(ErrorCodes.InvalidCredentials, "E-posta veya şifre hatalı.", statusCode: 401);
        }

        switch (user.Status)
        {
            case UserStatus.Banned:
                throw new AppException(ErrorCodes.UserBanned, "Hesabınız kalıcı olarak engellendi.", statusCode: 403);
            case UserStatus.Suspended:
                throw new AppException(ErrorCodes.UserBanned, "Hesabınız geçici olarak askıda.", statusCode: 403);
            /*
              SİLİNMİŞ HESAP: parola özeti zaten kullanılamaz hâle getirildiği için giriş
              yukarıdaki parola kontrolünde düşüyor ve buraya HİÇ ULAŞMIYOR. Dal yine de
              açıkça yazılıyor: bu bir switch ve yeni bir durum eklendiğinde sessizce
              DÜŞÜP GEÇMEK, girişin yanlışlıkla açılması demek olurdu.

              Mesaj bilerek "geçersiz kimlik bilgileri" ile aynı sınıfta değil — buraya
              ulaşmak zaten mümkün olmadığı için bilgi sızdırmıyor.
            */
            case UserStatus.Deleted:
                throw new AppException(ErrorCodes.InvalidCredentials,
                    "E-posta ya da parola hatalı.", statusCode: 401);
            case UserStatus.PendingVerification:
                throw new AppException(ErrorCodes.EmailNotVerified,
                    "Önce e-postanızı doğrulayın (gelen kutunuzu kontrol edin).", statusCode: 403);
        }

        user.LastLoginAtUtc = now;

        var device = await _db.UserDevices
            .SingleOrDefaultAsync(d => d.UserId == user.Id && d.HwidHash == hwid, ct);

        if (device is null)
        {
            _db.UserDevices.Add(new UserDevice { UserId = user.Id, HwidHash = hwid });
        }
        else
        {
            device.LastSeenAtUtc = now;
        }

        /* Yenileme token'ı SaveChanges'ten ÖNCE ekleniyor ki cihaz kaydıyla aynı
           yazmada gitsin. Ayrı SaveChanges'ler olsaydı, ikincisi düşünce kullanıcı
           giriş yapmış ama yenileyemez hâlde kalırdı — ve bu ancak iki saat sonra,
           erişim token'ı ölünce fark edilirdi. */
        /* "Beni hatırla" işaretli değilse yenileme token'ı HİÇ ÜRETİLMİYOR — boş bir
           satır yazıp kullanmamak yerine hiç yazmamak, ortak bilgisayarda geride
           kullanılabilir bir taşıyıcı bırakmamak demek. */
        string? yenilemeTokeni = null;
        if (request.RememberMe)
        {
            yenilemeTokeni = _refresh.Uret(user.Id, hwid, out _);
        }

        await _db.SaveChangesAsync(ct);

        return new LoginResult(
            _tokens.CreateAccessToken(user),
            user.Id,
            user.DisplayName,
            user.Role.ToString(),
            user.CanModerate,
            yenilemeTokeni);
    }

    private static string? Normalize(string? hwid)
    {
        var trimmed = hwid?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed[..Math.Min(trimmed.Length, 128)];
    }
}
