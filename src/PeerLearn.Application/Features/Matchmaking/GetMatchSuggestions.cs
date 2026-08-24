using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Domain.Community;
using PeerLearn.Domain.Identity;
using PeerLearn.Domain.Matchmaking;

namespace PeerLearn.Application.Features.Matchmaking;

/// <summary>
/// ÇAPRAZ EŞLEŞME ALGORİTMASI (Modül 1.2). İki adım:
///   1. Benim Seek ettiğim konuları Offer eden aktif kullanıcılar (arz ∩ talep).
///   2. Bunlardan hangileri benim Offer ettiklerimi Seek ediyor? → IsCrossMatch=true.
/// Kredi ekonomisi sayesinde tek yönlü eşleşme de geçerlidir; çapraz olanlar üste sıralanır
/// (takas iki tarafa da kredi kazandırdığı için tercih edilir), ardından puana göre.
/// (TopicId, Direction) partial index'i (IsActive=TRUE) her iki adımı da karşılar.
/// </summary>
public sealed record GetMatchSuggestionsQuery(Guid UserId, int Limit = 20)
    : IRequest<IReadOnlyList<MatchSuggestionDto>>;

/// <param name="Bio">
/// Kullanıcının kendi tanıtım cümlesi. Kartta gösterilir — Keşfet kartı yalnızca konu
/// listesi taşıyordu ve kişiler birbirinden ayırt edilemiyordu; bio, karta kimlik verir.
/// Girilmemişse null: arayüz satırı tamamen düşürür, boş bir çizgi bırakmaz.
/// </param>
/// <param name="Level">
/// Genel seviye (1-10). Krediden türer, veritabanında saklanmaz — bellekte hesaplanır
/// (UserLevelRules; EF bu fonksiyonu SQL'e çeviremez, projeksiyon içinde çağrılamaz).
/// </param>
public sealed record MatchSuggestionDto(
    Guid UserId,
    string DisplayName,
    string? Bio,
    int Level,
    decimal AverageRating,
    int RatingCount,
    bool IsCrossMatch,
    IReadOnlyList<SuggestedTopicDto> TheyCanTeach,
    IReadOnlyList<SuggestedTopicDto> TheyWantToLearn);

/// <param name="IsVolunteer">
/// Gönüllü (0 kredi) ilan. Yalnızca "anlatabilir" bacağında anlamlıdır; talep bacağında
/// her zaman false döner (ücret, ANLATANIN ilanının özelliğidir).
/// </param>
public sealed record SuggestedTopicDto(
    Guid TopicId,
    string TopicName,
    string SubjectName,
    int SelfAssessedLevel,
    bool IsVolunteer);

