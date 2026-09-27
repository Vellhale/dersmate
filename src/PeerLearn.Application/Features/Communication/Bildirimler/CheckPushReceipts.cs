using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PeerLearn.Application.Abstractions;

namespace PeerLearn.Application.Features.Communication.Bildirimler;

/// <summary>
/// Expo biletlerinin makbuzlarını sorar: teslim sağlayıcıya (FCM/APNs) ulaştı mı, cihaz
/// hâlâ kayıtlı mı.
/// </summary>
public sealed record CheckPushReceiptsCommand : IRequest<PushIsSonucu>;

/// <summary>
/// 15 dakikada bir (PushReceiptJob) ve elle (POST admin/jobs/push-receipts).
/// </summary>
/// <remarks>
/// ─── NEDEN MAKBUZ ───────────────────────────────────────────────────────────
/// Biletteki "ok" yalnızca Expo'nun isteği KABUL ettiğini söylüyor. Uygulaması silinmiş bir
/// telefonun token'ı çoğu zaman bilette değil makbuzda DeviceNotRegistered olarak görünür.
/// Makbuz sorulmazsa ölü cihazlar sonsuza kadar kayıtlı kalır ve her bildirimde Expo'ya
/// boşuna gider; Expo da ölü token'a ısrarla gönderen göndericiyi kısabiliyor.
///
/// ─── NE ZAMAN ───────────────────────────────────────────────────────────────
/// En az 15 dakikalık biletler sorulur (makbuz hemen hazır olmuyor); 24 saatten eski biletler
/// SORULMADAN silinir, Expo makbuzları 24 saatte temizliyor
/// (docs.expo.dev/push-notifications/sending-notifications). Sözlükte olmayan bilet henüz
/// hazır değildir, yerinde kalır ve sonraki turda yeniden sorulur.
///
/// Her makbuzda bilet silinir: ok → iş bitti; DeviceNotRegistered → cihaz satırı da silinir;
/// diğer hatalar maskeli olarak günlüğe yazılır ve bilet silinir — aynı hatayı her turda
/// yeniden sormanın faydası yok.
///
/// ⚠️ Cihaz silmesi biletten SONRA yenilenmemiş satırla sınırlı (<see cref="OluCihaz"/>): makbuz
/// saatler sonra gelebiliyor ve arada aynı token'la yeniden kaydolmuş (iOS'ta yeniden kurulum)
/// geçerli cihaz, eski kurulumun ölüm haberiyle silinmemeli.
/// </remarks>
public sealed class CheckPushReceiptsHandler : IRequestHandler<CheckPushReceiptsCommand, PushIsSonucu>
{
    /// <summary>Bilet bundan gençse sorulmaz: makbuz henüz oluşmamış olabilir.</summary>
    public static readonly TimeSpan EnErken = TimeSpan.FromMinutes(15);

    /// <summary>Bilet bundan yaşlıysa sorulmadan silinir: Expo makbuzu zaten sildi.</summary>
    public static readonly TimeSpan EnGec = TimeSpan.FromHours(24);

    /// <summary>
    /// Bir turdaki en fazla makbuz isteği (her biri 1000 bilet). Tavanın üstü sonraki tura
    /// kalır; bu hacim tasarlanan ölçeğin çok üstünde.
    /// </summary>
    public const int TurBasinaEnFazlaIstek = 10;

    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly IPushGonderici _gonderici;
    private readonly ILogger<CheckPushReceiptsHandler> _logger;

    public CheckPushReceiptsHandler(
        IAppDbContext db, IClock clock, IPushGonderici gonderici, ILogger<CheckPushReceiptsHandler> logger)
    {
        _db = db;
        _clock = clock;
        _gonderici = gonderici;
        _logger = logger;
    }

    public async Task<PushIsSonucu> Handle(CheckPushReceiptsCommand request, CancellationToken ct)
    {
        var now = DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc);
        var enEski = now - EnGec;
        var enYeni = now - EnErken;

