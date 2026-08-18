using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Economy;

/// <summary>
/// Kullanıcının kredi cüzdanı (önbelleklenmiş bakiye).
/// Değişmez (invariant):
///   AvailableBalance == SUM(CreditLots.RemainingAmount)   (bu cüzdanın aktif lotları)
///
/// TEK BAKİYE VAR. Eskiden bir de LockedBalance (escrow) vardı: rezervasyon anında
/// öğrencinin kredisi bloke edilir, ders onaylanınca eğitmene aktarılırdı. Ders almak
/// ücretsizleşince o mekanizmanın konusu kalmadı — rezervasyon hiçbir bakiyeye dokunmuyor,
/// puan yalnızca ANLATAN tarafa ve yalnızca onay anında BASILIYOR. Kolon bir süre sıfır
/// değerlerle durdu ve "bloke" kavramını arayüzde de yaşatıyordu; kaldırıldı.
/// Tüm bakiye güncellemeleri tek DB transaction içinde, xmin optimistic concurrency ile yapılır.
/// </summary>
public class Wallet : BaseEntity
{
    public Guid UserId { get; set; }

    /// <summary>Harcanabilir bakiye. DB check constraint: >= 0.</summary>
    public int AvailableBalance { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>PostgreSQL xmin sistem kolonuna eşlenir (optimistic concurrency token).</summary>
    public uint Version { get; set; }
}
