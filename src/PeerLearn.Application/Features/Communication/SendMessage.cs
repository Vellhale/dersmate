using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Communication;
using PeerLearn.Domain.Matchmaking;

namespace PeerLearn.Application.Features.Communication;

/// <summary>
/// Güvenli sohbet mesajı (Modül 2.1/2.2). Kanal bağımsızlığı ilkesi gereği kullanıcılar
/// Zoom/Meet/Discord linklerini bu mesajlarla paylaşır — platform video barındırmaz.
/// Persist burada; canlı yayın SignalR ChatHub'da (Api katmanı) yapılır.
/// </summary>
public sealed record SendMessageCommand(Guid ConversationId, Guid SenderUserId, string Content)
    : IRequest<SendMessageResult>;

public sealed record MessageDto(
    Guid Id,
    Guid ConversationId,
    Guid SenderUserId,
    string Content,
    DateTime SentAtUtc);

/// <param name="RecipientUserId">Hub'ın kullanıcı-bazlı bildirim göndereceği karşı taraf.</param>
public sealed record SendMessageResult(MessageDto Message, Guid RecipientUserId);

public sealed class SendMessageHandler : IRequestHandler<SendMessageCommand, SendMessageResult>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public SendMessageHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<SendMessageResult> Handle(SendMessageCommand request, CancellationToken ct)
    {
        var content = request.Content?.Trim() ?? string.Empty;
        if (content.Length is 0 or > 2000)
        {
            throw new AppException(ErrorCodes.MessageInvalid, "Mesaj 1-2000 karakter olmalı.");
        }

        // YAZMA erişimi: sonlandırılmış eşleşmenin sohbeti okunabilir ama yazılamaz.
        var access = await ConversationAccess.GetForWriteAsync(_db, request.ConversationId, request.SenderUserId, ct);

        var message = new Message
        {
            ConversationId = request.ConversationId,
            SenderUserId = request.SenderUserId,
            Content = content
        };
        _db.Messages.Add(message);

        var conversation = await _db.Conversations.SingleAsync(c => c.Id == request.ConversationId, ct);
        conversation.LastMessageAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(ct);

        var dto = new MessageDto(message.Id, message.ConversationId, message.SenderUserId,
            message.Content, message.CreatedAtUtc);

        return new SendMessageResult(dto, access.OtherUserId);
    }
}

/// <summary>
/// Sohbet erişim kuralı (tek yerde): kullanıcı, konuşmanın bağlı olduğu eşleşmenin
/// tarafı olmalı. Hub'daki JoinConversation da bunu kullanır.
/// </summary>
/// <remarks>
/// OKUMA VE YAZMA AYRI, çünkü eşleşme SONLANDIRILDIĞINDA (Closed) ikisi farklı davranır:
///  • Yazma kapanır — sonlandırma bunun için var.
///  • Okuma AÇIK KALIR — geçmiş sohbet kullanıcının kendi kaydı; ders bağlantıları,
///    verilen sözler, hatta bir şikâyete dayanak olacak mesajlar orada. Kapatmayı
///    geçmişi silmeye çevirmek, tacizciye "kapat, kanıt uçsun" düğmesi vermek olurdu.
/// Tek bir GetAsync bırakılsaydı iki davranış kaçınılmaz olarak birbirine karışırdı.
/// </remarks>
public static class ConversationAccess
{
    public readonly record struct AccessInfo(Guid MatchId, Guid OtherUserId, bool IsClosed);

    /// <summary>Mesaj yazma / okundu işaretleme: yalnızca KABUL EDİLMİŞ eşleşme.</summary>
    public static async Task<AccessInfo> GetForWriteAsync(
        IAppDbContext db, Guid conversationId, Guid userId, CancellationToken ct)
    {
        var access = await GetForReadAsync(db, conversationId, userId, ct);

        if (access.IsClosed)
        {
            throw new AppException(ErrorCodes.ConversationAccessDenied,
                "Bu eşleşme sonlandırıldı; sohbete yeni mesaj yazılamaz.", statusCode: 403);
        }

        return access;
    }

    /// <summary>Geçmişi görüntüleme: kabul edilmiş VEYA sonlandırılmış eşleşme.</summary>
    public static async Task<AccessInfo> GetForReadAsync(
        IAppDbContext db, Guid conversationId, Guid userId, CancellationToken ct)
    {
        var info = await (
                from c in db.Conversations
                join m in db.Matches on c.MatchId equals m.Id
                where c.Id == conversationId
                select new { m.Id, m.InitiatorUserId, m.ResponderUserId, m.Status })
            .SingleOrDefaultAsync(ct);

        if (info is null ||
            (info.InitiatorUserId != userId && info.ResponderUserId != userId) ||
            (info.Status != MatchStatus.Accepted && info.Status != MatchStatus.Closed))
        {
            throw new AppException(ErrorCodes.ConversationAccessDenied,
                "Bu sohbete erişiminiz yok.", statusCode: 403);
        }

        var other = info.InitiatorUserId == userId ? info.ResponderUserId : info.InitiatorUserId;
        return new AccessInfo(info.Id, other, info.Status == MatchStatus.Closed);
    }
}
