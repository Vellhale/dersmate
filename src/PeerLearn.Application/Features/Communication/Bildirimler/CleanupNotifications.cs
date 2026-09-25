using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Domain.Communication;

namespace PeerLearn.Application.Features.Communication.Bildirimler;

/// <summary>Bildirim defteri, mesaj kısma tablosu ve oturumu kapanmış push cihazlarının yaş temizliği.</summary>
public sealed record CleanupNotificationsCommand : IRequest<PushIsSonucu>;

/// <summary>
/// Günde bir (PushReceiptJob'un günlük adımı) ve makbuz ucuyla birlikte elle.
/// </summary>
/// <remarks>
/// ─── NE SİLİNİYOR ───────────────────────────────────────────────────────────
/// <list type="bullet">
/// <item>İşlenmiş (Sent/Skipped/Failed) ve 30 günden eski defter satırları. 30 gün en uzun
/// tekilleştirme ihtiyacından güvenle büyük: istek freni 7 gün, istek ömrü 14 gün. Satır
/// erken silinseydi aynı olayın ikinci satırı (ör. aynı çiftin freni) "ilk kez" sayılırdı.</item>
/// <item>Son gönderimi 1 günden eski kısma yuvaları: 60 saniyelik kısma için bir gün sonra
/// hiçbir anlamları yok.</item>
/// <item>Oturum bağı kopmuş (OturumBagi: bu cihazın en yeni yenileme token'ı aktif değil) ve
/// <see cref="BaglantisizCihazSaklama"/>'dan uzun süredir kaydını yenilememiş push cihazları.
/// Aşağıda ayrıca.</item>
/// </list>
///
/// ─── NEDEN BAĞLANTISIZ CİHAZLAR DA ──────────────────────────────────────────
/// Cihaz satırı çıkışta, her yerden çıkışta, banda, hesap silmede ve Expo "ölü" dediğinde
/// siliniyor. Ama üç yol bu noktaların hiçbirinden geçmiyor: uygulamayı çıkış yapmadan silip
/// oturumun 60 günde kendiliğinden dolmasını beklemek (o sürede bildirim olayı yoksa Expo'ya
/// hiç gidilmez, DeviceNotRegistered hiç gelmez), mobilin onAuthExpired yolu (orada çevrimdışı
/// çıkış işareti bilerek yazılmıyor) ve çıkıştan sonra commit olan, yarışta kalmış bir PUT.
/// Dağıtıcı bu satırlara zaten göndermiyor (oturum bağı), ama satır — token, cihaz kimliği
/// özeti, platform, tarihler — hesap silinene kadar süresiz kalıyordu. Gizlilik metni ise
/// süreli silme vaat ediyor (web ve mobil §5), KVKK m.4/2-d de amaçla sınırlı saklama istiyor:
/// oturumu kapanmış telefona bildirim göndermenin amacı yok.
///
/// Bağ kuralı TEK yerden (<see cref="OturumBagi.BagliCihaz"/>); geçici askı token'ları iptal
/// etmediği için askıdaki kullanıcının cihazı bağlı sayılır ve SİLİNMEZ (askı bitince bildirim
/// sürer). Kullanıcı yeniden giriş yaptığında mobil ilk açılışta cihazı yeniden kaydediyor.
///
/// Neden bekleme payı (<see cref="BaglantisizCihazSaklama"/>): LastSeenAtUtc her başarılı
/// kayıtta (açılış, öne geliş) yenileniyor. Pay, "bağ o an kopuk görünüyor ama kullanıcı
/// hâlâ o telefonda" gibi öngörmediğimiz bir durumda telefonun sessizce düşmesini önler;
/// uygulamayı silip 60 gün bekleyen kullanıcının satırı ise bu noktada zaten çok eski ve ilk
/// temizlikte gider. Metindeki "en geç 7 gün": 5 gün pay + temizlik aralığı (PushReceiptJob:
/// 24 saat, 15 dakikalık turlarla) ≤ 6 gün 15 dakika; sunucu kapalı kalmadıkça sınır tutar.
///
/// ─── NE İŞARETLENİYOR ───────────────────────────────────────────────────────
/// Ömrü bir günden fazla önce dolmuş ama hâlâ Pending kalan satırlar Skipped(Bayat) olur.
/// Normalde dağıtıcı onları ilk sahiplenmede zaten Bayat yazar; buraya kalan satır, sunucu
/// uzun süre kapalı kaldığında ya da dağıtım durduğunda birikenlerdir. Kirası süren (başka
/// bir turun elindeki) satıra dokunulmaz; kira dağıtıcıdaki gibi VERİTABANI saatiyle sınanır
/// (LINQ içindeki DateTime.UtcNow SQL'e now() olarak çevriliyor — değişkene alınırsa uygulama
/// saatinden bir parametreye dönüşür ve kira yine iki makinenin saatiyle karşılaştırılır).
///
/// Pending satırlar yaşları ne olursa olsun SİLİNMEZ: işlenmemiş bir olay sessizce kaybolmasın,
/// önce Bayat olarak iz bıraksın, 30 gün sonra silinsin.
/// </remarks>
public sealed class CleanupNotificationsHandler : IRequestHandler<CleanupNotificationsCommand, PushIsSonucu>
{
    public const int SaklamaGunu = 30;