        var silinenKayit = await _db.PushTickets.Where(t => t.CreatedAtUtc < enEski).ExecuteDeleteAsync(ct);
        var silinenCihaz = 0;

        // Anahtar kümesi (CreatedAtUtc, Id): hazır olmayan biletler yerinde kalıyor, aynı
        // turda ikinci kez sorulmasınlar diye son görülenden ileri gidilir.
        DateTime? sonAn = null;
        var sonId = Guid.Empty;

        for (var istek = 0; istek < TurBasinaEnFazlaIstek; istek++)
        {
            var q = _db.PushTickets.AsNoTracking().Where(t => t.CreatedAtUtc >= enEski && t.CreatedAtUtc <= enYeni);
            if (sonAn is { } sa)
            {
                var si = sonId;
                q = q.Where(t => t.CreatedAtUtc > sa || (t.CreatedAtUtc == sa && t.Id.CompareTo(si) > 0));
            }

            var biletler = await q
                .OrderBy(t => t.CreatedAtUtc).ThenBy(t => t.Id)
                .Take(PushSinirlari.EnFazlaBilet)
                .Select(t => new { t.Id, t.TicketId, t.PushDeviceId, t.CreatedAtUtc })
                .ToListAsync(ct);

            if (biletler.Count == 0)
            {
                break;
            }

            sonAn = biletler[^1].CreatedAtUtc;
            sonId = biletler[^1].Id;

            var makbuzlar = await _gonderici.MakbuzlariAlAsync(biletler.Select(b => b.TicketId).ToList(), ct);
            if (makbuzlar.Count == 0)
            {
                // Ya hiçbiri hazır değil ya da istek düştü (gönderici hatayı günlüğe yazdı).
                // Sonraki sayfalar daha yeni biletler; onlar da hazır olmayacak.
                break;
            }

            var silinecekBiletler = new List<Guid>();

            // Cihaz → ölüm haberi veren EN YENİ biletin yazıldığı an. Biletten sonra yeniden
            // kaydolmuş satır silinmez; aynı cihaza birden çok bilet varsa en yenisi belirler
            // (yeniden kayıttan SONRA gönderilmiş bir bilet de ölü diyorsa cihaz gerçekten ölü).
            var oluCihazlar = new Dictionary<Guid, DateTime>();

            foreach (var b in biletler)
            {
                if (!makbuzlar.TryGetValue(b.TicketId, out var makbuz))
                {
                    continue;
                }

                silinecekBiletler.Add(b.Id);

                if (makbuz.Basarili)
                {
                    continue;
                }

                if (makbuz.HataKodu == PushHataKurali.CihazKayitliDegil)
                {
                    var biletAni = DateTime.SpecifyKind(b.CreatedAtUtc, DateTimeKind.Utc);
                    if (!oluCihazlar.TryGetValue(b.PushDeviceId, out var onceki) || biletAni > onceki)
                    {
                        oluCihazlar[b.PushDeviceId] = biletAni;
                    }

                    continue;
                }

                // Maskeli: gönderici zaten maskeliyor, burada ikinci kez (tam token günlüğe düşmesin).
                _logger.Log(
                    PushHataKurali.YapilandirmaHatasi(makbuz.HataKodu) ? LogLevel.Error : LogLevel.Warning,
                    "Push makbuzu hata döndü: {Hata} (bilet {Bilet}).",
                    PushHataKurali.SonHata(makbuz.HataKodu, makbuz.HataMesaji), b.TicketId);
            }

            if (oluCihazlar.Count > 0)
            {
                silinenCihaz += await OluCihaz.SilAsync(_db, oluCihazlar, ct);
            }

            if (silinecekBiletler.Count > 0)
            {
                silinenKayit += await _db.PushTickets.Where(t => silinecekBiletler.Contains(t.Id)).ExecuteDeleteAsync(ct);
            }

            if (biletler.Count < PushSinirlari.EnFazlaBilet)
            {
                break;
            }
        }

        return PushIsSonucu.Bos with { SilinenCihaz = silinenCihaz, SilinenKayit = silinenKayit };
    }
}
