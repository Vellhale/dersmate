using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Features.Communication.Bildirimler;
using PeerLearn.Application.Features.Economy;
using PeerLearn.Application.Features.Identity;
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

/// <summary>
/// Süresi dolmuş yenileme token'ı satırlarını süpürür; günde bir.
/// </summary>
/// <remarks>
/// Aralık uzun tutuldu çünkü acelesi yok: temizlik bir gün gecikse tablo birkaç bin
/// satır fazla taşır, o kadar. Kısa aralık, hiçbir fayda vermeden her gün gereksiz bir
/// tarama koştururdu.
///
/// Başlangıç gecikmesi diğer işlerden UZUN: açılışta göç, katalog tohumlama ve ilk
/// istekler yarışıyor; bakım işinin o kalabalığa katılması için bir sebep yok.
/// </remarks>
public sealed class RefreshTokenCleanupJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RefreshTokenCleanupJob> _logger;

    public RefreshTokenCleanupJob(IServiceScopeFactory scopeFactory, ILogger<RefreshTokenCleanupJob> logger)
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
                var sonuc = await mediator.Send(new CleanupRefreshTokensCommand(), stoppingToken);

                if (sonuc.Silinen > 0)
                {
                    _logger.LogInformation(
                        "Yenileme token'ı bakımı: {Silinen} süresi dolmuş satır silindi.", sonuc.Silinen);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Yenileme token'ı bakımı başarısız; sonraki turda tekrar denenecek.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

// ─── Push bildirimleri (2026-09-25) ─────────────────────────────────────────────
// Üç iş, üç sıklık. Mimari: docs/ASAMA-2-BACKEND.md. İş mantığı Application'daki komutlarda
// (Features/Communication/Bildirimler); buradakiler yalnızca tetikleyici.

/// <summary>
/// Hatırlatmaları (yaklaşan ders, otomatik onay, günlük istek özeti) deftere ÖNCEDEN yazar;
/// dakikada bir.
/// </summary>
/// <remarks>
/// SessionSweepJob'a EKLENMEDİ: onun 10 dakikalık aralığı "10 dakika kala" hatırlatması için
/// fazla kaba. Hatırlatmanın kendi zamanlaması aralığa bağlı değil (satırlar iki saat önceden
/// yazılıyor, dağıtıcı DueAt'e göre gönderiyor); ama rezervasyondan hemen sonra derse az kalan
/// bir dersin satırının geç yazılmaması için aralık kısa. İlk tur açılıştan 1 dakika sonra:
/// göç ve ısınmayla yarışmasın.
/// </remarks>
public sealed class PushReminderJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PushReminderJob> _logger;

    public PushReminderJob(IServiceScopeFactory scopeFactory, ILogger<PushReminderJob> logger)
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
                var sonuc = await mediator.Send(new EnqueuePushRemindersCommand(), stoppingToken);

                if (sonuc.Eklenen > 0)
                {
                    _logger.LogDebug("Push hatırlatmaları: {Eklenen} yeni satır kuyruğa yazıldı.", sonuc.Eklenen);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Push hatırlatma kuyruklaması başarısız; sonraki turda tekrar denenecek.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>
/// Bildirim defterini gönderir: sinyalle ANINDA, sinyal yoksa en geç 5 saniyede bir.
/// </summary>
/// <remarks>
/// ─── NEDEN PERIODICTIMER DEĞİL ──────────────────────────────────────────────
/// Handler'lar commit'ten sonra IBildirimSinyali.Uyandir() çağırıyor; iş beklerken sinyal
/// gelirse beklemeyi kesip hemen tarar. Sinyal süreç içi: diğer sunucu kopyasının satırlarını,
/// kirası dolmuş satırları ve DueAt'i sonradan gelen satırları (mesajda +10 sn, hatırlatmalar,
/// yeniden denemeler) 5 saniyelik tarama yakalar. Boş turun maliyeti tek bir kısmi index
/// sorgusu. Mesajlar 10 sn gecikmeli kuyruğa girdiği için mesaj bildirimi 10–15 sn sonra gider.
///
/// ─── ARDIŞIK HATADA GERİ ÇEKİLME ────────────────────────────────────────────
/// Veritabanı düştüyse (ya da göç henüz uygulanmadıysa) 5 saniyede bir hata günlüğü yazmak
/// asıl sorunu gürültüde boğardı. Ardışık hatada bekleme 5 sn'den 1 dakikaya kadar ikiye
/// katlanır; ilk başarılı turda sıfırlanır.
/// </remarks>
public sealed class NotificationDispatchJob : BackgroundService
{
    public static readonly TimeSpan TaramaAraligi = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HataBeklemeTavani = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBildirimSinyali _sinyal;
    private readonly ILogger<NotificationDispatchJob> _logger;

    public NotificationDispatchJob(
        IServiceScopeFactory scopeFactory, IBildirimSinyali sinyal, ILogger<NotificationDispatchJob> logger)
    {
        _scopeFactory = scopeFactory;
        _sinyal = sinyal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ardisikHata = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                var sonuc = await mediator.Send(new DispatchNotificationsCommand(), stoppingToken);
                ardisikHata = 0;

                if (!sonuc.BosMu)
                {
                    _logger.LogDebug(
                        "Push dağıtımı: {Gonderilen} gönderildi, {Atlanan} atlandı, {Ertelenen} ertelendi, " +
                        "{Basarisiz} başarısız, {SilinenCihaz} cihaz silindi.",
                        sonuc.Gonderilen, sonuc.Atlanan, sonuc.Ertelenen, sonuc.Basarisiz, sonuc.SilinenCihaz);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                ardisikHata++;
                _logger.LogError(ex, "Push dağıtım turu başarısız ({Sayi}. ardışık); sonraki turda tekrar denenecek.", ardisikHata);
            }

            try
            {
                if (ardisikHata == 0)
                {
                    await _sinyal.BekleAsync(TaramaAraligi, stoppingToken);
                }
                else
                {
                    var us = Math.Min(ardisikHata - 1, 4);
                    var bekleme = TimeSpan.FromTicks(Math.Min(TaramaAraligi.Ticks * (1L << us), HataBeklemeTavani.Ticks));
                    await Task.Delay(bekleme, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

/// <summary>
/// Expo makbuzları 15 dakikada bir; bildirim defterinin ve oturumu kapanmış push cihazlarının
/// yaş temizliği günde bir.
/// </summary>
/// <remarks>
/// Temizlik ayrı bir iş olmadı: ikisi de "gönderimden sonra artakalanı toparla" işi ve günde
/// bir koşan bir sorgu için ayrı zamanlayıcı gereksiz. "Günde bir" süreç içi bir damgayla
/// izleniyor: yeniden başlatma temizliği öne çeker, o kadar — komut idempotent. İki sunucu
/// kopyası da günde bir koşar; aynı satırları ikinci kez işlemek zararsız.
///
/// İlk tur açılıştan 5 dakika sonra: gönderim bu sürede biletleri zaten yazmış olur ve açılış
/// kalabalığına bir tarama daha eklenmez.
/// </remarks>
public sealed class PushReceiptJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TemizlikAraligi = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PushReceiptJob> _logger;

    public PushReceiptJob(IServiceScopeFactory scopeFactory, ILogger<PushReceiptJob> logger)
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

        DateTime? sonTemizlik = null;
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

                var makbuz = await mediator.Send(new CheckPushReceiptsCommand(), stoppingToken);
                if (makbuz.SilinenCihaz > 0)
                {
                    _logger.LogInformation("Push makbuzları: {Cihaz} kayıtlı olmayan cihaz silindi.", makbuz.SilinenCihaz);
                }

                if (sonTemizlik is null || DateTime.UtcNow - sonTemizlik.Value >= TemizlikAraligi)
                {
                    var temizlik = await mediator.Send(new CleanupNotificationsCommand(), stoppingToken);
                    sonTemizlik = DateTime.UtcNow;

                    if (temizlik.SilinenKayit > 0 || temizlik.Atlanan > 0 || temizlik.SilinenCihaz > 0)
                    {
                        _logger.LogInformation(
                            "Bildirim defteri bakımı: {Silinen} satır silindi, {Bayat} bekleyen satır bayat işaretlendi, " +
                            "{Cihaz} oturumu kapanmış push cihazı silindi.",
                            temizlik.SilinenKayit, temizlik.Atlanan, temizlik.SilinenCihaz);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Push makbuz/bakım turu başarısız; sonraki turda tekrar denenecek.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
