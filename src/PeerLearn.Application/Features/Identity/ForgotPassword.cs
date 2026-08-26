using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Options;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Features.Identity;

public sealed record ForgotPasswordCommand(string Email) : IRequest<Unit>;

/// <summary>
/// Parola sıfırlama bağlantısı gönderir.
///
/// NEDEN GEREKLİ: 2026-08-27'ye kadar üründe HİÇBİR parola sıfırlama yolu yoktu —
/// ne uç, ne panelde bir düğme. Parolasını unutan kullanıcı hesabını kalıcı olarak
/// kaybediyordu; dersleri, puanı ve rozetleri o hesapta kalıyordu ve destek tarafında
/// da tek çare üretim veritabanına elle müdahaleydi.
///
/// ⚠️ KULLANICI NUMARALANDIRMASINA KAPALI: e-posta kayıtlı olsun olmasın, doğrulanmış
/// olsun olmasın, banlı olsun olmasın YANIT AYNIDIR (boş 200). Aksi halde bu uç,
/// "bu e-posta bu platformda kayıtlı mı" sorusunu herkese açık biçimde yanıtlayan bir
/// araca dönüşürdü — ResendVerification'da alınan kararın aynısı, aynı gerekçeyle.
///
/// Sessizce ATLANAN durumlar ve gerekçeleri:
///   • e-posta kayıtlı değil  → varlık bilgisi sızdırılmaz
///   • hesap banlı            → yaptırım, parola sıfırlayarak delinmez
///   • hesap doğrulanmamış    → o kullanıcının ihtiyacı sıfırlama değil DOĞRULAMA;
///                              sıfırlama bağlantısı göndermek onu yine giriş
///                              yapamadığı bir yere götürürdü (giriş, doğrulanmamış
///                              hesaba kapalı). Doğru yol resend-verification.
/// </summary>
public sealed class ForgotPasswordHandler : IRequestHandler<ForgotPasswordCommand, Unit>
{
    private readonly IAppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IEmailSender _email;
    private readonly EmailOptions _emailOptions;

    public ForgotPasswordHandler(IAppDbContext db, ITokenService tokens, IEmailSender email,
        IOptions<EmailOptions> emailOptions)
    {
        _db = db;
        _tokens = tokens;
        _email = email;
        _emailOptions = emailOptions.Value;
    }

    public async Task<Unit> Handle(ForgotPasswordCommand request, CancellationToken ct)
    {
        var email = request.Email.Trim();

        var user = await _db.Users
            .SingleOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), ct);

        if (user is null ||
            user.Status == UserStatus.Banned ||
            user.EmailVerifiedAtUtc is null)
        {
            return Unit.Value;
        }

        // Purpose, kullanıcının O ANKİ parola hash'ine bağlanıyor: bağlantı kullanılıp
        // parola değişince eski token kendiliğinden geçersizleşiyor (bkz. ParolaSifirlama).
        var token = _tokens.CreatePurposeToken(
            user.Id, ParolaSifirlama.Purpose(user.PasswordHash), ParolaSifirlama.Omur);

        await _email.SendAsync(
            user.Email,
            ParolaSifirlama.Konu(),
            ParolaSifirlama.Govde(token, _emailOptions.PublicWebUrl),
            ct);

        return Unit.Value;
    }
}
