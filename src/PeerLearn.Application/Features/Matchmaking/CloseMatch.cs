using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Matchmaking;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Application.Features.Matchmaking;

/// <summary>
/// Eşleşmeyi sonlandırma. Taraflardan HERHANGİ BİRİ tek başına kapatabilir.
/// </summary>
/// <remarks>
/// NEDEN TEK TARAFLI: eşleşmenin devamı karşılıklı rızaya bağlıdır. Kapatmak için karşı
/// tarafın onayını şart koşmak, ilişkiyi sürdürmek istemeyen kişiyi karşı tarafın
/// sessizliğine mahkûm ederdi — ve bu akış aynı zamanda taciz edici bir muhataptan
/// çekilmenin yolu. Onay beklenmez.
///
/// AMA AÇIK DERS VARKEN KAPATILAMAZ. Devam eden bir rezervasyonun escrow'unda kredi
/// duruyor ve o kredinin akıbeti ancak onay/iptal/itiraz ile belirlenir. Kapatma bunları
/// erişilemez kılsaydı (sohbet kapanır, ders akışı eşleşmeye bağlı) kredi süresiz askıda
/// kalırdı. Bu yüzden kapatma, açık ders varken 409 döner ve kullanıcıya önce dersi
/// sonuçlandırması söylenir. Tacizden çekilme ihtiyacı bu durumda da karşılanır:
/// engelleme/şikâyet yolu ayrıdır ve derse bağlı değildir.
/// </remarks>
public sealed record CloseMatchCommand(Guid MatchId, Guid ActorUserId) : IRequest<CloseMatchResult>;

public sealed record CloseMatchResult(Guid MatchId, string Status, bool AlreadyClosed);

public sealed class CloseMatchHandler : IRequestHandler<CloseMatchCommand, CloseMatchResult>
{
    /// <summary>Sonuçlanmamış ders durumları — bunlardan biri varsa eşleşme kapatılamaz.</summary>
    private static readonly SessionStatus[] AcikDersDurumlari =
    [
        SessionStatus.Booked,
        SessionStatus.AwaitingApproval,
        SessionStatus.Disputed
    ];

    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public CloseMatchHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<CloseMatchResult> Handle(CloseMatchCommand request, CancellationToken ct)
    {
        var match = await _db.Matches.SingleOrDefaultAsync(m => m.Id == request.MatchId, ct)
                    ?? throw new AppException(ErrorCodes.MatchNotFound, "Eşleşme bulunamadı.", statusCode: 404);

        if (match.InitiatorUserId != request.ActorUserId && match.ResponderUserId != request.ActorUserId)
        {
            throw new AppException(ErrorCodes.NotMatchParticipant,
                "Bu eşleşmeyi yalnızca tarafları sonlandırabilir.", statusCode: 403);
        }

        /*
          ZATEN KAPALIYSA HATA DEĞİL. İki taraf aynı anda "sonlandır" diyebilir; ikinciye
          hata dönmek, isteği yerine gelmiş bir kullanıcıya başarısızlık göstermek olurdu.
          İşlem idempotent: sonuç aynı, kapatan kişi ve zaman İLK kapatanınki kalır.
        */
        if (match.Status == MatchStatus.Closed)
        {
            return new CloseMatchResult(match.Id, match.Status.ToString(), AlreadyClosed: true);
        }

        if (match.Status != MatchStatus.Accepted)
        {
            throw new AppException(ErrorCodes.MatchNotAccepted,
                $"Yalnızca kabul edilmiş bir eşleşme sonlandırılabilir (şu an: {match.Status}).",
                statusCode: 409);
        }

        var acikDers = await _db.LessonSessions.AsNoTracking()
            .CountAsync(s => s.MatchId == match.Id && AcikDersDurumlari.Contains(s.Status), ct);

        if (acikDers > 0)
        {
            throw new AppException(ErrorCodes.MatchHasActiveSessions,
                $"Bu eşleşmede sonuçlanmamış {acikDers} ders var. Eşleşmeyi kapatmadan önce " +
                "o dersleri tamamla, onayla ya da iptal et.",
                statusCode: 409);
        }

        match.Status = MatchStatus.Closed;
        match.ClosedAtUtc = _clock.UtcNow;
        match.ClosedByUserId = request.ActorUserId;

        await _db.SaveChangesAsync(ct);

        return new CloseMatchResult(match.Id, match.Status.ToString(), AlreadyClosed: false);
    }
}
