using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Economy;

/// <summary>
/// Vadeli kredi partisi (lot). "Kazanılan puanın 30 gün ömrü vardır" kuralı lot bazında uygulanır:
/// her kazanç ayrı bir lot açar, harcamalar lotları vadesi en yakın olandan başlayarak (FIFO) tüketir.
/// Vadesi dolan lotların RemainingAmount'u background job tarafından sıfırlanır ve
/// Expiry tipinde bir CreditTransaction yazılır.
/// </summary>
public class CreditLot : BaseEntity
{
    public Guid WalletId { get; set; }

    public int InitialAmount { get; set; }

    /// <summary>Kalan miktar. DB check: 0 &lt;= RemainingAmount &lt;= InitialAmount.</summary>
    public int RemainingAmount { get; set; }

    public CreditLotSource Source { get; set; }

    /// <summary>Kazanca yol açan ders (LessonEarning kaynaklı lotlarda dolu).</summary>
    public Guid? SourceSessionId { get; set; }

    public DateTime EarnedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Vade sonu. <c>null</c> ise lot SÜRESİZDİR ve vade süpürücüsü ona hiç dokunmaz.
    /// </summary>
    /// <remarks>
    /// DERS KAZANÇLARI ARTIK SÜRESİZ. Kredi bir para birimiyken 30 günlük vade istifi
    /// önlüyordu — harcanmayan kredi ölüyordu ve bu doğruydu. Kredi bir BAŞARI PUANINA
    /// dönüşünce aynı kural anlamsızlaşıyor: puanın "istiflenmesi" diye bir sorun yok,
    /// üstelik yanan puan kullanıcının unvanını da düşürürdü — hiçbir şey yapmadığı hâlde
    /// Mentor'dan Uzman'a inen bir kullanıcı, sistemin en görünür vaadinin bozulması demek.
    ///
    /// Alan tamamen kaldırılmadı çünkü geçmiş lotlar (hoş geldin kredileri ve eski ders
    /// kazançları) gerçek vadeler taşıyor ve o kayıtlar tarihsel olarak doğru kalmalı.
    /// </remarks>
    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>PostgreSQL xmin sistem kolonuna eşlenir (optimistic concurrency token).</summary>
    public uint Version { get; set; }
}
