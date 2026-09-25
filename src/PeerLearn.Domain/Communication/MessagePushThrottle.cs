namespace PeerLearn.Domain.Communication;

/// <summary>
/// Mesaj bildirimi kısması: (alıcı, sohbet) başına son push anı. 60 saniyede en fazla bir.
/// </summary>
/// <remarks>
/// NEDEN TABLO, NEDEN BELLEK DEĞİL: iki sunucu kopyası aynı anda dağıtım yapabiliyor.
/// Yuva tek bir atomik ifadeyle alınır —
/// <c>INSERT … ON CONFLICT DO UPDATE SET "LastSentAtUtc" = @now WHERE "LastSentAtUtc" &lt;= @now - 60 sn RETURNING …</c>
/// — dönen satır yoksa yuva başkasında; bildirim satırı LastSentAtUtc + 60 sn'ye ertelenir.
/// Bellekteki bir sözlük her kopyada ayrı sayar ve telefonu iki kez çaldırırdı.
///
/// BaseEntity DEĞİL: doğal anahtar (RecipientUserId, ConversationId) bileşik PK. FK yok;
/// hesap silme ve günlük temizlik (LastSentAtUtc &lt; now - 1 gün) satırları kaldırır.
/// </remarks>
public class MessagePushThrottle
{
    public Guid RecipientUserId { get; set; }

    public Guid ConversationId { get; set; }

    public DateTime LastSentAtUtc { get; set; }
}
