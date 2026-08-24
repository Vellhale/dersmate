using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Matchmaking;

/// <summary>
/// İki kullanıcı arasındaki eşleşme talebi. Eşleşmenin çapraz (karşılıklı) olması
/// zorunlu değildir: OfferedTopicId null ise tek yönlüdür (başlatan yalnızca ders
/// almak istiyor — ders almak ücretsiz).
///
/// İKİ TÜR EŞLEŞME VAR ve ayrımı <see cref="RequestedTopicId"/> taşıyor:
///
///   • DERS eşleşmesi (RequestedTopicId dolu) — YKS tarafı. Belirli bir konu üzerinden
///     kurulur, kabul edilince o konuda ders rezerve edilebilir.
///   • ÜNİVERSİTE AĞI eşleşmesi (RequestedTopicId null) — üniversite tarafı. Ders ve
///     konu kavramı yok; iki kişi tanışıp sohbet etsin diye kurulur. Kabul edilince
///     yalnızca sohbet açılır, ders rezerve EDİLEMEZ (bkz. BookSession'daki muhafız).
///
/// Ayrı bir tür alanı (MatchKind) EKLENMEDİ: türü zaten RequestedTopicId'nin null olup
/// olmaması belirliyor ve aynı bilgiyi iki alanda tutmak, ikisinin er ya da geç
/// çelişmesi demekti. Enum'lar bu projede metin olarak saklandığı için (HasConversion)
/// yeni bir üye eklemek ayrıca göç yükü de getirirdi.
/// </summary>
public class Match : BaseEntity
{
    public Guid InitiatorUserId { get; set; }
    public Guid ResponderUserId { get; set; }

    /// <summary>
    /// Başlatanın karşı taraftan almak istediği konu.
    ///
    /// NULL = üniversite ağı isteği (ders değil, tanışma/sohbet). Bu alanı okuyan her
    /// yer null'ı ele almak zorunda — özellikle Topics ile birleştirmeler LEFT JOIN
    /// olmalı, aksi halde konusuz eşleşme sorgudan sessizce DÜŞER.
    /// </summary>
    public Guid? RequestedTopicId { get; set; }

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