    public static readonly TimeSpan BayatPayi = TimeSpan.FromDays(1);

    public static readonly TimeSpan KismaSaklama = TimeSpan.FromDays(1);

    /// <summary>
    /// Oturum bağı kopmuş cihazın, son başarılı kaydından sonra en az bu kadar beklemesi.
    /// Temizlik günde bir koştuğu için oturum kapandıktan sonra en geç 7 gün (gizlilik §5;
    /// artırılırsa web ve mobil metin de değişmeli).
    /// </summary>
    public static readonly TimeSpan BaglantisizCihazSaklama = TimeSpan.FromDays(5);

    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public CleanupNotificationsHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<PushIsSonucu> Handle(CleanupNotificationsCommand request, CancellationToken ct)
    {
        var now = DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc);
        var saklamaEsigi = now.AddDays(-SaklamaGunu);
        var bayatEsigi = now - BayatPayi;
        var kismaEsigi = now - KismaSaklama;

        var silinen = await _db.Notifications
            .Where(n => n.Status != NotificationStatus.Pending && n.ProcessedAtUtc < saklamaEsigi)
            .ExecuteDeleteAsync(ct);

        var bayat = await _db.Notifications
            .Where(n => n.Status == NotificationStatus.Pending && n.ExpiresAtUtc < bayatEsigi
                        && (n.LeaseUntilUtc == null || n.LeaseUntilUtc < DateTime.UtcNow))
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.Status, NotificationStatus.Skipped)
                .SetProperty(n => n.Outcome, (NotificationOutcome?)NotificationOutcome.Bayat)
                .SetProperty(n => n.ProcessedAtUtc, now), ct);

        silinen += await _db.MessagePushThrottles
            .Where(t => t.LastSentAtUtc < kismaEsigi)
            .ExecuteDeleteAsync(ct);

        /* Kural OturumBagi'nden OLDUĞU GİBİ alınıyor ve "bağlı olanların dışındakiler"
           deniyor (NOT IN): ifade ağacını burada olumsuzlamak kuralın ikinci bir kopyasını
           kurmak olurdu. Token'ı hiç olmayan cihaz (süresi dolan token'ları token temizliği
           siliyor) alt sorguda COALESCE(…, FALSE) ile "bağlı değil" çıkıyor — EF'in ürettiği
           SQL ölçüldü, e2e-bildirim 11. bölüm bu vakayı ayrıca sınıyor. Kimlik sütunu NULL
           olamadığı için NOT IN'in NULL tuzağı burada yok. */
        var cihazEsigi = now - BaglantisizCihazSaklama;
        var bagliCihazlar = _db.PushDevices
            .Where(OturumBagi.BagliCihaz(_db.RefreshTokens, now))
            .Select(d => d.Id);
        var silinenCihaz = await _db.PushDevices
            .Where(d => d.LastSeenAtUtc < cihazEsigi && !bagliCihazlar.Contains(d.Id))
            .ExecuteDeleteAsync(ct);

        return PushIsSonucu.Bos with { Atlanan = bayat, SilinenKayit = silinen, SilinenCihaz = silinenCihaz };
    }
}