public sealed class GetMatchSuggestionsHandler
    : IRequestHandler<GetMatchSuggestionsQuery, IReadOnlyList<MatchSuggestionDto>>
{
    private readonly IAppDbContext _db;

    public GetMatchSuggestionsHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<MatchSuggestionDto>> Handle(GetMatchSuggestionsQuery request, CancellationToken ct)
    {
        var me = request.UserId;
        var limit = Math.Clamp(request.Limit, 1, 50);

        var mySeeks = _db.PortfolioEntries.Where(p =>
            p.UserId == me && p.IsActive && p.Direction == PortfolioDirection.Seek);
        var myOffers = _db.PortfolioEntries.Where(p =>
            p.UserId == me && p.IsActive && p.Direction == PortfolioDirection.Offer);

        /*
          Adım 0: aday kümesini SUNUCUDA daralt — popüler bir konuyu binlerce kişi Offer
          ediyor olabilir; tüm (kullanıcı×konu) satırlarını belleğe çekmek yerine en yüksek
          puanlı limit×3 kullanıcı seçilir (×3: çapraz eşleşme yeniden sıralaması için pay).

          Kullanıcı üzerinden EXISTS ile yazılır, PortfolioEntries üzerinden GROUP BY ile
          DEĞİL: ikisi de aynı sonucu verir ama GROUP BY formu planda HashAggregate üretip
          eşleşen HER kullanıcıyı (ölçümde 10.062) grupluyordu. EXISTS formunda PostgreSQL
          Hash Semi Join seçiyor ve tekilleştirme maliyeti kalkıyor.
          Ölçüm (10.062 aday): 12,5 ms -> 8,1 ms.
        */
        var topUserIds = await _db.Users.AsNoTracking()
            .Where(user => user.Status == UserStatus.Active
                           && user.Id != me
                           && _db.PortfolioEntries.Any(offer =>
                               offer.UserId == user.Id
                               && offer.IsActive
                               && offer.Direction == PortfolioDirection.Offer
                               && mySeeks.Any(s => s.TopicId == offer.TopicId)))
            .OrderByDescending(user => user.AverageRating)
            .ThenByDescending(user => user.RatingCount)
            .Select(user => user.Id)
            .Take(limit * 3)
            .ToListAsync(ct);

        if (topUserIds.Count == 0)
        {
            return [];
        }

        // Adım 1: arz ∩ talep — seçilen adayların konu detayları.
        var candidates = await (
                from offer in _db.PortfolioEntries.AsNoTracking()
                join topic in _db.Topics on offer.TopicId equals topic.Id
                join subject in _db.Subjects on topic.SubjectId equals subject.Id
                join user in _db.Users on offer.UserId equals user.Id
                where offer.IsActive
                      && offer.Direction == PortfolioDirection.Offer
                      && topUserIds.Contains(offer.UserId)
                      && mySeeks.Any(s => s.TopicId == offer.TopicId)
                select new
                {
                    offer.UserId,
                    user.DisplayName,
                    user.Bio,
                    user.TotalEarnedCredits,
                    user.AverageRating,
                    user.RatingCount,
                    offer.TopicId,
                    TopicName = topic.Name,
                    SubjectName = subject.Name,
                    offer.SelfAssessedLevel,
                    offer.IsVolunteer
                })
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return [];
        }

        // Adım 2: çapraz bacak — adaylardan benim verebildiğimi arayanlar.
        var candidateIds = candidates.Select(c => c.UserId).Distinct().ToList();
        var reciprocal = await (
                from seek in _db.PortfolioEntries.AsNoTracking()
                join topic in _db.Topics on seek.TopicId equals topic.Id
                join subject in _db.Subjects on topic.SubjectId equals subject.Id
                where seek.IsActive
                      && seek.Direction == PortfolioDirection.Seek
                      && candidateIds.Contains(seek.UserId)
                      && myOffers.Any(o => o.TopicId == seek.TopicId)
                select new
                {
                    seek.UserId,
                    seek.TopicId,
                    TopicName = topic.Name,
                    SubjectName = subject.Name,
                    seek.SelfAssessedLevel
                })
            .ToListAsync(ct);

        var reciprocalByUser = reciprocal.ToLookup(r => r.UserId);

        return candidates
            .GroupBy(c => new { c.UserId, c.DisplayName, c.Bio, c.TotalEarnedCredits, c.AverageRating, c.RatingCount })
            .Select(g => new MatchSuggestionDto(
                g.Key.UserId,
                g.Key.DisplayName,
                g.Key.Bio,
                // Seviye burada, bellekte: gruplamadan sonra kişi başına BİR kez hesaplanır.
                UserLevelRules.Hesapla(g.Key.TotalEarnedCredits).Level,
                g.Key.AverageRating,
                g.Key.RatingCount,
                IsCrossMatch: reciprocalByUser[g.Key.UserId].Any(),
                TheyCanTeach: g
                    .Select(c => new SuggestedTopicDto(
                        c.TopicId, c.TopicName, c.SubjectName, c.SelfAssessedLevel, c.IsVolunteer))
                    .DistinctBy(t => t.TopicId)
                    .ToList(),
                // Talep bacağında ücret kavramı yok: IsVolunteer sabit false.
                TheyWantToLearn: reciprocalByUser[g.Key.UserId]
                    .Select(r => new SuggestedTopicDto(
                        r.TopicId, r.TopicName, r.SubjectName, r.SelfAssessedLevel, false))
                    .DistinctBy(t => t.TopicId)
                    .ToList()))
            .OrderByDescending(s => s.IsCrossMatch)
            .ThenByDescending(s => s.AverageRating)
            .ThenByDescending(s => s.RatingCount)
            .Take(limit)
            .ToList();
    }
}
