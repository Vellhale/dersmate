using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Catalog;

/// <summary>Ders. Örn: TYT altında "Matematik", AYT altında "Matematik".</summary>
public class Subject : BaseEntity
{
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = null!;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Bu dersin hangi branşa saydığı. Branş rozetleri (Çırak/Usta/Üstad) bu değere göre
    /// hesaplanır ve TYT ile AYT satırları AYNI branşa toplanır — "Matematik Ustası"
    /// unvanı TYT ve AYT matematiğinde geçirilen sürenin TOPLAMIYLA kazanılır.
    /// </summary>
    /// <remarks>
    /// NULL OLABİLİR ve bu bilinçli: katalogda sekiz branşın dışında kalmış eski satırlar
    /// (ör. emekli edilen "Üniversite → Fizik 1") bu alanı boş bırakır. Rozet motoru
    /// branşsız dersleri saymaz, ama satır silinmediği için o derslere bağlı geçmiş ders
    /// kayıtları da kırılmaz.
    ///
    /// Rozet mantığı neden <see cref="Name"/> yerine buna bakıyor: ders adı bir ürün
    /// kararıdır ve değişebilir ("Matematik" → "Temel Matematik"). Ada bağlı bir hesap,
    /// böyle bir yeniden adlandırmada sessizce sıfırlanırdı.
    /// </remarks>
    public SubjectBranch? Branch { get; set; }

    public EducationCategory Category { get; set; } = null!;
    public ICollection<Topic> Topics { get; set; } = new List<Topic>();
}

/// <summary>Ürünün izin verdiği sekiz ders. Katalog bunların dışına çıkmaz.</summary>
/// <remarks>
/// Veritabanında METİN olarak saklanır (HasConversion&lt;string&gt; — proje geneli kural,
/// bkz. docs/ASAMA-1-MIMARI.md). Üye EKLEMEK göç gerektirmez; üye SİLMEK, o değeri taşıyan
/// satırlar okunurken dönüştürme hatası verir ve rozet okuyan her sorguyu düşürür
/// (aynı tuzak BadgeCode'da yaşandı, bkz. DbSeeder rozet notu). Silmeden önce satırları taşı.
/// </remarks>
public enum SubjectBranch
{
    Turkce = 0,
    Tarih = 1,
    Cografya = 2,
    Matematik = 3,
    Geometri = 4,
    Fizik = 5,
    Kimya = 6,
    Biyoloji = 7
}

/// <summary>
/// Branş enum'ından kullanıcıya görünen ders adı. Rozet başlıkları, katalog tohumlaması ve
/// profil ekranı hep buradan okur.
/// </summary>
/// <remarks>
/// DOMAIN'DE DURUYOR, Infrastructure'daki müfredat dosyasında DEĞİL — çünkü Application
/// katmanı da buna ihtiyaç duyuyor (rozet başlığı üretirken) ve Clean Architecture gereği
/// Application, Infrastructure'a bağımlı olamaz. Ad bir ürün kararı ama aynı zamanda
/// alan diline ait bir terim; doğru yeri burası.
/// </remarks>
public static class SubjectBranchNames
{
    public static string Of(SubjectBranch branch) => branch switch
    {
        SubjectBranch.Turkce => "Türkçe",
        SubjectBranch.Tarih => "Tarih",
        SubjectBranch.Cografya => "Coğrafya",
        SubjectBranch.Matematik => "Matematik",
        SubjectBranch.Geometri => "Geometri",
        SubjectBranch.Fizik => "Fizik",
        SubjectBranch.Kimya => "Kimya",
        SubjectBranch.Biyoloji => "Biyoloji",
        _ => branch.ToString()
    };
}
