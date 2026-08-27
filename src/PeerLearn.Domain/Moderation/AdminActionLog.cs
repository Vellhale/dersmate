using PeerLearn.Domain.Common;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Domain.Moderation;

/// <summary>
/// Yönetim/hakem işlemlerinin denetim izi (audit trail). SALT EKLENİR — güncellenmez, silinmez.
///
/// NEDEN GEREKLİ: bu panelden verilen kararlar para benzeri bir varlığı (krediyi) taraflar
/// arasında hareket ettiriyor ve kalıcı ban verebiliyor. Kimin, ne zaman, hangi gerekçeyle
/// karar verdiği kaydedilmezse; hatalı bir kararı geri almak, kötüye kullanan bir moderatörü
/// tespit etmek veya kullanıcıya "kararınız şu gerekçeyle verildi" demek mümkün olmaz.
/// Disputes tablosundaki ResolvedByAdminId yalnızca SON durumu tutar; burada ise her işlem
/// ayrı satırdır (bir itiraz önce UnderReview'a alınıp sonra karara bağlanabilir).
/// </summary>
public class AdminActionLog : BaseEntity
{
    public Guid ActorUserId { get; set; }

    /// <summary>
    /// İşlem ANINDAKİ rol. Users.Role'a bakmak yetmez: rol sonradan değişirse geçmiş
    /// kararlar yanlış yetkiyle yapılmış gibi görünürdü.
    /// </summary>
    public UserRole ActorRole { get; set; }

    public AdminActionType Action { get; set; }

    /// <summary>Hedef kayıt türü: "Dispute" | "User" | "HwidBan" | "LessonSession".</summary>
    public string TargetType { get; set; } = null!;

    public Guid? TargetId { get; set; }

    /// <summary>İnsan tarafından okunabilir özet; panelde satır olarak gösterilir.</summary>
    public string Summary { get; set; } = null!;

    /// <summary>
    /// Karara özgü ek alanlar (jsonb): tutar, önceki/sonraki durum, gerekçe notu…
    /// Şemayı her yeni işlem türünde değiştirmemek için serbest biçimli.
    /// </summary>
    public string? MetadataJson { get; set; }

    /// <summary>
    /// İstemcinin ürettiği tekillik anahtarı. Aynı anahtarla gelen ikinci istek işlemi
    /// TEKRAR UYGULAMAZ, ilk sonucu döndürür.
    /// </summary>
    /// <remarks>
    /// NEDEN DENETİM İZİNDE, AYRI BİR TABLODA DEĞİL:
    /// tekillik kaydının ömrü ile denetim izinin ömrü aynı olmalı. Ayrı bir tabloda
    /// tutulsaydı, o tablo temizlendiği anda çok eski bir isteğin tekrarı sessizce
    /// yeniden uygulanırdı — üstelik tam da unutulmuş, kimsenin beklemediği bir anda.
    /// Denetim izi hiç silinmediği için (salt eklenir) koruma da kalıcı oluyor.
    /// Ayrıca zaten işlem başına BİR satır yazılıyor; kopya veri üretmeye gerek yok.
    ///
    /// Boş bırakılabilir: eski satırlarda yok ve tekillik istemeyen işlemler (ban, rol)
    /// hâlâ anahtarsız yazıyor. Tekillik kısıtı bu yüzden KISMİ index'tir.
    /// </remarks>
    public string? IdempotencyKey { get; set; }
}

public enum AdminActionType
{
    DisputeReviewStarted = 0,
    DisputeResolved = 1,
    UserBanned = 2,
    UserUnbanned = 3,
    HwidBanned = 4,
    HwidUnbanned = 5,
    RoleChanged = 6,

    /// <summary>Admin ucundan elle tetiklenen arka plan işi (test/operasyon).</summary>
    JobTriggered = 7,

    /// <summary>Öğretmen adaylığı beyanı belgeyle doğrulandı.</summary>
    TeacherCandidateVerified = 8,

    /// <summary>Öğretmen adaylığı beyanı uygun bulunmadı.</summary>
    TeacherCandidateRejected = 9,

    /// <summary>Öğretmen adaylığı kararı geri alındı (beyan yeniden incelemeye döndü).</summary>
    TeacherCandidateReviewReverted = 10,

    /// <summary>Uyarı ya da süreli askı (kalıcı ban ayrı: UserBanned).</summary>
    UserSanctioned = 11,

    /// <summary>
    /// Yönetim eliyle puan tanımlandı ya da düşüldü (AdminAdjustment hareketi).
    /// Defteri kullanıcı akışları dışında değiştiren TEK işlem budur; izi olmadan
    /// yapılabildiği sürece "elle SQL" tek çareydi.
    /// </summary>
    CreditAdjusted = 12,

    /// <summary>Tek yönlü şikayet incelendi ve kapatıldı (yaptırım ayrıca kaydedilir).</summary>
    ReportReviewed = 13,

    /// <summary>
    /// Forum içeriği (gönderi/yorum) yönetim kararıyla kaldırıldı ya da geri getirildi.
    ///
    /// ŞİKAYETİ KAPATMAKTAN AYRI BİR KAYIT ve ayrı olması gerekiyor: şikayetin
    /// kapatılması bir İNCELEME kaydı, içeriğin kaldırılması bir MÜDAHALE. İkisi tek
    /// satıra sıkıştırılsaydı "şikayet kapatıldı" izine bakan biri, içeriğe dokunulup
    /// dokunulmadığını göremezdi.
    /// </summary>
    ForumContentModerated = 14
}
