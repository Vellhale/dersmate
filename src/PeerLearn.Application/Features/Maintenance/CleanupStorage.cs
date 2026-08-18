using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PeerLearn.Application.Abstractions;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Application.Features.Maintenance;

/// <summary>
/// Depo bakımı: saklama süresi dolan kanıt görsellerini siler ve hiçbir kayda bağlı
/// olmayan artık dosyaları temizler.
/// </summary>
/// <remarks>
/// NEDEN GEREKLİ: yükleme ile silme aynı işlemde YAPILAMAZ. Profil fotoğrafı değişince
/// eski dosya bilerek bırakılıyor (bkz. UpdateAvatarHandler notu) çünkü dosya silme ile
/// satır güncelleme atomik değildir; yarıda kalan bir silme, profili var olmayan bir
/// dosyaya bağlardı. Aynı şekilde yükleme başarılı olup DB yazımı düşerse dosya öksüz
/// kalır. Bu iki sızıntı da küçük ama BİRİKİMLİDİR: temizleyen bir şey olmazsa depo
/// yalnızca büyür ve maliyeti kimse fark etmeden artar.
///
/// İki fazı da tek işte tutmanın sebebi, ikinci fazın birincinin ARTIĞINI da toplaması:
/// faz 1 satırı "içerik silindi" diye damgalayıp dosyayı silmeye çalışır; silme düşerse
/// dosya artık hiçbir satırdan referanslı değildir ve faz 2 onu yakalar.
/// </remarks>
public sealed record CleanupStorageCommand : IRequest<CleanupStorageResult>;

public sealed record CleanupStorageResult(int ProofsPurged, int OrphansDeleted, int SweepFailuresPruned);

public sealed class CleanupStorageHandler : IRequestHandler<CleanupStorageCommand, CleanupStorageResult>
{
    /// <summary>
    /// Kanıt görselinin saklanma süresi. İtiraz penceresi (48 saat) ve hakem incelemesi
    /// çoktan kapanmış olur; bu süre "sonradan çıkan bir şikâyette geriye bakabilmek" için.
    /// </summary>
    private const int ProofRetentionDays = 180;

    /// <summary>
    /// Bir dosya bu yaştan GENÇSE artık sayılmaz. Kritik: dosya depoya yazıldıktan sonra
    /// DB satırı yazılana kadar geçen kısa sürede dosya referanssızdır — bu pencere olmadan
    /// temizlik, o anda yüklenmekte olan kanıtı silerdi.
    /// </summary>
    private const int OrphanGraceDays = 7;

    /// <summary>
    /// Tek turda silinebilecek en fazla artık dosya. Bir mantık hatası (ör. referans
    /// kümesinin yanlış doldurulması) tüm depoyu tek seferde süpüremesin diye.
    /// </summary>
    private const int MaxDeletionsPerRun = 5_000;

    /// <summary>Bu kadar süredir dokunulmamış süpürme-hatası satırları temizlenir.</summary>
    private const int SweepFailureRetentionDays = 30;

    private static readonly SessionStatus[] NihaiDurumlar =
    [
        SessionStatus.Completed,
        SessionStatus.Cancelled,
        SessionStatus.Expired
    ];

    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly IProofStorage _storage;
    private readonly ILogger<CleanupStorageHandler> _logger;

    public CleanupStorageHandler(IAppDbContext db, IClock clock, IProofStorage storage,
        ILogger<CleanupStorageHandler> logger)
    {
        _db = db;
        _clock = clock;
        _storage = storage;
        _logger = logger;
    }

    public async Task<CleanupStorageResult> Handle(CleanupStorageCommand request, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        var purged = await SaklamaSuresiDolanlariSilAsync(now, ct);
        var orphans = await ArtiklariSupurAsync(now, ct);
        var pruned = await EskiSupurmeHatalariniSilAsync(now, ct);

        if (purged > 0 || orphans > 0 || pruned > 0)
        {
            _logger.LogInformation(
                "Depo bakımı: {Purged} kanıt görseli süresi doldu, {Orphans} artık dosya silindi, " +
                "{Pruned} eski süpürme-hatası kaydı temizlendi.", purged, orphans, pruned);
        }

        return new CleanupStorageResult(purged, orphans, pruned);
    }

