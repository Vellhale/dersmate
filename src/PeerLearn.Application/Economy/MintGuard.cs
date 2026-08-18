using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Application.Economy;

/// <summary>
/// Puan basımının davranışsal tavanları.
/// </summary>
/// <remarks>
/// NEDEN VAR — VE NEDEN ATLANMAMALI.
///
/// Eski modelde bir kullanıcının açabileceği ders sayısını sınırlayan şey KREDİSİYDİ:
/// kredisi biten öğrenci ders alamazdı. Bu, kimsenin "fren" diye tasarlamadığı ama fiilen
/// tek fren olan kısıttı. Öğrenci ödemeyi bıraktığı anda o fren tamamen kalktı ve geriye
/// yalnızca takvim çakışması kontrolü kaldı.
///
/// Sonuç: iki hesap birbirine sırayla "ders anlatıp" onaylayarak sınırsız puan basabilir.
/// Üstelik öğrencinin kötü bir dersi REDDETME motivasyonu da sıfırlandı — kaybedeceği
/// kredi yok. Onay bile gerekmiyor: 48 saatlik otomatik onay yolu da basım üretiyor.
///
/// Sıfır toplam denetimi bu davranışı yakalayamaz, çünkü model artık zaten yoktan üretiyor.
/// Bu yüzden fren ekonomik değil DAVRANIŞSAL olmak zorunda: aynı çiftin birbirine ne
/// sıklıkla puan bastığına ve kullanıcı başına günlük toplama bakılıyor.
///
/// Tavanlar bilinçli olarak GENİŞ: amaç dürüst kullanıcıyı hiç rahatsız etmeden endüstriyel
/// ölçekte basımı kesmek. Günde 8 saat ders anlatan gerçek bir kullanıcı tavana çarpmaz.
/// </remarks>
public sealed class MintGuard
{
    /// <summary>
    /// Bir eğitmenin AYNI öğrenciye 24 saatlik pencerede verebileceği en fazla ders.
    /// </summary>
    /// <remarks>
    /// Karşılıklı basım her zaman aynı ikili arasında olur. Aynı kişiye aynı gün ikiden
    /// fazla ders anlatmak gerçek kullanımda nadirdir; sahte hesap çiftinde ise sınırsız
    /// tekrar tam olarak saldırının kendisidir.
    ///
    /// TAVAN YÖNLÜDÜR — ve bu, ikilinin toplam kotası DEĞİLDİR.
    /// Sayım (eğitmen, öğrenci) sırasına bakar. A→B iki ders, B→A iki ders daha demektir:
    /// aynı iki hesap 24 saatte 4 ders, yani 400 puan üretebilir. Ölçüldü (2026-08-17):
    /// roller değişince ikinci yön sıfırdan sayılıp iki isteği daha kabul ediyor.
    ///
    /// Bu bilinçli bir seçim OLARAK KALIYOR, çünkü platform akran eğitimidir: iki kişinin
    /// birbirine ders anlatması olağan kullanımın ta kendisi. Tavanı ikili bazına çevirmek
    /// karşılıklı öğreten dürüst kullanıcıları da yarıya indirirdi. Yine de sınır 2 değil
    /// fiilen 4'tür; suistimal hesabı yapılırken bu rakam kullanılmalıdır.
    /// </remarks>
    public const int MaxSessionsPerPairPerDay = 2;

