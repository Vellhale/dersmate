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
/// düz metin bir doğrulama e-postası göndermek. Bir SDK (SendGrid/SES) eklemek, bu projenin
/// kurulum kırılganlığını (Google Drive üzerinde node_modules/NuGet) artırırdı. Sağlayıcı
/// değiştirilecekse yapılacak tek şey IEmailSender'ın başka bir uygulamasını yazmaktır.
///
/// GÖNDERİM HATASI YUTULMAZ ama akışı da düşürmez: kayıt işlemi tamamlanmışken e-posta
/// gönderilemediyse kullanıcı zaten "yeniden gönder" ile kurtulabilir (bkz.
/// ResendVerificationHandler). Yutulmayan kısım LOG: sessiz kalırsa kimse fark etmez.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
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

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl,
            Credentials = string.IsNullOrWhiteSpace(_options.Username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(_options.Username, _options.Password)
        };

        using var message = new MailMessage(_options.FromAddress, to, subject, body)
        {
            IsBodyHtml = false
        };

        try
        {
            await client.SendMailAsync(message, ct);
            _logger.LogInformation("E-posta gönderildi: To={To} Subject={Subject}", to, subject);
        }
        catch (Exception ex)
        {
            // Adres ve konu loglanır, GÖVDE loglanmaz: doğrulama token'ı içeriyor.
            _logger.LogError(ex, "E-posta GÖNDERİLEMEDİ: To={To} Subject={Subject}", to, subject);
        }
    }
}
