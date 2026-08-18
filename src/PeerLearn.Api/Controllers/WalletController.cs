using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeerLearn.Application.Common;
using PeerLearn.Application.Features.Economy;

namespace PeerLearn.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/wallet")]
public sealed class WalletController : ControllerBase
{
    private readonly IMediator _mediator;

    public WalletController(IMediator mediator) => _mediator = mediator;

    /// <summary>Bakiye + vadesi yaklaşan lotlar ("X kredin şu tarihte yanacak" uyarısı için).</summary>
    [HttpGet]
    public async Task<WalletDto> Get(CancellationToken ct)
        => await _mediator.Send(new GetWalletQuery(User.GetUserId()), ct);

    /// <summary>Cüzdan ekstresi (append-only defterden, sayfalı).</summary>
    [HttpGet("statement")]
    public async Task<PagedResult<StatementEntryDto>> GetStatement(
        [FromQuery] int page, [FromQuery] int pageSize, CancellationToken ct)
        => await _mediator.Send(new GetStatementQuery(
            User.GetUserId(), page == 0 ? 1 : page, pageSize == 0 ? 20 : pageSize), ct);
}
