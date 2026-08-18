using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PeerLearn.Application.Features.Communication;

namespace PeerLearn.Api.Hubs;

/// <summary>
/// Platform içi güvenli sohbet (Modül 2.1). Erişim kuralı tek yerde: her metot,
/// Application katmanındaki ConversationAccess/SendMessage üzerinden yetki doğrular —
/// hub yalnızca taşıma katmanıdır. AppException'lar AppExceptionHubFilter ile
/// "CODE|mesaj" formatında istemciye taşınır. Grup adı: "conv:{conversationId}".
///
/// İstemci olayları:
///   ReceiveMessage(MessageDto)         → gruba yayın
///   ConversationUpdated(conversationId) → karşı tarafa kişisel bildirim (rozet güncelle)
///   MessagesRead(conversationId, byUserId)
/// </summary>
[Authorize]
public sealed class ChatHub : Hub
{
    private readonly IMediator _mediator;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(IMediator mediator, ILogger<ChatHub> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    private Guid UserId => Guid.Parse(Context.UserIdentifier!);

    public async Task JoinConversation(Guid conversationId)
    {
        // Yetkisizse AppException fırlar → filter üzerinden istemciye kod+mesaj gider.
        await _mediator.Send(new GetMessagesQuery(conversationId, UserId, Page: 1, PageSize: 1));
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(conversationId));
    }

    public Task LeaveConversation(Guid conversationId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(conversationId));

    /// <returns>
    /// Kaydedilen mesaj. Persist başarılıysa yayın hatası çağrıyı DÜŞÜRMEZ (yalnızca loglanır);
    /// aksi halde gönderen "kaydedilmedi" sanıp tekrar gönderir ve mesaj mükerrer olurdu.
    /// </returns>
    public async Task<MessageDto> SendMessage(Guid conversationId, string content)
    {
        var result = await _mediator.Send(new SendMessageCommand(conversationId, UserId, content));

        try
        {
            await Clients.Group(GroupName(conversationId)).SendAsync("ReceiveMessage", result.Message);

            // Karşı taraf sohbeti açık tutmuyorsa bile gelen kutusu rozetini güncelleyebilsin.
            await Clients.User(result.RecipientUserId.ToString())
                .SendAsync("ConversationUpdated", conversationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mesaj yayını başarısız (persist edildi): {ConversationId}", conversationId);
        }

        return result.Message;
    }

    public async Task MarkRead(Guid conversationId)
    {
        var updated = await _mediator.Send(new MarkConversationReadCommand(conversationId, UserId));
        if (updated > 0)
        {
            try
            {
                await Clients.Group(GroupName(conversationId)).SendAsync("MessagesRead", conversationId, UserId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Okundu yayını başarısız: {ConversationId}", conversationId);
            }
        }
    }

    internal static string GroupName(Guid conversationId) => $"conv:{conversationId}";
}
