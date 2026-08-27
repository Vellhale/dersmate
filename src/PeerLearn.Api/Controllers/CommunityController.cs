using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeerLearn.Application.Common;
using PeerLearn.Application.Features.Community;
using PeerLearn.Application.Features.Moderation;
using PeerLearn.Domain.Community;
using PeerLearn.Domain.Moderation;

namespace PeerLearn.Api.Controllers;

/// <summary>
/// Topluluk (forum) uçları.
///
/// [Authorize]: forum GİRİŞ GEREKTİRİYOR, herkese açık değil. İki sebep var ve ikisi de
/// moderasyonla ilgili: (a) oy ve şikayet kimliğe bağlı olmadan kötüye kullanıma açık,
/// (b) yaptırım (askı/ban) ancak hesabı olan birine uygulanabilir. Herkese açık okuma
/// istenirse ayrı ve YALNIZCA okuyan bir uç eklenir; bu ucun yetkisi gevşetilmez.
/// </summary>
[ApiController]
[Authorize]
[Route("api/community")]
public sealed class CommunityController : ControllerBase
{
    private readonly IMediator _mediator;

    public CommunityController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Akış: sıralama + tarih penceresi + etiket filtresi.
    ///
    /// Sıralama ve pencere SUNUCUDA uygulanıyor, istemcide değil: istemci tarafı
    /// sıralama, sayfalamayı anlamsız kılardı (ikinci sayfayı verebilmek için tüm
    /// gönderileri göndermek gerekirdi).
    /// </summary>
    [HttpGet("posts")]
    public async Task<PagedResult<ForumPostDto>> Feed(
        [FromQuery] ForumSort sort = ForumSort.Newest,
        [FromQuery] ForumRange range = ForumRange.All,
        [FromQuery] ForumTag? tag = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => await _mediator.Send(new GetForumFeedQuery(
            User.GetUserId(), sort, range, tag, page, pageSize), ct);

    public sealed record CreatePostRequest(ForumTag Tag, string Title, string Body);

    /// <summary>
    /// Yeni gönderi. Spam duvarı (günlük tavan) ve bağlantı eşiği handler'da —
    /// istemciden bağımsız olarak uygulanır.
    /// </summary>
    [HttpPost("posts")]
    public async Task<ActionResult<Guid>> CreatePost(CreatePostRequest request, CancellationToken ct)
        => Ok(await _mediator.Send(new CreateForumPostCommand(
            User.GetUserId(), request.Tag, request.Title, request.Body), ct));

    [HttpGet("posts/{postId:guid}/comments")]
    public async Task<IReadOnlyList<ForumCommentDto>> Comments(Guid postId, CancellationToken ct)
        => await _mediator.Send(new GetForumCommentsQuery(postId, User.GetUserId()), ct);

    public sealed record CreateCommentRequest(string Body);

    [HttpPost("posts/{postId:guid}/comments")]
    public async Task<ActionResult<Guid>> CreateComment(
        Guid postId, CreateCommentRequest request, CancellationToken ct)
        => Ok(await _mediator.Send(new CreateForumCommentCommand(
            postId, User.GetUserId(), request.Body), ct));

    public sealed record VoteRequest(short Value);

    /// <summary>
    /// Gönderiye oy. Aynı yöne ikinci istek oyu GERİ ALIR — istemcinin "sil" için ayrı
    /// bir uç çağırması gerekmiyor, çünkü kullanıcı için de tek bir jest (aynı oka
    /// tekrar basmak).
    /// </summary>
    [HttpPost("posts/{postId:guid}/vote")]
    public async Task<ForumVoteResult> VotePost(Guid postId, VoteRequest request, CancellationToken ct)
        => await _mediator.Send(new VoteForumContentCommand(
            User.GetUserId(), postId, null, request.Value), ct);

    [HttpPost("comments/{commentId:guid}/vote")]
    public async Task<ForumVoteResult> VoteComment(
        Guid commentId, VoteRequest request, CancellationToken ct)
        => await _mediator.Send(new VoteForumContentCommand(
            User.GetUserId(), null, commentId, request.Value), ct);

    public sealed record ContentReportRequest(ReportReason Reason, string Description);

    /// <summary>
    /// Gönderi şikayeti. Aynı kuyruğa düşüyor (moderation.Reports) — ders, sohbet ve
    /// forum şikayetleri moderatör için tek yerde toplanıyor.
    ///
    /// Eşiği geçen içerik OTOMATİK perdeleniyor (ForumRules.AutoReviewThreshold):
    /// arayüzdeki "3 şikayet alan gönderi akışta kapatılır" vaadinin karşılığı.
    /// </summary>
    [HttpPost("posts/{postId:guid}/report")]
    public async Task<ActionResult<Guid>> ReportPost(
        Guid postId, ContentReportRequest request, CancellationToken ct)
        => Ok(await _mediator.Send(new CreateReportCommand(
            User.GetUserId(), null, null, request.Reason, request.Description,
            CommunityPostId: postId), ct));

    [HttpPost("comments/{commentId:guid}/report")]
    public async Task<ActionResult<Guid>> ReportComment(
        Guid commentId, ContentReportRequest request, CancellationToken ct)
        => Ok(await _mediator.Send(new CreateReportCommand(
            User.GetUserId(), null, null, request.Reason, request.Description,
            CommunityCommentId: commentId), ct));
}
