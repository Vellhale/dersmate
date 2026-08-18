using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Scheduling;

/// <summary>
/// Rezerve edilmiş ders oturumu.
/// Time-Lock kuralı: "Dersi Tamamladım" isteği yalnızca UtcNow >= ScheduledEndUtc iken kabul edilir
/// (uygulama katmanında zorlanır; ScheduledEndUtc job/index kullanımı için ayrıca saklanır).
/// </summary>
public class LessonSession : BaseEntity
{
    public Guid MatchId { get; set; }
    public Guid TutorUserId { get; set; }
    public Guid StudentUserId { get; set; }
    public Guid TopicId { get; set; }

    public DateTime ScheduledStartUtc { get; set; }
    public int DurationMinutes { get; set; } = 60;

    /// <summary>ScheduledStartUtc + DurationMinutes. Rezervasyon anında uygulama katmanı hesaplar.</summary>
    public DateTime ScheduledEndUtc { get; set; }

    /// <summary>
    /// Ders onaylandığında EĞİTMENE basılacak puan. Öğrenciden hiçbir şey düşmez.
    /// </summary>
    /// <remarks>
    /// ALAN ADI KORUNDU, ANLAMI DEĞİŞTİ — ve bu bilinçli bir takas.
    ///
    /// Eskiden bu tutar öğrenciden tahsil edilip eğitmene geçen bir bedeldi; artık öğrenci
    /// ödemiyor, tutar yalnızca eğitmene basılıyor. "Cost" adı bugün yanıltıcı ama kolon
    /// adını değiştirmek 887 satırlık canlı veriyi, yedi e2e paketini ve API sözleşmesini
    /// aynı anda kırardı. Adın taşıdığı yanlış anlam bu yorumla kapatılıyor; okuma tarafına
    /// dönen DTO alanı ise doğru adıyla (MintAmount) çıkıyor.
    ///
    /// Değer rezervasyon anında DONDURULUR: eğitmen ilanını sonradan gönüllüye çevirse bile
    /// açılmış dersin ödülü değişmez — aksi halde emeği verilmiş bir dersin karşılığı
    /// sonradan sıfırlanabilirdi.
    /// </remarks>
    public int CreditCost { get; set; }

    /// <summary>
    /// Bu ders gönüllü olarak mı veriliyor (eğitmen puan kazanmıyor).
    /// </summary>
    /// <remarks>
    /// GÖNÜLLÜLÜK ARTIK <see cref="CreditCost"/>'TAN OKUNMUYOR, AYRI BİR BAYRAK.
    ///
    /// Eskiden "gönüllü" ile "0 kredi" aynı şeydi ve kod her yerde `CreditCost == 0` diye
    /// soruyordu. Yeni modelde bu çıkarım çöküyor: öğrenci zaten hiçbir derste ödemiyor,
    /// yani 0 olan şey artık gönüllülüğü değil yalnızca eğitmenin kazancını anlatıyor.
    /// Bayrak ayrılmasaydı, ödül miktarını 0 yapan her durum (ör. ileride bir yaptırım)
    /// kullanıcıya gönüllü rozeti kazandırırdı.
    /// </remarks>
    public bool IsVolunteer { get; set; }

    /// <summary>
    /// Derse özel benzersiz doğrulama kodu (Session ID). Eğitmen ekran görüntüsünde bu kodun
    /// görünmesini sağlar; kanıt yüklerken kod eşleşmesi kontrol edilir.
    /// </summary>
    public string VerificationCode { get; set; } = null!;

    public SessionStatus Status { get; set; } = SessionStatus.Booked;

    public DateTime? CompletionRequestedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancelReason { get; set; }

    /// <summary>PostgreSQL xmin sistem kolonuna eşlenir (optimistic concurrency token).</summary>
    public uint Version { get; set; }

    public ICollection<SessionProof> Proofs { get; set; } = new List<SessionProof>();
}
