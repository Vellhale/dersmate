using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PeerLearn.Api.Hubs;
using PeerLearn.Application.Features.Communication;

namespace PeerLearn.Api.Controllers;

/// <summary>
/// Sohbet REST yüzeyi: geçmiş ve liste buradan, CANLI akış SignalR ChatHub'dan.
/// (Mesaj göndermek için de öncelikle hub kullanılmalı; buradaki POST fallback içindir ve
/// hub ile AYNI yayınları yapar — iki yol arasında davranış farkı olmamalı.)
/// </summary>
[ApiController]
[Authorize]
[Route("api/conversations")]
public sealed class ConversationsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IHubContext<ChatHub> _hub;
    private readonly ILogger<ConversationsController> _logger;

    public ConversationsController(IMediator mediator, IHubContext<ChatHub> hub,
        ILogger<ConversationsController> logger)
    {
        _mediator = mediator;
        _hub = hub;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IReadOnlyList<ConversationDto>> GetConversations(CancellationToken ct)
        => await _mediator.Send(new GetConversationsQuery(User.GetUserId()), ct);

    [HttpGet("{conversationId:guid}/messages")]
    public async Task<IReadOnlyList<MessageDto>> GetMessages(
        Guid conversationId, [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        => await _mediator.Send(new GetMessagesQuery(
            conversationId, User.GetUserId(), page == 0 ? 1 : page, pageSize == 0 ? 50 : pageSize), ct);

    public sealed record SendRequest(string Content);

    [HttpPost("{conversationId:guid}/messages")]
    public async Task<MessageDto> Send(Guid conversationId, SendRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(
            new SendMessageCommand(conversationId, User.GetUserId(), request.Content), ct);

        try
        {
            await _hub.Clients.Group(ChatHub.GroupName(conversationId))
                .SendAsync("ReceiveMessage", result.Message, ct);
            await _hub.Clients.User(result.RecipientUserId.ToString())
                .SendAsync("ConversationUpdated", conversationId, ct);
        }
        catch (Exception ex)
        {
            // Persist kaynak-of-truth; yayın hatası isteği düşürmez.
            _logger.LogWarning(ex, "REST mesaj yayını başarısız: {ConversationId}", conversationId);
        }

        return result.Message;
    }

    [HttpPost("{conversationId:guid}/read")]
    public async Task<ActionResult<int>> MarkRead(Guid conversationId, CancellationToken ct)
        => Ok(await _mediator.Send(new MarkConversationReadCommand(conversationId, User.GetUserId()), ct));
}