    /// <summary>Bir eğitmenin 24 saatlik pencerede açabileceği en fazla ders.</summary>
    /// <remarks>
    /// 60 dakikalık derslerle 8 saat, 30 dakikalıklarla 4 saat eder — bir insanın gerçekten
    /// anlatabileceğinin üst sınırı. Aşan her şey ya veri hatası ya suistimaldir.
    ///
    /// ASIL KORUDUĞU ŞEY çift tavanından farklı: çift tavanı iki hesabın birbirini
    /// beslemesini keser, bu tavan BİR hesabın sahte öğrenci ordusundan ders toplamasını.
    /// İkincisi rezervasyon kilidi çift bazında olduğu sürece hiç çalışmıyordu — 12
    /// eşzamanlı istek 12 kabul alıyordu. Kilit eğitmen bazına alınarak düzeltildi
    /// (bkz. LockKeys.Tutor); kanıtı tools/e2e-mintguard.ps1 adım B'dir.
    /// </remarks>
    public const int MaxSessionsPerTutorPerDay = 8;

    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public MintGuard(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>
    /// Rezervasyon anında tavanları uygular. Basım anında değil REZERVASYON anında
    /// kontrol ediliyor: kullanıcıya "bu ders zaten açılamaz" demek, dersi yaptırıp
    /// sonunda puanı vermemekten çok daha dürüst.
    /// </summary>
    /// <param name="scheduledStartUtc">
    /// Açılmak istenen dersin saati. Pencere BU ANIN etrafında kuruluyor.
    /// </param>
    /// <remarks>
    /// PENCERE İKİ TARAFTAN DA SINIRLI — ve bu, ilk yazımda atlanmış bir hataydı.
    ///
    /// Sayım önce "ScheduledStartUtc >= şimdi − 24 saat" biçimindeydi; üst sınırı
    /// olmadığı için GELECEKTEKİ TÜM dersleri sayıyordu. Sonuç: aynı eğitmenden ayda bir
    /// ders alan dürüst bir öğrenci üçüncü rezervasyonunda "kotan dolu" duvarına çarpardı,
    /// üstelik dersleri ileri tarihe yaymak da kurtarmazdı. Tavan, engellemesi gereken
    /// davranışı (aynı gün üst üste ders üretme) değil, olağan kullanımı engelliyordu.
    ///
    /// Doğrusu, açılmak istenen dersin saati etrafında ±24 saatlik kayan bir pencere:
    /// bir günde yığılmayı keser, haftalara yayılmış düzenli dersi hiç görmez.
    /// </remarks>
    public async Task EnsureCanBookAsync(
        Guid tutorUserId, Guid studentUserId, DateTime scheduledStartUtc, CancellationToken ct)
    {
        var pencereBasi = scheduledStartUtc.AddDays(-1);
        var pencereSonu = scheduledStartUtc.AddDays(1);

        // Sayım BAŞLANGIÇ saatine göre: rezervasyon anında ders henüz tamamlanmadığı için
        // "tamamlanan ders" saymak tavanı işlevsiz kılardı (hepsi aynı anda açılabilirdi).
        // İptal edilenler sayılmaz — iptal, üretilmemiş bir dersin kotayı yemesi demek olurdu.
        var ciftSayisi = await _db.LessonSessions.AsNoTracking()
            .CountAsync(s => s.TutorUserId == tutorUserId
                             && s.StudentUserId == studentUserId
                             && s.ScheduledStartUtc >= pencereBasi
                             && s.ScheduledStartUtc <= pencereSonu
                             && s.Status != SessionStatus.Cancelled, ct);

        if (ciftSayisi >= MaxSessionsPerPairPerDay)
        {
            throw new AppException(ErrorCodes.MintLimitReached,
                $"Aynı eğitmenle 24 saatlik bir aralıkta en fazla {MaxSessionsPerPairPerDay} ders " +
                "planlanabilir. Daha ileri bir tarih seçebilir ya da başka bir eğitmenle devam edebilirsin.",
                statusCode: 429);
        }

        var egitmenSayisi = await _db.LessonSessions.AsNoTracking()
            .CountAsync(s => s.TutorUserId == tutorUserId
                             && s.ScheduledStartUtc >= pencereBasi
                             && s.ScheduledStartUtc <= pencereSonu
                             && s.Status != SessionStatus.Cancelled, ct);

        if (egitmenSayisi >= MaxSessionsPerTutorPerDay)
        {
            throw new AppException(ErrorCodes.MintLimitReached,
                $"Bu eğitmenin o gün için ders kotası dolu (en fazla {MaxSessionsPerTutorPerDay}). " +
                "Lütfen daha ileri bir tarih seç.",
                statusCode: 429);
        }
    }
}
