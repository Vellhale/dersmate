using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Options;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Features.Identity;

public sealed record ResendVerificationCommand(string Email) : IRequest<ResendVerificationResult>;

/// <param name="VerificationToken">
/// Yalnızca Jwt:ExposeVerificationTokenInResponse=true iken dolu (geliştirme/otomasyon
/// kolaylığı); üretimde daima boş — token yalnızca e-postayla gider.
/// </param>
public sealed record ResendVerificationResult(string VerificationToken);

/// <summary>
/// Doğrulama bağlantısını yeniden gönderir.
///
/// NEDEN GEREKLİ: doğrulama token'ının ömrü 24 saat (JwtOptions.EmailVerifyTokenHours) ve
/// bu süre dolduğunda kullanıcının elinde HİÇBİR çıkış yolu kalmıyordu — giriş yapamıyor
/// (hesap PendingVerification), aynı e-postayla yeniden kayıt olamıyor (EmailTaken) ve
/// arayüzün yönlendirdiği doğrulama sayfası yalnızca elde token varken işe yarayan bir
/// kutu gösteriyordu. Hoş geldin kredisi de doğrulamaya bağlı olduğu için ilk kullanım
/// tamamen duruyordu.
///
/// KULLANICI NUMARALANDIRMASINA KAPALI: e-posta kayıtlı olsun olmasın, doğrulanmış olsun
/// olmasın yanıt AYNIDIR. Aksi halde bu uç, "bu e-posta bu platformda kayıtlı mı" sorusunu
/// herkese açık biçimde yanıtlayan bir araca dönüşürdü.
/// </summary>
public sealed class ResendVerificationHandler
    : IRequestHandler<ResendVerificationCommand, ResendVerificationResult>
{
    private readonly IAppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IEmailSender _email;
    private readonly JwtOptions _jwtOptions;
    private readonly EmailOptions _emailOptions;

    public ResendVerificationHandler(IAppDbContext db, ITokenService tokens,
        IEmailSender email, IOptions<JwtOptions> jwtOptions, IOptions<EmailOptions> emailOptions)
    {
        _db = db;
        _tokens = tokens;
        _email = email;
        _jwtOptions = jwtOptions.Value;
        _emailOptions = emailOptions.Value;
    }

    public async Task<ResendVerificationResult> Handle(
        ResendVerificationCommand request, CancellationToken ct)
    {
        var email = request.Email?.Trim() ?? string.Empty;

        var user = await _db.Users
            .SingleOrDefaultAsync(u => u.Email == email, ct);

        /*
          Gönderilmeyecek durumlar SESSİZCE geçilir (hata DEĞİL):
            • e-posta kayıtlı değil       → varlık bilgisi sızdırılmaz
            • hesap zaten doğrulanmış     → gereksiz token üretilmez
            • hesap banlı                 → yaptırım e-postayla delinmez
          Üçünde de çağırana aynı boş sonuç döner.
        */
        if (user is null || user.EmailVerifiedAtUtc is not null || user.Status == UserStatus.Banned)
        {
            return new ResendVerificationResult(string.Empty);
        }

        var token = _tokens.CreatePurposeToken(user.Id, RegisterHandler.EmailVerifyPurpose,
            TimeSpan.FromHours(_jwtOptions.EmailVerifyTokenHours));

        await _email.SendAsync(user.Email, DogrulamaEpostasi.Konu(yenidenGonderim: true),
            DogrulamaEpostasi.Govde(token, _emailOptions.PublicWebUrl), ct);

        return new ResendVerificationResult(
            _jwtOptions.ExposeVerificationTokenInResponse ? token : string.Empty);
    }
}
