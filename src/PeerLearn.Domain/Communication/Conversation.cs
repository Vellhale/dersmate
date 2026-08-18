using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Communication;

/// <summary>
/// Eşleşme kabul edildiğinde açılan 1:1 sohbet kanalı (SignalR grubu bu Id ile kurulur).
/// Katılımcılar Match üzerinden türetilir; ayrıca katılımcı tablosu tutulmaz.
/// </summary>
public class Conversation : BaseEntity
{
    public Guid MatchId { get; set; }

    /// <summary>Gelen kutusunu "son mesaja göre" sıralamak için denormalize alan.</summary>
    public DateTime? LastMessageAtUtc { get; set; }

    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
