using PeerLearn.Domain.Catalog;
using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Community;

/// <summary>
/// Branş rozeti seviyeleri. Eşik, o branşta ANLATILAN toplam süredir (dakika cinsinden
/// ölçülür, saat olarak konuşulur).
/// </summary>
public enum SubjectBadgeLevel
{
    /// <summary>5 saat anlatım — "Matematik Çırağı".</summary>
    Cirak = 1,

    /// <summary>20 saat anlatım — "Fizik Ustası".</summary>
    Usta = 2,

    /// <summary>50 saat ve üzeri anlatım — "Tarih Üstadı".</summary>
    Ustad = 3
}

/// <summary>
/// Branş rozeti kuralları. Eşikler tek yerde: motor, testler ve arayüz metni buradan okur.
/// </summary>
/// <remarks>
/// EŞİKLER DAKİKA CİNSİNDEN TUTULUYOR, SAAT DEĞİL. Ders süresi
/// <see cref="Scheduling.LessonSession.DurationMinutes"/> ile dakika olarak kaydediliyor;
/// saate çevirip karşılaştırmak, 30 dakikalık derslerde tam sayı bölmesi yüzünden süre
/// kaybettirirdi (iki adet 30 dk ders = 1 saat, ama 0 saat + 0 saat = 0).
/// </remarks>
public static class SubjectBadgeRules
{
    public const int CirakMinutes = 5 * 60;
    public const int UstaMinutes = 20 * 60;
    public const int UstadMinutes = 50 * 60;

    /// <summary>Verilen dakikayla hak edilen TÜM seviyeler (kümülatif: Üstad olan Usta da olur).</summary>
    /// <remarks>
    /// ALT SEVİYELER DE VERİLİR. 50 saati bir kerede dolduran bir eğitmenin profilinde
    /// yalnızca Üstad görünüp Çırak/Usta hiç görünmemesi, rozet geçmişini kazanım anıyla
    /// birlikte saklama amacını boşa çıkarırdı. Arayüz zaten en yükseğini öne çıkarır.
    /// </remarks>
    public static IEnumerable<SubjectBadgeLevel> EarnedLevels(int minutes)
    {
        if (minutes >= CirakMinutes) yield return SubjectBadgeLevel.Cirak;
        if (minutes >= UstaMinutes) yield return SubjectBadgeLevel.Usta;
        if (minutes >= UstadMinutes) yield return SubjectBadgeLevel.Ustad;
    }

    public static int RequiredMinutes(SubjectBadgeLevel level) => level switch
    {
        SubjectBadgeLevel.Cirak => CirakMinutes,
        SubjectBadgeLevel.Usta => UstaMinutes,
        SubjectBadgeLevel.Ustad => UstadMinutes,
        _ => int.MaxValue
    };

    /// <summary>"Matematik Çırağı", "Fizik Ustası", "Tarih Üstadı".</summary>
    public static string Title(string subjectDisplayName, SubjectBadgeLevel level) => level switch
    {
        SubjectBadgeLevel.Cirak => $"{subjectDisplayName} Çırağı",
        SubjectBadgeLevel.Usta => $"{subjectDisplayName} Ustası",
        SubjectBadgeLevel.Ustad => $"{subjectDisplayName} Üstadı",
        _ => subjectDisplayName
    };
}

/// <summary>
/// Kullanıcının bir branşta kazandığı rozet. Her (kullanıcı, branş, seviye) üçlüsü tektir.
/// </summary>
/// <remarks>
/// NEDEN AYRI TABLO, <see cref="UserBadge"/>'e EKLENMİŞ BİR SATIR DEĞİL: UserBadge katalog
/// tablosuna (Badges) FK ile bağlı ve o katalog ürün metniyle yönetiliyor. Branş rozeti ise
/// 8 branş × 3 seviye = 24 satırlık türetilmiş bir küme; katalogda tutmak, her müfredat
/// değişikliğinde katalog göçü gerektirirdi.
///
/// SAYAÇ KOLONU YOK — ve bu bilinçli. Ürün isteği "anlatım saati sayacını güncelle" diyordu;
/// denormalize bir sayaç, veri elle düzeltildiğinde ya da bir olay kaçırıldığında kalıcı
/// olarak yanlış rozet bırakır. Bu projede aynı karar <see cref="Community"/> motorunda
/// zaten verilmiş: rozetler olay anında artırılarak değil, MEVCUT VERİDEN yeniden hesaplanır
/// (bkz. BadgeEngine sınıf notu). Süre her seferinde tamamlanmış derslerden toplanıyor;
/// tek doğruluk kaynağı LessonSessions.
///
/// <see cref="MinutesAtAward"/> bunun istisnası gibi görünür ama değil: o bir sayaç değil,
/// kazanım anının FOTOĞRAFI — denetim ve "neden bu rozeti aldım" sorusu için. Hiçbir karar
/// bu alana bakarak verilmez.
/// </remarks>
public class UserSubjectBadge : BaseEntity
{
    public Guid UserId { get; set; }

    /// <summary>Sekiz branştan biri. Katalogdaki Subject satırına DEĞİL, branşa bağlanır.</summary>
    /// <remarks>
    /// SubjectId kullanılmadı: TYT Matematik ve AYT Matematik ayrı Subject satırlarıdır ama
    /// aynı rozete sayarlar. SubjectId'ye bağlansaydı aynı eğitmen "Matematik Çırağı"
    /// rozetini iki kez kazanır, profilde iki kez görünürdü.
    /// </remarks>
    public SubjectBranch Branch { get; set; }

    public SubjectBadgeLevel Level { get; set; }

    public DateTime EarnedAtUtc { get; set; }

    /// <summary>Rozet verildiği anda ölçülen toplam anlatım süresi (dakika). Yalnızca denetim için.</summary>
    public int MinutesAtAward { get; set; }
}
