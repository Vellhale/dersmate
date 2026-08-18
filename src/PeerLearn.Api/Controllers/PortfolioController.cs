using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeerLearn.Application.Features.Matchmaking;
using PeerLearn.Domain.Matchmaking;

namespace PeerLearn.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/portfolio")]
public sealed class PortfolioController : ControllerBase
{
    private readonly IMediator _mediator;

    public PortfolioController(IMediator mediator) => _mediator = mediator;

    /// <summary>Kendi portföyüm (Offer + Seek).</summary>
    [HttpGet("entries")]
    public async Task<IReadOnlyList<PortfolioEntryDto>> GetEntries(CancellationToken ct)
        => await _mediator.Send(new GetMyPortfolioQuery(User.GetUserId()), ct);

    public sealed record AddEntryRequest(
        Guid TopicId,
        PortfolioDirection Direction,
        int SelfAssessedLevel,
        string? Note,
        bool IsVolunteer = false);

    [HttpPost("entries")]
    public async Task<ActionResult<Guid>> AddEntry(AddEntryRequest request, CancellationToken ct)
    {
        var id = await _mediator.Send(new AddPortfolioEntryCommand(
            User.GetUserId(), request.TopicId, request.Direction, request.SelfAssessedLevel,
            request.Note, request.IsVolunteer), ct);

        return Ok(id);
    }

    [HttpDelete("entries/{entryId:guid}")]
    public async Task<IActionResult> RemoveEntry(Guid entryId, CancellationToken ct)
    {
        await _mediator.Send(new RemovePortfolioEntryCommand(User.GetUserId(), entryId), ct);
        return NoContent();
    }

    /// <summary>Çapraz eşleşme önerileri (Modül 1.2).</summary>
    [HttpGet("suggestions")]
    public async Task<IReadOnlyList<MatchSuggestionDto>> GetSuggestions([FromQuery] int limit, CancellationToken ct)
        => await _mediator.Send(new GetMatchSuggestionsQuery(User.GetUserId(), limit == 0 ? 20 : limit), ct);
}
