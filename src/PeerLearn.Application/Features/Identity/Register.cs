using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Options;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Features.Identity;

/// <param name="TermsVersion">
/// İstemcinin gösterdiği sözleşme sürümü. KAYDEDİLEN DEĞER BU DEĞİL
/// (<see cref="LegalDocuments.CurrentVersion"/> kaydediliyor); bu alanın tek işi eşitlik
/// kontrolü — önbellekten gelen eski bir arayüzün, kullanıcının hiç görmediği metne onay
/// vermesini engelliyor. Gerekçenin tamamı LegalDocuments'ta.
/// </param>
/// <param name="AgeConfirmed">
/// "18 yaşından büyüğüm ya da velimin onayıyla" beyanı. Sözleşme onayından AYRI: formda
/// da iki ayrı kutu ve ikisi farklı beyan.
/// </param>
public sealed record RegisterCommand(
    string Email,
    string Password,
    string DisplayName,
    string? TermsVersion = null,
    bool AgeConfirmed = false) : IRequest<RegisterResult>;

/// <param name="VerificationToken">
/// Yalnızca Jwt:ExposeVerificationTokenInResponse=true iken dolu (geliştirme kolaylığı);
/// aksi halde boş string — token yalnızca e-postayla gider (e-posta sahipliği kanıtı).
/// </param>
public sealed record RegisterResult(Guid UserId, string VerificationToken);

public sealed class RegisterHandler : IRequestHandler<RegisterCommand, RegisterResult>
{
    public const string EmailVerifyPurpose = "email-verify";

    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly IEmailSender _email;
    private readonly JwtOptions _jwtOptions;
    private readonly EmailOptions _emailOptions;
    private readonly IClock _clock;

    public RegisterHandler(IAppDbContext db, IPasswordHasher hasher, ITokenService tokens,
        IEmailSender email, IOptions<JwtOptions> jwtOptions, IOptions<EmailOptions> emailOptions,
        IClock clock)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _email = email;
        _jwtOptions = jwtOptions.Value;
        _emailOptions = emailOptions.Value;
        _clock = clock;
    }

    public async Task<RegisterResult> Handle(RegisterCommand request, CancellationToken ct)
    {
        var email = request.Email.Trim();
        if (email.Length is < 5 or > 320 || !email.Contains('@'))
        {
            throw new AppException(ErrorCodes.InvalidCredentials, "Geçerli bir e-posta girin.");
        }

        if (request.Password.Length < 8)
        {
            throw new AppException(ErrorCodes.InvalidCredentials, "Şifre en az 8 karakter olmalı.");
        }

        /*
          ONAY KAPISI — sunucuda, istemcideki `disabled` düğmesine ek olarak.

          Arayüzde iki kutu işaretlenmeden "Hesap oluştur" basılamıyor, ama o kontrol
          yalnızca kullanıcıyı yönlendirmek için: uca doğrudan istek atan biri onaysız
          hesap açabilirdi ve o hesapların onay kaydı sessizce boş kalırdı — tam da bu
          değişikliğin kapatmaya çalıştığı boşluk.

          SÜRÜM EŞİTLİĞİ: istemci hangi metni gösterdiyse onu bildiriyor; farklıysa kayıt
          durduruluyor. Kullanıcının önbelleğindeki eski arayüz eski metni gösterip onay
          almış olabilir — o onayı yürürlükteki metne saymak, kanıt değeri olmayan bir
          kayıt üretirdi. Hata mesajı ne yapılacağını söylüyor (sayfayı yenile).
        */
        if (!request.AgeConfirmed)
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                "Yaş beyanı olmadan hesap açılamaz.");
        }

        if (string.IsNullOrWhiteSpace(request.TermsVersion))
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                "Kullanım koşulları ve gizlilik metni kabul edilmeden hesap açılamaz.");
        }

        if (request.TermsVersion != LegalDocuments.CurrentVersion)
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                "Kullanım koşulları güncellendi. Sayfayı yenileyip yeni metni okuduktan " +
                "sonra tekrar dene.", statusCode: 409);
        }

        // citext kolonu sayesinde karşılaştırma büyük/küçük harf duyarsızdır.
        var exists = await _db.Users.AnyAsync(u => u.Email == email, ct);
        if (exists)
        {
            throw new AppException(ErrorCodes.EmailTaken, "Bu e-posta zaten kayıtlı.", statusCode: 409);
        }

        var simdi = _clock.UtcNow;

        var user = new User
        {
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = _hasher.Hash(request.Password),

            // İstemcinin gönderdiği dizge DEĞİL, sunucunun yürürlükteki sabiti yazılıyor:
            // yukarıda eşitliği doğrulandı, kaydın kaynağı sunucu olmalı.
            TermsVersion = LegalDocuments.CurrentVersion,
            TermsAcceptedAtUtc = simdi,
            AgeConfirmedAtUtc = simdi
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct); // Unique index eşzamanlı kayıtta son savunmadır.

        var token = _tokens.CreatePurposeToken(user.Id, EmailVerifyPurpose,
            TimeSpan.FromHours(_jwtOptions.EmailVerifyTokenHours));

        await _email.SendAsync(email, DogrulamaEpostasi.Konu(yenidenGonderim: false),
            DogrulamaEpostasi.Govde(token, _emailOptions.PublicWebUrl), ct);

        return new RegisterResult(
            user.Id,
            _jwtOptions.ExposeVerificationTokenInResponse ? token : string.Empty);
    }
}
