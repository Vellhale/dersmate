using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Features.Identity;

public sealed record ResetPasswordCommand(string Token, string NewPassword) : IRequest<Unit>;

/// <summary>
/// Sıfırlama bağlantısındaki token'la yeni parolayı yazar.
///
/// ─── HATA MESAJI NEDEN TEK ────────────────────────────────────────────────────
/// Token geçersiz, süresi dolmuş, zaten kullanılmış ya da kullanıcı silinmiş —
/// dördü de AYNI mesajı alıyor. Ayrı mesajlar ("bu bağlantı zaten kullanıldı" gibi)
/// elinde token olan birine, o token'ın gerçek bir hesaba ait olduğunu doğrulardı.
/// Kullanıcı açısından da fark yok: dördünde de yapılacak şey aynı, yeni bağlantı
/// istemek.
///
/// ─── TEK KULLANIMLIK ──────────────────────────────────────────────────────────
/// Ayrı bir "kullanıldı" tablosu YOK. Token'ın amacı, üretildiği andaki parola
/// hash'inin damgasını taşıyor; parola değişince damga da değişiyor ve aynı bağlantı
/// bir daha eşleşmiyor. Ayrıntı ve gerekçe: ParolaSifirlama.
///
/// Bu aynı zamanda "iki bağlantı istedim, birincisini kullandım" durumunu da doğru
/// çözüyor: ikinci bağlantı da ölür, çünkü ikisi de ESKİ hash'e bağlıydı.
/// ──────────────────────────────────────────────────────────────────────────────
/// </summary>
public sealed class ResetPasswordHandler : IRequestHandler<ResetPasswordCommand, Unit>
{
    /// <summary>Register ile AYNI alt sınır — iki yerde farklı olması, sıfırlamayı
    /// parola kuralını zayıflatmanın yolu yapardı.</summary>
    public const int MinParolaUzunlugu = 8;

    private const string GecersizMesaj =
        "Sıfırlama bağlantısı geçersiz, süresi dolmuş ya da zaten kullanılmış. " +
        "Yeni bir bağlantı isteyin.";

    private readonly IAppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IPasswordHasher _hasher;

    public ResetPasswordHandler(IAppDbContext db, ITokenService tokens, IPasswordHasher hasher)
    {
        _db = db;
        _tokens = tokens;
        _hasher = hasher;
    }

    public async Task<Unit> Handle(ResetPasswordCommand request, CancellationToken ct)
    {
        if (request.NewPassword.Length < MinParolaUzunlugu)
        {
            throw new AppException(ErrorCodes.InvalidCredentials,
                $"Parola en az {MinParolaUzunlugu} karakter olmalı.");
        }

        // Önekle doğrula: tam amaç dizesi kullanıcının parola hash'ine bağlı olduğu için
        // kullanıcıyı yüklemeden bilinemiyor (bkz. ValidatePurposeTokenByPrefix).
        var cozum = _tokens.ValidatePurposeTokenByPrefix(request.Token, "password-reset:")
                    ?? throw new AppException(ErrorCodes.InvalidToken, GecersizMesaj, statusCode: 401);

        var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == cozum.UserId, ct)
                   ?? throw new AppException(ErrorCodes.InvalidToken, GecersizMesaj, statusCode: 401);

        // ASIL KONTROL: token, kullanıcının ŞU ANKİ parolasına mı ait? Değilse bağlantı
        // ya kullanılmış ya da parola başka bir yoldan değişmiş.
        if (ParolaSifirlama.Purpose(user.PasswordHash) != cozum.Purpose)
        {
            throw new AppException(ErrorCodes.InvalidToken, GecersizMesaj, statusCode: 401);
        }

        // Ban kontrolü token doğrulandıktan SONRA: banlı hesap için erken dönüp farklı
        // bir mesaj vermek, ban durumunu token sahibine ifşa ederdi. ForgotPassword zaten
        // banlıya bağlantı göndermiyor; bu, arada banlanan hesap için son kapı.
        if (user.Status == UserStatus.Banned)
        {
            throw new AppException(ErrorCodes.InvalidToken, GecersizMesaj, statusCode: 401);
        }

        user.PasswordHash = _hasher.Hash(request.NewPassword);
        await _db.SaveChangesAsync(ct);

        /*
          E-POSTA DOĞRULAMA DURUMUNA DOKUNULMUYOR — bilerek.

          Sıfırlama bağlantısına tıklamak da e-posta sahipliğini kanıtlar, yani teknik
          olarak burada hesabı doğrulanmış saymak savunulabilirdi. Yapılmadı: doğrulama
          aynı transaction'da HOŞ GELDİN KREDİSİ basıyor (VerifyEmail) ve o yolu ikinci
          bir yerden tetiklemek, ekonominin tek girişini ikiye bölerdi. Zaten
          ForgotPassword doğrulanmamış hesaba bağlantı da göndermiyor.
        */

        return Unit.Value;
    }
}