    /// <summary>
    /// Faz 1 — saklama süresi dolan kanıt GÖRSELLERİ. Satır silinmez: hash geçmişi sahte
    /// kanıt tespitinin tek dayanağı (bkz. SessionProof.ContentDeletedAtUtc notu).
    /// </summary>
    private async Task<int> SaklamaSuresiDolanlariSilAsync(DateTime now, CancellationToken ct)
    {
        var esik = now.AddDays(-ProofRetentionDays);

        /*
          Yalnızca NİHAİ durumdaki derslerin kanıtı silinir. Devam eden ya da İTİRAZLI bir
          derste kanıt, kredinin kaderini belirleyen delildir — saklama süresi dolsa bile
          hakem karar verene kadar durmalı. (Disputed nihai durumlar listesinde yok.)
        */
        /*
          İKİ SORGU — VE SEBEBİ BİR TUZAK.

          Tek sorguda birleşim yapılıp entity doğrudan çekiliyordu. AsNoTracking yalnızca
          birleşimin KARŞI tarafında (LessonSessions) çağrılmıştı ama EF'te izleme davranışı
          SORGU DÜZEYİNDEDİR: sorgunun herhangi bir yerindeki AsNoTracking TÜM sonucu
          izlenmeyen yapar. Sonuç: aşağıdaki damga atanıyor, SaveChanges hiçbir şey yazmıyor,
          dosya siliniyordu — yani içerik gidiyor ama satır "duruyor" diyordu. Test bunu
          yakaladı; sessizce geçseydi kullanıcıya "kanıt bulunamadı" diye görünürdü.

          Kimlikler izlenmeyen sorgudan alınıp entity'ler AYRI ve tamamen izlenen bir
          sorguyla yükleniyor. Niyet artık okunur: hangi satırların yazılacağı belli.
        */
        var adayIdler = await (
                from p in _db.SessionProofs.AsNoTracking()
                join s in _db.LessonSessions.AsNoTracking() on p.SessionId equals s.Id
                where p.ContentDeletedAtUtc == null
                      && p.CreatedAtUtc <= esik
                      && NihaiDurumlar.Contains(s.Status)
                orderby p.CreatedAtUtc
                select p.Id)
            .Take(MaxDeletionsPerRun)
            .ToListAsync(ct);

        if (adayIdler.Count == 0)
        {
            return 0;
        }

        var adaylar = await _db.SessionProofs
            .Where(p => adayIdler.Contains(p.Id))
            .ToListAsync(ct);

        /*
          SIRA: önce damga + SaveChanges, SONRA dosya silme.

          Ters sırada bir çökme "dosya yok ama satır var diyor" bırakırdı; kullanıcı kanıtı
          açmaya çalışır ve sistem sebebini açıklayamaz. Bu sırada çökme ise "satır silindi
          diyor ama dosya duruyor" bırakır — zararsız, üstelik faz 2 o dosyayı zaten toplar.
        */
        foreach (var proof in adaylar)
        {
            proof.ContentDeletedAtUtc = now;
        }

        await _db.SaveChangesAsync(ct);

        var silinen = 0;
        foreach (var proof in adaylar)
        {
            try
            {
                await _storage.DeleteAsync(proof.StorageKey, ct);
                silinen++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Damga zaten atıldı; dosya faz 2'de artık olarak yakalanacak. Tur durmaz.
                _logger.LogWarning(ex, "Kanıt dosyası silinemedi ({Key}); artık taramasına bırakıldı.",
                    proof.StorageKey);
            }
        }

        return silinen;
    }

    /// <summary>
    /// Faz 2 — işaretle ve süpür: hiçbir DB kaydının işaret etmediği dosyalar.
    /// </summary>
    private async Task<int> ArtiklariSupurAsync(DateTime now, CancellationToken ct)
    {
        var referansli = new HashSet<string>(StringComparer.Ordinal);

        var avatarlar = await _db.Users.AsNoTracking()
            .Where(u => u.AvatarUrl != null)
            .Select(u => u.AvatarUrl!)
            .ToListAsync(ct);

        var kanitlar = await _db.SessionProofs.AsNoTracking()
            .Where(p => p.ContentDeletedAtUtc == null)
            .Select(p => p.StorageKey)
            .ToListAsync(ct);

        foreach (var key in avatarlar) referansli.Add(key);
        foreach (var key in kanitlar) referansli.Add(key);

        /*
          EMNİYET SÜRGÜSÜ. Referans kümesinin boş çıkması, "her dosya artık" demek DEĞİL;
          neredeyse her zaman bir hatadır (yanlış bağlantı dizesi, sorgunun bozulması,
          şema kayması). Bu durumda hiçbir şey silinmez ve hata olarak loglanır — bir
          temizlik işinin yapabileceği en kötü şey, doğru dosyaları silmektir.
        */
        if (referansli.Count == 0)
        {
            var depoDoluMu = false;
            await foreach (var _ in _storage.ListAsync(ct))
            {
                depoDoluMu = true;
                break;
            }

            if (depoDoluMu)
            {
                _logger.LogError(
                    "Artık taraması İPTAL: veritabanında hiç referanslı dosya yok ama depo dolu. " +
                    "Bu bir veri/bağlantı sorunudur; silme yapılmadı.");
                return 0;
            }

            return 0;
        }

        var yasEsigi = now.AddDays(-OrphanGraceDays);
        var silinen = 0;

        await foreach (var nesne in _storage.ListAsync(ct))
        {
            if (silinen >= MaxDeletionsPerRun)
            {
                // Sessiz kesme yok: kalanın bir sonraki turda geleceği SÖYLENİR.
                _logger.LogWarning(
                    "Artık taraması {Limit} dosyada durduruldu; kalanlar sonraki turda silinecek.",
                    MaxDeletionsPerRun);
                break;
            }

            // Genç dosya: DB satırı henüz yazılmamış olabilir (yükleme yarışı).
            if (nesne.LastModifiedUtc > yasEsigi || referansli.Contains(nesne.Key))
            {
                continue;
            }

            try
            {
                await _storage.DeleteAsync(nesne.Key, ct);
                silinen++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Artık dosya silinemedi ({Key}).", nesne.Key);
            }
        }

        return silinen;
    }

    /// <summary>
    /// Faz 3 — uzun süredir dokunulmamış süpürme-hatası kayıtları. Kayıt sorgudan düşünce
    /// (ör. ders elle düzeltildi) satır geride kalır; süresiz birikmesinin anlamı yok.
    /// </summary>
    private async Task<int> EskiSupurmeHatalariniSilAsync(DateTime now, CancellationToken ct)
    {
        var esik = now.AddDays(-SweepFailureRetentionDays);

        return await _db.SweepFailures
            .Where(f => f.LastFailedAtUtc <= esik)
            .ExecuteDeleteAsync(ct);
    }
}
