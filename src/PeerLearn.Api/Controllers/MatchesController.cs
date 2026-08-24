using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeerLearn.Application.Features.Matchmaking;

namespace PeerLearn.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/matches")]
public sealed class MatchesController : ControllerBase
{
    private readonly IMediator _mediator;

    public MatchesController(IMediator mediator) => _mediator = mediator;

    /// <summary>Eşleşmelerim: gelen bekleyen, gönderdiğim bekleyen ve aktif eşleşmeler.</summary>
    [HttpGet]
    public async Task<MyMatchesDto> GetMine(CancellationToken ct)
        => await _mediator.Send(new GetMyMatchesQuery(User.GetUserId()), ct);

    /// <summary>
    /// <paramref name="RequestedTopicId"/> null gönderilebilir: o zaman istek bir ÜNİVERSİTE
    /// AĞI isteğidir (ders değil, tanışma). Kabul edilirse yalnızca sohbet açılır.
    /// </summary>
    public sealed record CreateRequest(Guid ResponderUserId, Guid? RequestedTopicId, Guid? OfferedTopicId);

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreateRequest request, CancellationToken ct)
    {
        var id = await _mediator.Send(new CreateMatchRequestCommand(
            User.GetUserId(), request.ResponderUserId, request.RequestedTopicId, request.OfferedTopicId), ct);

        return Ok(id);
    }

    public sealed record RespondRequest(bool Accept);

    [HttpPost("{matchId:guid}/respond")]
    public async Task<RespondMatchResult> Respond(Guid matchId, RespondRequest request, CancellationToken ct)
        => await _mediator.Send(new RespondMatchCommand(matchId, User.GetUserId(), request.Accept), ct);

    /// <summary>
    /// Eşleşmeyi sonlandır. Tek taraflı; sohbet salt okunur olur, yeni ders rezerve edilemez.
    /// Sonuçlanmamış ders varken reddedilir (409).
    /// </summary>
    [HttpPost("{matchId:guid}/close")]
    public async Task<CloseMatchResult> Close(Guid matchId, CancellationToken ct)
        => await _mediator.Send(new CloseMatchCommand(matchId, User.GetUserId()), ct);
}
