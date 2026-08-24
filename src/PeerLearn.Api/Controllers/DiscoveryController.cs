using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeerLearn.Application.Common;
using PeerLearn.Application.Features.Discovery;

namespace PeerLearn.Api.Controllers;

/// <summary>Gelişmiş arama ve filtreleme (Modül 1).</summary>
[ApiController]
[Authorize]
[Route("api/discovery")]
public sealed class DiscoveryController : ControllerBase
{
    private readonly IMediator _mediator;

    public DiscoveryController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Filtrelenmiş ders ilanı listesi.
    ///
    /// Parametreler [FromQuery] ile alınır: arama sonucu paylaşılabilir/yer imlenebilir bir
    /// URL olmalı ve arayüz filtreleri adres çubuğunda tutabilmeli. POST gövdesi bunu
    /// imkânsız kılardı.
    /// </summary>
    [HttpGet("offers")]
    public async Task<PagedResult<OfferCardDto>> SearchOffers(
        [FromQuery] string? search,
        [FromQuery] Guid? categoryId,
        [FromQuery] Guid? subjectId,
        [FromQuery] Guid? topicId,
        [FromQuery] int? minLevel,
        [FromQuery] int? maxLevel,
        [FromQuery] decimal? minRating,
        [FromQuery] decimal? maxRating,
        [FromQuery] bool onlyVolunteer = false,
        [FromQuery] OfferSortOrder sort = OfferSortOrder.Relevance,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return await _mediator.Send(
            new SearchOffersQuery(
                CurrentUserId: User.GetUserId(),
                Search: search,
                CategoryId: categoryId,
                SubjectId: subjectId,
                TopicId: topicId,
                MinLevel: minLevel,
                MaxLevel: maxLevel,
                MinRating: minRating,
                MaxRating: maxRating,
                OnlyVolunteer: onlyVolunteer,
                Sort: sort,
                Page: page,
                PageSize: pageSize),
            ct);
    }

    /// <summary>
    /// Üniversite ağı araması. Sonuç birimi KULLANICI, ilan değil.
    ///
    /// Yalnızca iki ölçüt alır — üniversite ve bölüm. Ders, konu ve konu seviyesi
    /// parametreleri BİLEREK yok: üniversite ağı bu kavramları taşımıyor (gerekçe
    /// SearchUniversityPeersQuery'de).
    /// </summary>
    [HttpGet("users")]
    public async Task<PagedResult<UniversityPeerDto>> SearchUniversityPeers(
        [FromQuery] string? university,
        [FromQuery] string? department,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return await _mediator.Send(
            new SearchUniversityPeersQuery(
                CurrentUserId: User.GetUserId(),
                University: university,
                Department: department,
                Page: page,
                PageSize: pageSize),
            ct);
    }
}
