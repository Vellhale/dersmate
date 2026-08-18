using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Matchmaking;

/// <summary>
/// Kullanıcının iki yönlü portföyü: Direction=Offer → "verebilirim", Direction=Seek → "almak istiyorum".
/// Çapraz eşleşme sorgusu bu tablo üzerinden çalışır:
///   A.Offer(X) ∩ B.Seek(X)  ve  B.Offer(Y) ∩ A.Seek(Y)
/// </summary>
public class PortfolioEntry : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid TopicId { get; set; }
    public PortfolioDirection Direction { get; set; }

    /// <summary>Öz değerlendirme 1-5. Offer girişlerinde eşleştirme sıralamasına katkı verir.</summary>
    public int SelfAssessedLevel { get; set; } = 3;

    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gönüllü ders ilanı: bu konudan rezerve edilen dersler 0 krediye mal olur.
    /// Yalnızca Direction=Offer için anlamlıdır ve öğretmen adayı beyanı (bkz.
    /// TeacherCandidateProfile) olmadan açılamaz — uygulama katmanında zorlanır.
    ///
    /// Dersin ücreti rezervasyon anında LessonSession.CreditCost'a KOPYALANIR: ilan
    /// sonradan ücretliye çevrilse bile açılmış ders 0 kredilik kalmalıdır.
    /// </summary>
    public bool IsVolunteer { get; set; }
}
