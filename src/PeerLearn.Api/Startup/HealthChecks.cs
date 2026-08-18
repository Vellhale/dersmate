using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PeerLearn.Infrastructure.Persistence;
using StackExchange.Redis;

namespace PeerLearn.Api.Startup;

/// <summary>
/// Sağlık yoklamaları.
///
/// İKİ AYRI UÇ, çünkü iki farklı soru soruluyor:
///   /health        — "süreç ayakta mı?" (canlılık). Bağımlılık YOKLAMAZ. Yük dengeleyici
///                    buna bakar; DB kısa süre düştü diye sağlıklı instance'ları öldürüp
///                    kaskad başlatmamalı.
///   /health/ready  — "istek karşılayabilir mi?" (hazırlık). DB'ye gerçekten bağlanır.
///
/// Eskiden tek bir /health vardı ve sabit "ok" dönüyordu: PostgreSQL tamamen kopmuşken de
/// yeşil görünüyordu, yani izleme sisteminin göreceği hiçbir sinyal yoktu.
/// </summary>
public static class HealthChecks
{
    public static IServiceCollection AddPeerLearnHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"])
            .AddCheck<RedisHealthCheck>("redis", failureStatus: HealthStatus.Degraded, tags: ["ready"]);

        return services;
    }
}

/// <summary>Veri olmadan uygulama işe yaramaz: PostgreSQL erişilemezse hazır DEĞİL.</summary>
public sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly PeerLearnDbContext _db;

    public PostgresHealthCheck(PeerLearnDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("PostgreSQL'e bağlanılamıyor.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL yoklaması hata verdi.", ex);
        }
    }
}

/// <summary>
/// Redis YOKSA "bozuk" değil "düşük kapasiteli" (Degraded): uygulama süreç içi kilide ve
/// belleğe düşerek çalışmaya devam eder — tek instance'ta bu güvenli, çok instance'ta
/// riskli. İzlemenin bunu ayırt edebilmesi için ayrı bir durum döner. Redis hiç
/// yapılandırılmamışsa (tek instance kurulumu) yoklama sağlıklı sayılır.
/// </summary>
public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _services;

    public RedisHealthCheck(IServiceProvider services) => _services = services;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var multiplexer = _services.GetService<IConnectionMultiplexer>();

        if (multiplexer is null)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Redis yapılandırılmamış (tek instance)."));
        }

        return Task.FromResult(multiplexer.IsConnected
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded("Redis bağlantısı yok — kilit ve önbellek süreç içine düştü."));
    }
}
