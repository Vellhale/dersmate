using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PeerLearn.Application.Features.Economy;
using PeerLearn.Application.Features.Maintenance;
using PeerLearn.Application.Features.Scheduling;

namespace PeerLearn.Infrastructure.Jobs;

// MVP'de BackgroundService + PeriodicTimer yeterli; iş mantığı MediatR komutlarında olduğu
// için ileride Hangfire/Quartz'a geçiş yalnızca bu tetikleyicileri değiştirmek demektir.

/// <summary>30 gün kuralı: vadesi dolan kredileri 15 dakikada bir süpürür (Modül 4.2).</summary>
public sealed class CreditExpiryJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CreditExpiryJob> _logger;

    public CreditExpiryJob(IServiceScopeFactory scopeFactory, ILogger<CreditExpiryJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                await mediator.Send(new ExpireCreditsCommand(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Kredi vade süpürmesi başarısız; sonraki turda tekrar denenecek.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>
/// Topluluk katkısını puana çevirir (net oy → kredi). 15 dakikada bir.
/// </summary>
/// <remarks>
/// SIKLIK VADE SÜPÜRMESİYLE AYNI ve bilinçli: puan bir bakiye değil bir unvan, yani
/// 15 dakikalık gecikmenin kullanıcıya hiçbir maliyeti yok. Daha sık koşmak, her turda
/// tüm kullanıcıları tarayan bir sorguyu karşılıksız tekrarlamak olurdu.
///
/// NEDEN OY ANINDA DEĞİL: iki kilit (içerik + yazarın cüzdanı) ve en sık yolun en
/// pahalı yola bağlanması. Gerekçenin tamamı GrantCommunityRewardsHandler'da.
/// </remarks>
public sealed class CommunityRewardJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// İlk tur açılıştan 2 dakika sonra: uygulama başlarken (migration, ısınma) tüm
    /// kullanıcıları tarayan bir sorgu eklemek açılışı gereksiz yere ağırlaştırırdı.
    /// </summary>
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CommunityRewardJob> _logger;

    public CommunityRewardJob(IServiceScopeFactory scopeFactory, ILogger<CommunityRewardJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                await mediator.Send(new GrantCommunityRewardsCommand(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Topluluk ödülü turu başarısız; sonraki turda tekrar denenecek.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>
/// Depo bakımı: saklama süresi dolan kanıt görselleri + artık dosyalar. Günde bir.
/// </summary>
/// <remarks>
/// SIKLIK NEDEN DÜŞÜK: iş, deponun TAMAMINI listeleyip DB referanslarıyla karşılaştırıyor
/// (mark &amp; sweep). Maliyeti dosya sayısıyla doğru orantılı ve kazancı zamana yayılı —
/// bir dosyanın bir gün fazladan durması hiçbir şeye mal olmaz. Sık koşmak, ucuz olmayan
/// bir taramayı hiçbir fayda karşılığı tekrarlamak olurdu.
///
/// İlk tur, açılıştan 5 dakika sonra: uygulama başlarken (migration, ısınma) ağır bir
/// tarama başlatmak, en kırılgan anda gereksiz yük demek.
/// </remarks>
public sealed class StorageCleanupJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StorageCleanupJob> _logger;

    public StorageCleanupJob(IServiceScopeFactory scopeFactory, ILogger<StorageCleanupJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                await mediator.Send(new CleanupStorageCommand(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Depo bakımı başarısız; sonraki turda tekrar denenecek.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>Otomatik onay (48 saat) + süresi geçmiş rezervasyon düşürme; 10 dakikada bir.</summary>
public sealed class SessionSweepJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SessionSweepJob> _logger;

    public SessionSweepJob(IServiceScopeFactory scopeFactory, ILogger<SessionSweepJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                var result = await mediator.Send(new SweepSessionsCommand(), stoppingToken);

                if (result.AutoApproved > 0 || result.Expired > 0)
                {
                    _logger.LogInformation(
                        "Oturum süpürmesi: {AutoApproved} otomatik onay, {Expired} düşen rezervasyon.",
                        result.AutoApproved, result.Expired);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Oturum süpürmesi başarısız; sonraki turda tekrar denenecek.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
