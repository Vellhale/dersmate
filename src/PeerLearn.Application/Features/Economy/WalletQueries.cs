using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Community;
using PeerLearn.Domain.Economy;

namespace PeerLearn.Application.Features.Economy;

public sealed record GetWalletQuery(Guid UserId) : IRequest<WalletDto>;

/// <summary>
/// Puan özeti. Kullanıcı arayüzünden cüzdan kavramı kaldırıldı; bu uç yönetim ve
/// geçmiş görüntüleme için duruyor.
/// </summary>
/// <param name="TotalEarnedCredits">
/// Ders anlatarak biriktirilen TOPLAM puan — unvanın dayandığı sayı. Bakiyeden farklı
/// olarak asla azalmaz.
/// </param>
/// <param name="CurrentBalance">
/// Cüzdandaki güncel bakiye. Harcanacak bir yer olmadığı için ürün açısından
/// ikincildir; yönetim tarafında defter denetimi için tutuluyor.
/// </param>
/// <param name="RankTitle">Unvan adı — başlıktaki rozet bunu gösterir.</param>
/// <param name="RankEmoji">Unvan simgesi.</param>
/// <param name="NextRankAt">Bir sonraki unvanın eşiği; en üstte <c>null</c>.</param>
public sealed record WalletDto(
    int TotalEarnedCredits,
    int CurrentBalance,
    string RankTitle,
    string RankEmoji,
    int? NextRankAt,
    IReadOnlyList<CreditLotDto> ActiveLots);

/// <param name="ExpiresAtUtc">
/// Vade sonu; <c>null</c> ise puan süresizdir. Ders kazançları artık süresiz açılıyor,
/// dolu değer yalnızca eski lotlarda ve hoş geldin kredisinde görülür.
/// </param>
public sealed record CreditLotDto(int RemainingAmount, string Source, DateTime? ExpiresAtUtc);

public sealed class GetWalletHandler : IRequestHandler<GetWalletQuery, WalletDto>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public GetWalletHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<WalletDto> Handle(GetWalletQuery request, CancellationToken ct)
    {
        // Unvanın kaynağı cüzdan değil kullanıcı sayacı: cüzdanı olmayan kullanıcıda da
        // doğru sonuç dönmeli.
        var toplamKazanc = await _db.Users.AsNoTracking()
            .Where(u => u.Id == request.UserId)
            .Select(u => u.TotalEarnedCredits)
            .SingleOrDefaultAsync(ct);

        // Unvan tek yerde hesaplanıyor (profil sorgusuyla aynı saf fonksiyon); eşikler
        // iki ayrı yerde yazılsaydı er geç birbirinden ayrılırlardı.
        var rank = UserRankCalculator.Hesapla(toplamKazanc);

        var wallet = await _db.Wallets.AsNoTracking()
            .SingleOrDefaultAsync(w => w.UserId == request.UserId, ct);

        if (wallet is null)
        {
            return new WalletDto(toplamKazanc, 0, rank.Title, rank.Emoji, rank.NextRankAt, []);
        }

        var now = _clock.UtcNow;

        // Süresiz lotlar (ExpiresAtUtc == null) DA listelenir — yeni ders kazançlarının
        // tamamı böyle. Yalnızca vadesi dolmuş olanlar dışarıda kalır.
        var lots = await _db.CreditLots.AsNoTracking()
            .Where(l => l.WalletId == wallet.Id && l.RemainingAmount > 0
                        && (l.ExpiresAtUtc == null || l.ExpiresAtUtc > now))
            .OrderBy(l => l.ExpiresAtUtc)
            .Select(l => new { l.RemainingAmount, l.Source, l.ExpiresAtUtc })
            .ToListAsync(ct);

        return new WalletDto(
            toplamKazanc,
            lots.Sum(l => l.RemainingAmount),
            rank.Title,
            rank.Emoji,
            rank.NextRankAt,
            lots.Select(l => new CreditLotDto(l.RemainingAmount, l.Source.ToString(), l.ExpiresAtUtc)).ToList());
    }
}

