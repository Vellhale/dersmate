using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Communication;

/// <summary>
/// Platform içi mesaj. Video/toplantı platform dışında yapıldığı için (sıfır API maliyeti)
/// kullanıcılar Zoom/Meet/Discord linklerini bu mesajlar üzerinden paylaşır.
/// Gönderim zamanı = CreatedAtUtc. Silme soft-delete'tir (itiraz süreçlerinde delil kalmalı).
/// </summary>
public class Message : BaseEntity
{
    public Guid ConversationId { get; set; }
    public Guid SenderUserId { get; set; }
    public string Content { get; set; } = null!;
    public DateTime? ReadAtUtc { get; set; }
    public bool IsDeleted { get; set; }

    public Conversation Conversation { get; set; } = null!;
}
