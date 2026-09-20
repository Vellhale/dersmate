using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Infrastructure.Services;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>ASP.NET Identity'nin PasswordHasher'ı (PBKDF2-HMAC-SHA256) — tam Identity kurulumu olmadan.</summary>
/// <remarks>
/// ─── İŞ FAKTÖRÜ NEDEN AÇIKÇA AYARLANIYOR ────────────────────────────────────────────
/// <see cref="PasswordHasher{TUser}"/>'ın varsayılan iterasyon sayısı (net8 = 100.000)
/// OWASP'ın 2023+ önerisinin (PBKDF2-HMAC-SHA256 için ≥600.000) ALTINDA. Varsayılana
/// güvenmek, karma gücünü sessizce framework sürümüne bağlar; burada açıkça
/// <see cref="IterasyonSayisi"/>'na sabitleniyor.
///
/// ─── ESKİ KARMALAR KIRILMIYOR ───────────────────────────────────────────────────────
/// Identity'nin PBKDF2 biçimi iterasyon sayısını karmanın İÇİNE gömüyor, yani düşük
/// faktörle üretilmiş eski karmalar doğrulanmaya devam eder; yalnızca doğrulama anında
/// "yükseltilmeli" (<see cref="PasswordVerificationResult.SuccessRehashNeeded"/>) işareti
/// döner. <see cref="Verify(string, string, out string?)"/> bunu yakalayıp taze karma
/// üretiyor; çağıran (Login) onu kalıcılaştırıyor. Böylece göç gerekmiyor: her kullanıcı
/// bir sonraki başarılı girişinde sessizce yeni faktöre taşınıyor.
/// </remarks>
public sealed class PasswordHasherService : IPasswordHasher
{
    /// <summary>
    /// PBKDF2-HMAC-SHA256 iterasyon sayısı. OWASP 2023+ önerisi ≥600.000; framework
    /// varsayılanının (100.000) altına DÜŞÜRÜLMEMELİ (birim testle korunuyor).
    /// </summary>
    public const int IterasyonSayisi = 600_000;

    private readonly PasswordHasher<User> _inner = new(
        Options.Create(new PasswordHasherOptions { IterationCount = IterasyonSayisi }));

    public string Hash(string password)
        => _inner.HashPassword(null!, password);

    public bool Verify(string hash, string password)
        => Verify(hash, password, out _);

    public bool Verify(string hash, string password, out string? yenilenmisKarma)
    {
        yenilenmisKarma = null;

        switch (_inner.VerifyHashedPassword(null!, hash, password))
        {
            case PasswordVerificationResult.Success:
                return true;

            case PasswordVerificationResult.SuccessRehashNeeded:
                // Karma eski/zayıf iş faktörüyle üretilmiş: parola doğru, güncel faktörle
                // yeniden karmalayıp çağırana bildiriyoruz (rehash-on-verify).
                yenilenmisKarma = Hash(password);
                return true;

            default:
                return false;
        }
    }
}

/// <summary>
/// MVP e-posta göndericisi: gerçek SMTP/SES entegrasyonu gelene kadar loglar.
/// Doğrulama token'ları geliştirme ortamında konsoldan okunur.
/// </summary>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger) => _logger = logger;

    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        _logger.LogInformation("[E-POSTA] To={To} Subject={Subject}\n{Body}", to, subject, body);
        return Task.CompletedTask;
    }
}