public sealed record GetStatementQuery(Guid UserId, int Page = 1, int PageSize = 20)
    : IRequest<PagedResult<StatementEntryDto>>;

public sealed record StatementEntryDto(
    DateTime CreatedAtUtc,
    int Amount,
    string Type,
    int BalanceAfter,
    Guid? RelatedSessionId,

    /*
      Hareketin HANGİ derse ait olduğu.

      Çıplak bir SessionId kullanıcıya hiçbir şey anlatmıyordu; sıfır toplamlı bir
      ekonomide defter, kullanıcının "kredim nereye gitti" sorusunu yanıtlayabildiği TEK
      araç. Konu adı ve karşı taraf burada dönmezse arayüzün elinde gösterecek bir şey yok.
    */
    string? TopicName,
    string? CounterpartDisplayName,
    DateTime? ScheduledStartUtc);

public sealed class GetStatementHandler : IRequestHandler<GetStatementQuery, PagedResult<StatementEntryDto>>
{
    private readonly IAppDbContext _db;

    public GetStatementHandler(IAppDbContext db) => _db = db;

    public async Task<PagedResult<StatementEntryDto>> Handle(GetStatementQuery request, CancellationToken ct)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var taban =
            from t in _db.CreditTransactions.AsNoTracking()
            join w in _db.Wallets.AsNoTracking() on t.WalletId equals w.Id
            where w.UserId == request.UserId
            select t;

        // Toplam sayı DÖNMELİ: arayüz "daha var mı" sorusunu ancak böyle yanıtlayabilir.
        // Önceden liste ilk 25 satırda sessizce kesiliyordu ve devamı olduğu belli olmuyordu.
        var toplam = await taban.CountAsync(ct);

        // (WalletId, CreatedAtUtc DESC) index'i tam bu sorgu içindir.
        var rows = await taban
            .OrderByDescending(t => t.CreatedAtUtc)
            .ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new { t.CreatedAtUtc, t.Amount, t.Type, t.BalanceAfter, t.RelatedSessionId })
            .ToListAsync(ct);

        // Ders künyeleri TEK sorguda toplanır (satır başına sorgu N+1 üretirdi).
        var sessionIds = rows.Where(r => r.RelatedSessionId != null)
            .Select(r => r.RelatedSessionId!.Value)
            .Distinct()
            .ToList();

        var sessionInfo = sessionIds.Count == 0
            ? []
            : await (
                    from s in _db.LessonSessions.AsNoTracking()
                    join topic in _db.Topics.AsNoTracking() on s.TopicId equals topic.Id
                    join tutor in _db.Users.AsNoTracking() on s.TutorUserId equals tutor.Id
                    join student in _db.Users.AsNoTracking() on s.StudentUserId equals student.Id
                    where sessionIds.Contains(s.Id)
                    select new
                    {
                        s.Id,
                        TopicName = topic.Name,
                        s.ScheduledStartUtc,
                        s.TutorUserId,
                        TutorName = tutor.DisplayName,
                        StudentName = student.DisplayName
                    })
                .ToListAsync(ct);

        var byId = sessionInfo.ToDictionary(s => s.Id);

        return new PagedResult<StatementEntryDto>(
            rows.Select(t =>
            {
                var info = t.RelatedSessionId is { } id && byId.TryGetValue(id, out var s) ? s : null;

                // Karşı taraf: bu hareketin sahibi eğitmense öğrenci, değilse eğitmen.
                var counterpart = info is null
                    ? null
                    : info.TutorUserId == request.UserId ? info.StudentName : info.TutorName;

                return new StatementEntryDto(
                    t.CreatedAtUtc,
                    t.Amount,
                    t.Type.ToString(),
                    t.BalanceAfter,
                    t.RelatedSessionId,
                    info?.TopicName,
                    counterpart,
                    info?.ScheduledStartUtc);
            }).ToList(),
            toplam,
            page,
            pageSize);
    }
}
