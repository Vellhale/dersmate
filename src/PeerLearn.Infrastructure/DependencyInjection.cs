using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Options;
using PeerLearn.Infrastructure.Caching;
using PeerLearn.Infrastructure.Jobs;
using PeerLearn.Infrastructure.Locking;
using PeerLearn.Infrastructure.Persistence;
using PeerLearn.Infrastructure.Services;
using StackExchange.Redis;

namespace PeerLearn.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EconomyOptions>(configuration.GetSection(EconomyOptions.SectionName));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));

        services.AddDbContext<PeerLearnDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres")));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<PeerLearnDbContext>());

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, PasswordHasherService>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IProofStorage, LocalProofStorage>();

        /*
          E-posta sağlayıcısı AYARDAN seçilir. Geliştirmede "Log" (token konsoldan okunur),
          üretimde "Smtp". Yanlış seçim üretimde sessiz kalmaz: ProductionGuard, sağlayıcı
          Log iken uygulamayı açılışta durdurur — çünkü doğrulama token'ı yalnızca
          e-postayla gidiyor ve gönderilemezse hiç kimse hesabını doğrulayamaz.
        */
        var emailProvider = configuration[$"{EmailOptions.SectionName}:Provider"] ?? "Log";
        if (emailProvider.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }

        // Redis yapılandırıldıysa gerçek distributed lock; yoksa tek-instance fallback.
        var redisConnection = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                // AbortOnConnectFail=false: Redis anlık erişilemezse uygulama AÇILIŞTA ÇÖKMEZ,
                // arka planda yeniden bağlanır. Kilit alma denemesi o aralıkta hata verir —
                // sessizce süreç içi kilide düşmekten (çok instance'ta felaket) çok daha iyisi.
                var options = ConfigurationOptions.Parse(redisConnection);
                options.AbortOnConnectFail = false;
                options.ConnectRetry = 3;

                var multiplexer = ConnectionMultiplexer.Connect(options);
                var logger = sp.GetRequiredService<ILogger<RedisLockProvider>>();

                multiplexer.ConnectionFailed += (_, e) =>
                    logger.LogError("Redis bağlantısı koptu ({Type}): {Exception}", e.ConnectionType, e.Exception?.Message);
                multiplexer.ConnectionRestored += (_, e) =>
                    logger.LogInformation("Redis bağlantısı geri geldi ({Type}).", e.ConnectionType);

                return multiplexer;
            });
            services.AddSingleton<IDistributedLockProvider, RedisLockProvider>();
            services.AddSingleton<ICacheService, RedisCacheService>();
        }
        else
        {
            // Önbellek için süreç içi yedek SESSİZ — kilidin aksine burada düşmek güvenli
            // (en fazla bayat liste). Gerekçesi ICacheService dokümantasyonunda.
            services.AddSingleton<ICacheService, InMemoryCacheService>();

            /*
              ÜRETİM RİSKİ: süreç içi kilit yalnızca TEK instance'ta anlam taşır. Çok
              instance'lı bir kurulumda sessizce buraya düşmek, kredi yarışlarında ilk
              savunma katmanını görünmez şekilde yok eder (DB kısıtları son hat olarak
              kalır ama kullanıcı 409 DUPLICATE gibi ham hatalar görür).
              Bu yüzden seçim SESSİZ değil, açıkça loglanır.
            */
            services.AddSingleton<IDistributedLockProvider>(sp =>
            {
                sp.GetRequiredService<ILogger<InProcessLockProvider>>().LogWarning(
                    "ConnectionStrings:Redis BOŞ — süreç içi kilit kullanılıyor. Bu kurulum " +
                    "yalnızca TEK instance için güvenlidir; birden fazla instance çalıştıracaksanız " +
                    "Redis yapılandırın (bkz. docs/ASAMA-3-FRONTEND.md § Çok instance).");

                return new InProcessLockProvider();
            });
        }

        services.AddHostedService<CreditExpiryJob>();
        services.AddHostedService<SessionSweepJob>();
        services.AddHostedService<StorageCleanupJob>();
        services.AddHostedService<CommunityRewardJob>();

        return services;
    }
}
