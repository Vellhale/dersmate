using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Options;

namespace PeerLearn.Infrastructure.Services;

/// <summary>
/// SMTP üzerinden gerçek e-posta gönderimi.
///
/// NEDEN HARİCİ PAKET YOK: System.Net.Mail çerçeveyle geliyor ve tek ihtiyacımız olan şey
/// düz metin bir doğrulama e-postası göndermek. Bir SDK (SendGrid/SES) eklemek, tek bir
/// çağrı için bağımlılık ve kimlik yönetimi yükü getirirdi. Sağlayıcı değiştirilecekse
/// yapılacak tek şey IEmailSender'ın başka bir uygulamasını yazmaktır.
///
/// GÖNDERİM HATASI YUTULMAZ ama akışı da düşürmez: kayıt işlemi tamamlanmışken e-posta
/// gönderilemediyse kullanıcı zaten "yeniden gönder" ile kurtulabilir (bkz.
/// ResendVerificationHandler). Yutulmayan kısım LOG: sessiz kalırsa kimse fark etmez.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    /// <summary>
    /// SMTP zaman aşımı (ms). Varsayılan 100 saniye ve o değer BU UYGULAMA İÇİN
    /// tehlikeli: e-posta gönderimi kayıt isteğinin İÇİNDE yapılıyor, yani ulaşılamayan
    /// bir SMTP sunucusu her kayıt isteğini 100 saniye askıda bırakır. Kullanıcı formun
    /// donduğunu görür, tarayıcı çoğu zaman daha önce vazgeçer ve kişi kaydın başarısız
    /// olduğunu sanıp tekrar dener — oysa hesap açılmıştır ve ikinci deneme
    /// "bu e-posta zaten kayıtlı" der.
    ///
    /// 20 saniye çalışan bir SMTP için fazlasıyla yeterli (el sıkışma tipik olarak
    /// 1-3 sn); ulaşılamayan bir sunucuda ise hata hızlıca ortaya çıkıyor.
    ///
    /// Yerel doğrulamada ölçüldü: var olmayan bir host ile --test-email komutu 300
    /// saniyede bile dönmemişti.
    /// </summary>
    private const int ZamanAsimiMs = 20_000;

    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            _logger.LogError("Email:Host boş — '{Subject}' e-postası {To} adresine GÖNDERİLEMEDİ.", subject, to);
            return;
        }

        try
        {
            await SendOrThrowAsync(to, subject, body, ct);
            _logger.LogInformation("E-posta gönderildi: To={To} Subject={Subject}", to, subject);
        }
        catch (Exception ex)
        {
            // Adres ve konu loglanır, GÖVDE loglanmaz: doğrulama token'ı içeriyor.
            _logger.LogError(ex, "E-posta GÖNDERİLEMEDİ: To={To} Subject={Subject}", to, subject);
        }
    }

    /// <summary>
    /// Gönderir ve HATA FIRLATIR — <see cref="SendAsync"/>'in yutmayan hâli.
    /// </summary>
    /// <remarks>
    /// Yalnızca TEŞHİS için (<c>--test-email</c>). Normal akışlar SendAsync kullanmalı:
    /// kayıt tamamlanmışken e-posta gönderilemedi diye isteği düşürmek, kullanıcıyı
    /// hesabı açılmış ama "kayıt başarısız" yazan bir ekranda bırakırdı.
    ///
    /// Ama teşhis komutunun aynı yutmayı yapması, komutu işe yaramaz kılardı: yanlış
    /// SMTP ayarıyla da "gönderildi" derdi. Burada hata YUKARI çıkıyor ki komut
    /// gerçekten sınasın.
    /// </remarks>
    public async Task SendOrThrowAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            throw new InvalidOperationException(
                "Email:Host boş. Ortam değişkeni: Email__Host");
        }

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl,
            Timeout = ZamanAsimiMs,
            Credentials = string.IsNullOrWhiteSpace(_options.Username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(_options.Username, _options.Password)
        };

        /*
          İPTAL JETONU + Timeout BİRLİKTE, ikisi de tek başına yetmiyor:
          SmtpClient.Timeout bağlantı kurulduktan sonraki işlemleri sınırlıyor ama
          çözülemeyen bir DNS adında ya da yanıt vermeyen bir portta beklemeyi her
          zaman kesmiyor. Jeton o boşluğu kapatıyor.
        */
        using var sure = CancellationTokenSource.CreateLinkedTokenSource(ct);
        sure.CancelAfter(ZamanAsimiMs);

        using var message = new MailMessage(_options.FromAddress, to, subject, body)
        {
            IsBodyHtml = false
        };

        await client.SendMailAsync(message, sure.Token);
    }
}
