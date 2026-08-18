using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Scheduling;

/// <summary>
/// Ders sonu değerlendirmesi. Yalnızca TAMAMLANMIŞ bir dersin ÖĞRENCİSİ yazabilir ve
/// ders başına yalnızca bir kez (partial unique index).
///
/// SPAM'A KARŞI TASARIM: yorum serbest bir eylem değil, bir dersin ÇIKTISI. Kayıt her zaman
/// bir SessionId'ye bağlıdır; "doğrulanmış yorum" ifadesi bu bağdan gelir — sistemde
/// derse bağlanmayan bir yorum var olamaz.
///
/// User.AverageRating bu tablodan denormalize edilir (liste sıralamalarında JOIN
/// yapmamak için); tek doğruluk kaynağı yine burasıdır.
/// </summary>
public class SessionReview : BaseEntity
{
    public Guid SessionId { get; set; }
    public Guid ReviewerUserId { get; set; }
    public Guid RevieweeUserId { get; set; }

    /// <summary>Genel deneyim, 1-5. Ortalama puan bu alandan hesaplanır.</summary>
    public int Score { get; set; }

    /// <summary>Anlatım becerisi, 1-5.</summary>
    public int TeachingScore { get; set; }

    /// <summary>Zamanlama (dersin vaktinde başlaması), 1-5.</summary>
    public int PunctualityScore { get; set; }

    public string? Comment { get; set; }

    /// <summary>
    /// Dersin gönüllü (0 kredi) olup olmadığı, değerlendirme ANINDA dondurulur.
    /// Ders kaydından JOIN ile de bulunabilirdi; burada tutulmasının sebebi profil
    /// istatistiklerinin ("gönüllü derslerdeki ortalaman") tek tabloyla hesaplanabilmesi.
    /// </summary>
    public bool WasVolunteerSession { get; set; }

    public ICollection<SessionReviewTag> Tags { get; set; } = new List<SessionReviewTag>();
}

/// <summary>
/// Hızlı etiket seçimi. Serbest metin DEĞİL, sabit sözlük — çünkü profildeki "popüler
/// etiket dağılımı" ancak sayılabilir bir kümede anlamlıdır. Ayrı tablo olmasının sebebi
/// de bu: GROUP BY ile doğrudan sayılır (jsonb dizisi olsaydı ham SQL gerekirdi).
/// </summary>
public class SessionReviewTag : BaseEntity
{
    public Guid ReviewId { get; set; }
    public ReviewTag Tag { get; set; }

    public SessionReview Review { get; set; } = null!;
}

public enum ReviewTag
{
    /// <summary>"Konuya çok hakim"</summary>
    KnowsSubject = 0,

    /// <summary>"Sabırlı ve açıklayıcı"</summary>
    PatientAndClear = 1,

    /// <summary>"Zamanında başladı"</summary>
    StartedOnTime = 2,

    /// <summary>"Çözümlü sorular çok iyiydi"</summary>
    GreatExamples = 3,

    /// <summary>"Anlaşılır kaynaklar paylaştı"</summary>
    SharedResources = 4,

    /// <summary>"Tekrar ders alırım"</summary>
    WouldBookAgain = 5
}
