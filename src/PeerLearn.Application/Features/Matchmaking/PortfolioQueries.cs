using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;

namespace PeerLearn.Application.Features.Matchmaking;

/// <summary>Kullanıcının kendi portföyü (iki yönlü): arayüzdeki "Verebileceklerim / Almak istediklerim".</summary>
public sealed record GetMyPortfolioQuery(Guid UserId) : IRequest<IReadOnlyList<PortfolioEntryDto>>;

public sealed record PortfolioEntryDto(
    Guid EntryId,
    Guid TopicId,
    string TopicName,
    string SubjectName,
    string CategoryName,
    string Direction,
    int SelfAssessedLevel,
    string? Note,
    bool IsVolunteer);

public sealed class GetMyPortfolioHandler : IRequestHandler<GetMyPortfolioQuery, IReadOnlyList<PortfolioEntryDto>>
{
    private readonly IAppDbContext _db;

    public GetMyPortfolioHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<PortfolioEntryDto>> Handle(GetMyPortfolioQuery request, CancellationToken ct)
    {
        var rows = await (
                from entry in _db.PortfolioEntries.AsNoTracking()
                join topic in _db.Topics on entry.TopicId equals topic.Id
                join subject in _db.Subjects on topic.SubjectId equals subject.Id
                join category in _db.EducationCategories on subject.CategoryId equals category.Id
                where entry.UserId == request.UserId && entry.IsActive
                orderby subject.Name, topic.Name
                select new
                {
                    entry.Id,
                    entry.TopicId,
                    TopicName = topic.Name,
                    SubjectName = subject.Name,
                    CategoryName = category.Name,
                    entry.Direction,
                    entry.SelfAssessedLevel,
                    entry.Note,
                    entry.IsVolunteer
                })
            .ToListAsync(ct);

        return rows
            .Select(r => new PortfolioEntryDto(r.Id, r.TopicId, r.TopicName, r.SubjectName,
                r.CategoryName, r.Direction.ToString(), r.SelfAssessedLevel, r.Note, r.IsVolunteer))
            .ToList();
    }
}
