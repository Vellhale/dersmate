using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Identity;

public class User : BaseEntity
{
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? PhoneNumber { get; set; }

    public DateTime? EmailVerifiedAtUtc { get; set; }
    public DateTime? PhoneVerifiedAtUtc { get; set; }
    public UserStatus Status { get; set; } = UserStatus.PendingVerification;

    /// <summary>
    /// Hoş geldin kredisinin verildiği an. Null değilse bir daha verilmez (tek seferlik guard).
    /// Kredi tutarı/kuralı uygulama katmanında, iz burada.
    /// </summary>
    public DateTime? WelcomeCreditGrantedAtUtc { get; set; }

    public string? Bio { get; set; }

    /// <summary>Profil fotoğrafının depo anahtarı (kanıt görselleriyle aynı depo soyutlaması).</summary>
    public string? AvatarUrl { get; set; }

    // ---- Samimi profil alanları ----
    public string? University { get; set; }
    public string? Department { get; set; }

    /// <summary>
    /// Anlatılan ders sayısı ve toplam dakika — profildeki "deneyim" sayaçları.
    /// Denormalize: her profil görüntülemede LessonSessions'ı taramak yerine ders
    /// tamamlandığında artırılır. GÖNÜLLÜ DERSLER DE SAYILIR — bu modülün amacı zaten
    /// kredi kazandırmadan deneyim biriktirmek.
    /// </summary>
    public int TaughtSessionCount { get; set; }
    public int TaughtMinutes { get; set; }

    /// <summary>
    /// Ders anlatarak kazanılmış TOPLAM kredi. Seviye (<see cref="Community.UserLevelRules"/>)
    /// bu sayıdan hesaplanır.
    /// </summary>
    /// <remarks>
    /// CÜZDAN BAKİYESİNDEN AYRI BİR SAYAÇ — ve ayrı olması şart.
    ///
    /// Bakiye azalabilen bir büyüklük (vade yakımı, ileride bir harcama yolu). Unvan ise
    /// "bu kişi ne kadar ders anlatmış" sorusunun yanıtı ve GERİYE GİTMEMELİ: bakiyeden
    /// okunsaydı, kredisi yandığı gün Mentor olan kullanıcı Uzman'a düşerdi. Kazanılmış
    /// bir unvanı kullanıcının hiçbir eylemi olmadan geri almak, sistemin en görünür
    /// vaadini bozmak olurdu.
    ///
    /// Bu yüzden yalnızca ARTAR: ders onayında eğitmenin kazancı kadar eklenir, hiçbir
    /// yolda azalmaz. Denormalize; kaynağı CreditTransaction(LessonEarning) satırları.
    /// </remarks>
    public int TotalEarnedCredits { get; set; }

    /// <summary>SessionReview'lardan denormalize edilir; eşleştirme sıralamasında kullanılır.</summary>
    public decimal AverageRating { get; set; }
    public int RatingCount { get; set; }

    public DateTime? LastLoginAtUtc { get; set; }

    /// <summary>
    /// Geçici yaptırımın bitiş anı (Status = Suspended iken anlamlı).
    ///
    /// Neden ayrı kolon: süre bilgisi UserSanction satırında da duruyor ama her istekte
    /// yaptırım tablosunu sorgulamak, erişim kontrolünü ikinci bir sorguya bağlardı. Bu
    /// alan sayesinde "bu kullanıcı şu an içeri girebilir mi" sorusu TEK satır okumayla
    /// yanıtlanır. Süre dolduğunda arka plan işi Status'u Active'e çevirir; erişim kontrolü
    /// beklemeden serbest bırakır (tarih geçmişse yaptırım fiilen bitmiştir).
    /// </summary>
    public DateTime? SuspendedUntilUtc { get; set; }

    /// <summary>
    /// RBAC rolü; JWT'ye rol claim'i olarak yazılır. Eski bool IsAdmin bunun yerini aldı —
    /// migration mevcut IsAdmin=true kayıtlarını Admin'e taşır. Tek doğruluk kaynağı burasıdır;
    /// "yönetim panelini görebilir mi" sorusu <see cref="CanModerate"/> ile türetilir, ayrı
    /// bir bayrak olarak SAKLANMAZ (iki kaynak er ya da geç birbirinden ayrılır).
    /// </summary>
    public UserRole Role { get; set; } = UserRole.Student;

    /// <summary>Yönetim paneline erişebilen roller. Kalıcı ban gibi ağır yetkiler için yetmez.</summary>
    public bool CanModerate => Role is UserRole.Admin or UserRole.Moderator;

    /*
      ─── KAYIT ONAYININ KAYDI (2026-08-27) ───────────────────────────────────

      Kayıt formu 2026-08-27'de iki onay kutusu kazandı (sözleşme + yaş beyanı) ama
      SUNUCU HİÇBİR ŞEY SAKLAMIYORDU. Bu, onay hiç sormamaktan daha kötü bir durum:
      arayüz kullanıcıya "kabul ettin" diyor, ürün sahibi de kullanıcıların kabul
      ettiğini sanıyor, ama "şu kişi şu metni şu tarihte kabul etti" diyebilecek tek
      bir satır yok. Onayın değeri KANITINDADIR.

      ÜÇÜ AYRI ALAN, tek bayrak değil:
        • hangi METİN kabul edildi (sürüm) — metin değiştiğinde eski onay yeni metni
          kapsamaz; sürüm olmadan bunu söyleyemezsiniz.
        • NE ZAMAN kabul edildi.
        • yaş beyanı AYRI bir zaman damgası — sözleşmeyi kabul etmekle "18 yaşından
          büyüğüm" demek farklı iki beyan ve formda da iki ayrı kutu.

      ⚠️ NULL KALAN SATIRLAR GERİYE DOLDURULMAZ. Onay kutusu eklenmeden önce açılmış
      hesaplarda bu alanlar boş ve boş kalmalı: uydurulmuş bir onay kaydı, hiç kayıt
      olmamasından daha zararlıdır (denetimde "kanıtımız var" denir, kanıt sahtedir).
      O kullanıcılardan onay gerekiyorsa uygulama içi bir yeniden onay akışı gerekir.
    */

    /// <summary>Kabul edilen sözleşme sürümü (<c>LegalDocuments.CurrentVersion</c>).</summary>
    public string? TermsVersion { get; set; }

    public DateTime? TermsAcceptedAtUtc { get; set; }

    /// <summary>"18 yaşından büyüğüm ya da velimin onayıyla" beyanının anı.</summary>
    public DateTime? AgeConfirmedAtUtc { get; set; }

    /// <summary>PostgreSQL xmin sistem kolonuna eşlenir (optimistic concurrency token).</summary>
    public uint Version { get; set; }

    public ICollection<UserDevice> Devices { get; set; } = new List<UserDevice>();
}
