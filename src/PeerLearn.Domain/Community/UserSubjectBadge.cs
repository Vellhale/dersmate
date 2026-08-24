using PeerLearn.Domain.Catalog;
using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Community;

/// <summary>
/// Branş rozeti seviyeleri. Eşik, o branşta ANLATILAN toplam süredir (dakika cinsinden
/// ölçülür, saat olarak konuşulur).
///
/// ÜÇ KADEME İKİYE İNDİ (2026-08-24, ürün kararı). Eski merdiven 5 / 20 / 50 saatti ve
/// iki ucu da işe yaramıyordu: 5 saat rozeti neredeyse herkeste vardı (ayırt etmiyordu),
/// 50 saat ise pratikte kimsenin ulaşamadığı bir sayıydı. İki kademe — 8 ve 15 saat —
/// hem gerçekten kazanılabilir hem de kazanıldığında bir şey söylüyor.
///
/// ⚠️ ENUM ÜYELERİ VERİTABANINDA METİN OLARAK SAKLANIYOR (HasConversion&lt;string&gt;).
/// Eski üyeler (Cirak, Usta) kaldırıldığı için o metni taşıyan satırlar okunamaz hâle
/// gelirdi; ayrıca "Ustad" adı KALDI ama EŞİĞİ değişti (50 saat → 15 saat), yani eski
/// satırların anlamı da bozuldu. Bu yüzden göç, şemaya dokunmadan ÖNCE tüm rozet
/// satırlarını tamamlanmış derslerden yeniden hesaplıyor
/// (20260824_SubjectBadgeTwoTiers).
/// </summary>
public enum SubjectBadgeLevel
{
    /// <summary>8 saat anlatım — "Matematik Öğretici" (gümüş).</summary>
    Ogretici = 1,

    /// <summary>15 saat ve üzeri anlatım — "Matematik Üstadı" (altın).</summary>
    Ustad = 2
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
    public const int OgreticiMinutes = 8 * 60;
    public const int UstadMinutes = 15 * 60;

    /// <summary>Verilen dakikayla hak edilen TÜM seviyeler (kümülatif: Üstad olan Öğretici de olur).</summary>
    /// <remarks>
    /// ALT SEVİYE DE VERİLİR. 15 saati bir kerede dolduran bir eğitmenin profilinde
    /// yalnızca Üstad görünüp Öğretici hiç görünmemesi, rozet geçmişini kazanım anıyla
    /// birlikte saklama amacını boşa çıkarırdı. Arayüz zaten en yükseğini öne çıkarır.
    /// </remarks>
    public static IEnumerable<SubjectBadgeLevel> EarnedLevels(int minutes)
    {
        if (minutes >= OgreticiMinutes) yield return SubjectBadgeLevel.Ogretici;
        if (minutes >= UstadMinutes) yield return SubjectBadgeLevel.Ustad;
    }

    public static int RequiredMinutes(SubjectBadgeLevel level) => level switch
    {
        SubjectBadgeLevel.Ogretici => OgreticiMinutes,
        SubjectBadgeLevel.Ustad => UstadMinutes,
        _ => int.MaxValue
    };

    /// <summary>"Matematik Öğretici", "Tarih Üstadı".</summary>
    public static string Title(string subjectDisplayName, SubjectBadgeLevel level) => level switch
    {
        SubjectBadgeLevel.Ogretici => $"{subjectDisplayName} Öğretici",
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
/// 8 branş × 2 seviye = 16 satırlık türetilmiş bir küme; katalogda tutmak, her müfredat
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
    /// aynı rozete sayarlar. SubjectId'ye bağlansaydı aynı eğitmen "Matematik Öğretici"
    /// rozetini iki kez kazanır, profilde iki kez görünürdü.
    /// </remarks>
    public SubjectBranch Branch { get; set; }

    public SubjectBadgeLevel Level { get; set; }

    public DateTime EarnedAtUtc { get; set; }

    /// <summary>Rozet verildiği anda ölçülen toplam anlatım süresi (dakika). Yalnızca denetim için.</summary>
    public int MinutesAtAward { get; set; }
}
