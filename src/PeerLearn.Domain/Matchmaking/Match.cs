using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Matchmaking;

/// <summary>
/// İki kullanıcı arasındaki eşleşme talebi. Kredi ekonomisi sayesinde eşleşmenin
/// çapraz (karşılıklı) olması zorunlu değildir: OfferedTopicId null ise tek yönlüdür
/// (başlatan yalnızca ders almak istiyor, ödemeyi kredisiyle yapacak).
/// </summary>
public class Match : BaseEntity
{
    public Guid InitiatorUserId { get; set; }
    public Guid ResponderUserId { get; set; }

    /// <summary>Başlatanın karşı taraftan almak istediği konu.</summary>
    public Guid RequestedTopicId { get; set; }

    /// <summary>Çapraz eşleşmede başlatanın karşılığında anlatmayı önerdiği konu (opsiyonel).</summary>
    public Guid? OfferedTopicId { get; set; }

    public MatchStatus Status { get; set; } = MatchStatus.Pending;

    /// <summary>
    /// Muhatabın yanıt verdiği an. Yanıtsız kalıp SÜRESİ DOLAN istekte null KALIR —
    /// süre dolumu bir yanıt değildir ve ⚡ hızlı yanıt ortancası bunu "görmezden
    /// gelinmiş istek" saymaya devam etmelidir (bkz. BadgeEngine sağdan sansür notu).
    /// </summary>
    public DateTime? RespondedAtUtc { get; set; }

    /// <summary>Eşleşmenin taraflardan biri tarafından sonlandırıldığı an (Closed).</summary>
    public DateTime? ClosedAtUtc { get; set; }

    /// <summary>Sonlandıran taraf. Sohbet başlığında "kim kapattı" bilgisi buradan yazılır.</summary>
    public Guid? ClosedByUserId { get; set; }
}
