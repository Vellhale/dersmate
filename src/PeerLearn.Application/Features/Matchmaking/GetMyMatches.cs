using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Domain.Matchmaking;

namespace PeerLearn.Application.Features.Matchmaking;

/// <summary>
/// Eşleşme ekranının üç sekmesi: bana gelen bekleyen istekler, gönderdiğim bekleyen istekler,
/// aktif (kabul edilmiş) eşleşmeler. (InitiatorUserId, Status) ve (ResponderUserId, Status)
/// index'leri her iki bacağı da karşılar.
/// </summary>
public sealed record GetMyMatchesQuery(Guid UserId) : IRequest<MyMatchesDto>;

public sealed record MyMatchesDto(
    IReadOnlyList<MatchListItemDto> Incoming,
    IReadOnlyList<MatchListItemDto> Outgoing,
    IReadOnlyList<MatchListItemDto> Active);

public sealed record MatchListItemDto(
    Guid MatchId,
    Guid OtherUserId,
    string OtherDisplayName,
    Guid RequestedTopicId,
    string RequestedTopicName,
    Guid? OfferedTopicId,
    string? OfferedTopicName,
    string Status,
    Guid? ConversationId,
    bool IAmInitiator,

    /// <summary>
    /// Karşı tarafın BANA anlatacağı konudaki ilanı gönüllü mü (0 kredi).
    ///
    /// Rezervasyon penceresi ücreti buradan yazar. Önceden süre listesi sabit metinle
    /// "1 kredi" diyordu ama BookSession ücreti 0'a düşürüyordu: kullanıcı ücretsiz dersi
    /// ilk kez ders ONAYINDA öğreniyordu. Fiyat karar anında görünmeli.
    /// </summary>
    bool TutorOffersVolunteer,

    DateTime CreatedAtUtc);

public sealed class GetMyMatchesHandler : IRequestHandler<GetMyMatchesQuery, MyMatchesDto>
{
    private readonly IAppDbContext _db;

    public GetMyMatchesHandler(IAppDbContext db) => _db = db;

    public async Task<MyMatchesDto> Handle(GetMyMatchesQuery request, CancellationToken ct)
    {
        var me = request.UserId;

        var rows = await (
                from m in _db.Matches.AsNoTracking()
                where (m.InitiatorUserId == me || m.ResponderUserId == me) &&
                      (m.Status == MatchStatus.Pending || m.Status == MatchStatus.Accepted)
                join initiator in _db.Users.AsNoTracking() on m.InitiatorUserId equals initiator.Id
                join responder in _db.Users.AsNoTracking() on m.ResponderUserId equals responder.Id
                join requested in _db.Topics.AsNoTracking() on m.RequestedTopicId equals requested.Id
                from offered in _db.Topics.AsNoTracking().Where(t => t.Id == m.OfferedTopicId).DefaultIfEmpty()
                from conversation in _db.Conversations.AsNoTracking().Where(c => c.MatchId == m.Id).DefaultIfEmpty()
                orderby m.CreatedAtUtc descending
                select new
                {
                    MatchId = m.Id,
                    m.InitiatorUserId,
                    m.ResponderUserId,
                    InitiatorName = initiator.DisplayName,
                    ResponderName = responder.DisplayName,
                    m.RequestedTopicId,
                    RequestedTopicName = requested.Name,
                    m.OfferedTopicId,
                    OfferedTopicName = offered != null ? offered.Name : null,
                    m.Status,
                    ConversationId = (Guid?)conversation.Id,
                    m.CreatedAtUtc
                })
            .ToListAsync(ct);

        /*
          Gönüllü bayrağı, karşı tarafın BANA anlatacağı konudaki AKTİF Offer kaydından
          okunur — BookSession.cs ücreti tam olarak aynı kayıttan hesaplıyor, yani ekranda
          yazan fiyat ile uygulanan fiyat aynı kaynaktan gelir.

          Tek sorguda toplanıp bellekte eşleniyor: satır başına ayrı sorgu N+1 üretirdi.
        */
        var teachablePairs = rows
            .Select(r => new
            {
                TutorId = r.InitiatorUserId == me ? r.ResponderUserId : r.InitiatorUserId,
                TopicId = r.InitiatorUserId == me ? r.RequestedTopicId : r.OfferedTopicId
            })
            .Where(x => x.TopicId is not null)
            .ToList();

        var tutorIds = teachablePairs.Select(x => x.TutorId).Distinct().ToList();
        var topicIds = teachablePairs.Select(x => x.TopicId!.Value).Distinct().ToList();

        var volunteerOffers = tutorIds.Count == 0
            ? []
            : await _db.PortfolioEntries.AsNoTracking()
                .Where(p => p.IsActive
                            && p.IsVolunteer
                            && p.Direction == PortfolioDirection.Offer
                            && tutorIds.Contains(p.UserId)
                            && topicIds.Contains(p.TopicId))
                .Select(p => new { p.UserId, p.TopicId })
                .ToListAsync(ct);

        var volunteerSet = volunteerOffers.Select(o => (o.UserId, o.TopicId)).ToHashSet();

        var items = rows.Select(r =>
        {
            var iAmInitiator = r.InitiatorUserId == me;
            var tutorId = iAmInitiator ? r.ResponderUserId : r.InitiatorUserId;
            var teachTopicId = iAmInitiator ? r.RequestedTopicId : r.OfferedTopicId;

            var volunteer = teachTopicId is not null && volunteerSet.Contains((tutorId, teachTopicId.Value));

            return new MatchListItemDto(
                r.MatchId,
                iAmInitiator ? r.ResponderUserId : r.InitiatorUserId,
                iAmInitiator ? r.ResponderName : r.InitiatorName,
                r.RequestedTopicId,
                r.RequestedTopicName,
                r.OfferedTopicId,
                r.OfferedTopicName,
                r.Status.ToString(),
                r.ConversationId,
                iAmInitiator,
                volunteer,
                r.CreatedAtUtc);
        }).ToList();

        return new MyMatchesDto(
            Incoming: items.Where(i => i.Status == nameof(MatchStatus.Pending) && !i.IAmInitiator).ToList(),
            Outgoing: items.Where(i => i.Status == nameof(MatchStatus.Pending) && i.IAmInitiator).ToList(),
            Active: items.Where(i => i.Status == nameof(MatchStatus.Accepted)).ToList());
    }
}
