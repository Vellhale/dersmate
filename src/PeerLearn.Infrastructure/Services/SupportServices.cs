using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using PeerLearn.Application.Abstractions;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Infrastructure.Services;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>ASP.NET Identity'nin PasswordHasher'ı (PBKDF2) — tam Identity kurulumu olmadan.</summary>
public sealed class PasswordHasherService : IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(string password)
        => _inner.HashPassword(null!, password);

    public bool Verify(string hash, string password)
        => _inner.VerifyHashedPassword(null!, hash, password)
            is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
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
