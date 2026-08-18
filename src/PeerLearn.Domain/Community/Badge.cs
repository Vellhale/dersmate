using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Community;

/// <summary>
/// Rozet kataloğu. Kayıtlar seed ile gelir; kod (Code) tekildir ve kural motoru buna bakar.
///
/// NEDEN AYRI TABLO (enum + sabit metin yerine): rozet adı/açıklaması/emojisi ürün kararıdır
/// ve deploy gerektirmeden değişebilmeli. Kural motorunun bağlandığı şey ise <see cref="Code"/>
/// — yani görünen metin değişse bile mantık kırılmaz.
/// </summary>
public class Badge : BaseEntity
{
    public BadgeCode Code { get; set; }
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;

    /// <summary>Arayüzde rozetin başına konan emoji. Görsel varlık yönetmemek için metin.</summary>
    public string Emoji { get; set; } = null!;

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <remarks>
/// BAŞARI ROZETLERİ EMEKLİ EDİLDİ (FirstLesson, FiveStarTeacher, FastResponder,
/// TenHoursTraded, VolunteerTutor).
///
/// Yerlerini UNVAN sistemi aldı: birikimli emek artık tek bir ölçüyle (TotalEarnedCredits →
/// Çırak/Öğretici/Uzman/…) anlatılıyor. Beş ayrı rozet aynı şeyi parça parça söylüyordu ve
/// hepsi aynı görsel ağırlıkta olduğu için hiçbiri okunmuyordu.
///
/// GERİYE TEK ROZET KALDI ve o bir BAŞARI DEĞİL, bir KİMLİK BEYANIDIR: FutureTeacher,
/// öğretmen adaylığı moderasyon akışına bağlıdır (beyan → inceleme → doğrulama/ret) ve
/// unvan sistemiyle ikame edilemez. Bu yüzden emekliliğin dışında tutuldu.
///
/// SAYISAL DEĞER KORUNDU: kod veritabanında METİN olarak saklanıyor (bkz.
/// BadgeConfiguration: HasConversion&lt;string&gt;), yani 4 değeri işlevsel olarak
/// önemsiz — ama değiştirmenin de hiçbir faydası yok, olası bir sayısal serileştirmede
/// sessizce anlam kaydırır.
/// </remarks>
public enum BadgeCode
{
    /// <summary>🌱 Öğretmen adayı olduğunu beyan etti (formasyon/eğitim fakültesi).</summary>
    FutureTeacher = 4
}

/// <summary>
/// Kullanıcının kazandığı rozet. Kazanım ANI saklanır: rozet sonradan kaldırılsa bile
/// "ne zaman hak etti" bilgisi kaybolmaz ve profilde kronolojik vitrin kurulabilir.
/// </summary>
/// <remarks>
/// VİTRİN (IsFeatured / MaxFeatured) KALDIRILDI. Vitrin, "çok rozet arasından üçünü seç"
/// problemini çözüyordu; emeklilikten sonra seçilecek tek bir rozet kaldı ve seçim
/// yapılamayan bir vitrin bilgi taşımaz. Alanla birlikte onu yazan uç
/// (PUT /api/profile/featured-badges) ve istemci çağrısı da kaldırıldı.
/// </remarks>
public class UserBadge : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid BadgeId { get; set; }
    public DateTime EarnedAtUtc { get; set; }

    public Badge Badge { get; set; } = null!;
}
