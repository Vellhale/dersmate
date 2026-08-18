using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Identity;

/// <summary>
/// Kullanıcı başına tek satır: ürün turu (onboarding) durumu + KVKK/GDPR çerez rızası.
///
/// NEDEN SUNUCUDA TUTULUYOR (yalnızca localStorage değil):
///   1. Rıza kanıtı. KVKK/GDPR'da ispat yükümlülüğü veri sorumlusundadır; "kullanıcı onay
///      verdi" demek yetmez, NE ZAMAN ve HANGİ METİN SÜRÜMÜNE onay verdiği gösterilmelidir.
///      localStorage kullanıcı tarafından silinebilir/değiştirilebilir, kanıt değeri yoktur.
///   2. Cihaz değişince tur baştan başlamasın, rıza yeniden sorulmasın.
///
/// localStorage yine de KULLANILIR ve gereklidir: kullanıcı giriş yapmadan önce de banner
/// çıkar ve o anda kimlik yoktur. Akış şu: anonim tercih localStorage'a yazılır, giriş
/// yapılınca buraya taşınır; sonrasında sunucu kaydı otoritedir.
/// </summary>
public class UserPreference : BaseEntity
{
    public Guid UserId { get; set; }

    // ---- Ürün turu (Modül 5) ----

    /// <summary>Tur sonuna kadar tamamlandı.</summary>
    public bool OnboardingCompleted { get; set; }

    public DateTime? OnboardingCompletedAtUtc { get; set; }

    /// <summary>Yarıda bırakıldıysa kaçıncı adımdaydı (0 tabanlı). Kaldığı yerden devam için.</summary>
    public int OnboardingLastStep { get; set; }

    /// <summary>
    /// "Bir daha gösterme". Tamamlamaktan AYRI tutulur: turu bitirmeden kapatan kullanıcıya
    /// bir dahaki girişte tekrar önerebilmek, ama açıkça "istemiyorum" diyene hiç sormamak
    /// için. Tek bayrakla bu iki niyet ayırt edilemezdi.
    /// </summary>
    public bool OnboardingSuppressed { get; set; }

    // ---- Çerez rızası (Modül 3) ----
    // "Zorunlu" kategori bilerek YOK: teknik olarak gerekli çerezler rızaya tabi değildir,
    // saklanacak bir tercih de yoktur. Sadece rızaya tabi kategoriler tutulur.

    public CookieConsentState AnalyticsConsent { get; set; } = CookieConsentState.NotAsked;
    public CookieConsentState FunctionalConsent { get; set; } = CookieConsentState.NotAsked;

    /// <summary>
    /// Onaylanan aydınlatma metninin sürümü (örn. "2026-08-14"). Metin değişince sürüm
    /// artırılır ve rıza yeniden sorulur — eski metne verilmiş onay yeni işleme kapsamını
    /// meşrulaştırmaz.
    /// </summary>
    public string? ConsentVersion { get; set; }

    public DateTime? ConsentUpdatedAtUtc { get; set; }

    /// <summary>
    /// Rızanın verildiği andaki IP'nin HASH'i. Ham IP saklanmaz: kanıt için "aynı ağdan mı
    /// geldi" karşılaştırması yeterli, ham adres ise başlı başına kişisel veridir ve veri
    /// minimizasyonu ilkesine aykırı olurdu.
    /// </summary>
    public string? ConsentIpHash { get; set; }

    /// <summary>PostgreSQL xmin (optimistic concurrency).</summary>
    public uint Version { get; set; }
}
