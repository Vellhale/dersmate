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
    private readonly IClock _clock;

    public ForgotPasswordHandler(IAppDbContext db, ITokenService tokens, IEmailSender email,
        IOptions<EmailOptions> emailOptions, IClock clock)
    {
        _db = db;
        _tokens = tokens;
        _email = email;
        _emailOptions = emailOptions.Value;
        _clock = clock;
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

        // HESAP BAŞINA BEKLEME — e-posta bombardımanına karşı (dinamik testte yakalandı:
        // forgot-password'a art arda istek 12/12 gönderim üretiyordu). ResendVerification
        // ile aynı desen. Yanıt yine TEKDÜZE (boş 200) kalıyor: cooldown içindeyse sessizce
        // atlanır, dışarıdan 'gönderildi mi' ayrımı görünmez (numaralandırma açılmaz).
        var simdi = _clock.UtcNow;
        if (ParolaSifirlama.BeklemeIcinde(user.PasswordResetRequestedAtUtc, simdi))
        {
            return Unit.Value;
        }

        // Damgayı GÖNDERİMDEN ÖNCE yazıp kalıcılaştır: koruma, gönderim yavaşlasa ya da
        // patlasa bile sürsün (en kötüde kullanıcı bir sonraki denemesini bekler; sel kesilir).
        user.PasswordResetRequestedAtUtc = simdi;
        await _db.SaveChangesAsync(ct);

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
