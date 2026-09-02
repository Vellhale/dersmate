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
    private readonly IEmailSender _email;
    private readonly JwtOptions _jwtOptions;
    private readonly IClock _clock;

    public ResendVerificationHandler(IAppDbContext db, IEmailSender email,
        IOptions<JwtOptions> jwtOptions, IClock clock)
    {
        _db = db;
        _email = email;
        _jwtOptions = jwtOptions.Value;
        _clock = clock;
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

        var simdi = _clock.UtcNow;

        /*
          HESAP BAŞINA BEKLEME SÜRESİ — hız sınırından AYRI bir koruma.

          Hız sınırı IP başına (RateLimit:AuthPerMinute) ve saldırgan IP değiştirerek
          onu aşabilir. Bu bekleme ise HEDEF HESABA bakıyor: kimden gelirse gelsin, bir
          adrese dakikada birden fazla doğrulama postası gitmiyor. Kapattığı saldırı
          "mail bombing": birinin gelen kutusunu doldurup adresi kullanılamaz hâle
          getirmek.

          SESSİZCE GEÇİLİYOR, hata DEĞİL: "biraz bekle" demek, o adresin kayıtlı
          olduğunu söylerdi — bu ucun tüm tasarımı varlık sızdırmamak üzerine kurulu
          (yukarıdaki nota bkz.). Kullanıcı için de sonuç aynı: yeni posta gelmiyor,
          elindeki kod hâlâ geçerli.
        */
        if (user.EmailVerificationCodeSentAtUtc is { } sonGonderim &&
            (simdi - sonGonderim).TotalSeconds < EmailVerificationRules.ResendCooldownSeconds)
        {
            return new ResendVerificationResult(string.Empty);
        }

        /*
          YENİ KOD ESKİSİNİ GEÇERSİZ KILIYOR ve deneme sayacı sıfırlanıyor.

          Sayacın sıfırlanması bir zafiyet değil: yeni kod da 1.000.000 olasılıktan
          rastgele seçiliyor, yani saldırgan "yeni kod iste, 5 dene" döngüsüyle hiçbir
          ilerleme kaydetmiyor — her turda baştan başlıyor. Sıfırlanmasaydı, kodunu
          yanlış girip yenisini isteyen DÜRÜST kullanıcı kilitli kalırdı.
        */
        var kod = EmailVerificationRules.GenerateCode();

        user.EmailVerificationCodeHash = EmailVerificationRules.HashCode(user.Id, kod);
        user.EmailVerificationCodeExpiresAtUtc =
            simdi.AddMinutes(EmailVerificationRules.ValidityMinutes);
        user.EmailVerificationCodeSentAtUtc = simdi;
        user.EmailVerificationAttempts = 0;

        await _db.SaveChangesAsync(ct);

        await _email.SendAsync(user.Email, DogrulamaEpostasi.Konu(yenidenGonderim: true),
            DogrulamaEpostasi.Govde(kod), ct);

        return new ResendVerificationResult(
            _jwtOptions.ExposeVerificationTokenInResponse ? kod : string.Empty);
    }
}
