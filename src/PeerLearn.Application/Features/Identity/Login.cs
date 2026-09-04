using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Features.Identity;

/// <param name="HwidHash">
/// İstemci cihaz parmak izinin SHA-256 hex hash'i (opsiyonel ama istemciler göndermeli).
/// HWID ban kontrolü ve cihaz izleme (Modül 4.3) bu değer üzerinden çalışır.
/// </param>
public sealed record LoginCommand(string Email, string Password, string? HwidHash)
    : IRequest<LoginResult>;

/// <param name="Role">RBAC rolü ("Student" | "Moderator" | "Admin").</param>
/// <param name="IsAdmin">
/// Arayüzün "Yönetim sekmesini göster" kararı. Role'den TÜRETİLİR, ayrıca saklanmaz.
/// Adı geriye dönük uyumluluk için korundu (mevcut React kodu ve e2e betikleri bunu okuyor);
/// anlamı "rolü Admin" değil, "yönetim paneline erişebilir" — moderatör de true alır.
/// </param>
public sealed record LoginResult(
    string AccessToken,
    Guid UserId,
    string DisplayName,
    string Role,
    bool IsAdmin);

public sealed class LoginHandler : IRequestHandler<LoginCommand, LoginResult>
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly IClock _clock;

    public LoginHandler(IAppDbContext db, IPasswordHasher hasher, ITokenService tokens, IClock clock)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _clock = clock;
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

        await _db.SaveChangesAsync(ct);

        return new LoginResult(
            _tokens.CreateAccessToken(user),
            user.Id,
            user.DisplayName,
            user.Role.ToString(),
            user.CanModerate);
    }

    private static string? Normalize(string? hwid)
    {
        var trimmed = hwid?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed[..Math.Min(trimmed.Length, 128)];
    }
}
