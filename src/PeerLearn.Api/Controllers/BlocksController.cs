using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeerLearn.Application.Features.Identity;

namespace PeerLearn.Api.Controllers;

/// <summary>
/// Kullanıcı engelleme. Yönetim yaptırımlarından (ban/askı) TAMAMEN AYRI bir kavram:
/// burada admin aktörü yok, denetim izi yok — kişisel bir tercih.
/// </summary>
/// <remarks>
/// ⚠️ ŞİKAYET İLE KARIŞTIRILMAMALI. Şikayet (<c>POST /api/sessions/{id}/report</c>)
/// yönetime gidiyor ve tek yönlü/gizli. Engelleme kimseye bildirilmiyor, yalnızca
/// iletişimi kesiyor. Arayüz, engelleme sonrası şikayet yolunu ayrıca öneriyor —
/// taciz varsa engellemek yetmez, yönetimin haberi olmalı.
///
/// "Beni kimler engelledi" diye bir uç BİLEREK YOK: o liste engellemeyi misillemeye
/// çevirirdi.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/blocks")]
public sealed class BlocksController : ControllerBase
{
    private readonly IMediator _mediator;

    public BlocksController(IMediator mediator) => _mediator = mediator;

    public sealed record BlockRequest(Guid UserId, string? Note);

    /// <summary>Kullanıcıyı engeller. İdempotent — zaten engelliyse de başarılı döner.</summary>
    [HttpPost]
    public async Task<IActionResult> Block(BlockRequest request, CancellationToken ct)
    {
        await _mediator.Send(new BlockUserCommand(User.GetUserId(), request.UserId, request.Note), ct);
        return NoContent();
    }

    /// <summary>Engeli kaldırır. İdempotent — engel yoksa da başarılı döner.</summary>
    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> Unblock(Guid userId, CancellationToken ct)
    {
        await _mediator.Send(new UnblockUserCommand(User.GetUserId(), userId), ct);
        return NoContent();
    }

    /// <summary>Kullanıcının engellediklerinin listesi — yalnızca kendi engelledikleri.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<BlockedUserDto>> MyBlocks(CancellationToken ct)
        => await _mediator.Send(new GetMyBlocksQuery(User.GetUserId()), ct);
}
