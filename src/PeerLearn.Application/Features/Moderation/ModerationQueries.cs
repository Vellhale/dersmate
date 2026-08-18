using System.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Economy;
using PeerLearn.Domain.Identity;
using PeerLearn.Domain.Moderation;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Application.Features.Moderation;

// ---------------------------------------------------------------------------
// 1) İtiraz detayı — hakemin karar vermek için ihtiyaç duyduğu HER ŞEY tek çağrıda
// ---------------------------------------------------------------------------

public sealed record GetDisputeDetailQuery(Guid DisputeId) : IRequest<DisputeDetailDto>;

public sealed record DisputePartyDto(
    Guid UserId,
    string DisplayName,
    string Email,
    string Status,
    decimal AverageRating,
    int RatingCount,
    int PastDisputesAgainst,
    DateTime JoinedAtUtc);

public sealed record DisputeProofDto(
    Guid ProofId,
    Guid UploadedByUserId,
    string Sha256Hash,
    bool IsDuplicateHash,
    DateTime UploadedAtUtc);

public sealed record DisputeDetailDto(
    Guid DisputeId,
    string Reason,
    string Status,
    string Description,
    DateTime CreatedAtUtc,
    string? TutorStatement,
    DateTime? TutorStatementAtUtc,
    string? ResolutionNote,
    DateTime? ResolvedAtUtc,

    // Ders
    Guid SessionId,
    string SessionStatus,
    string VerificationCode,
    string TopicName,
    string SubjectName,
    DateTime ScheduledStartUtc,
    DateTime ScheduledEndUtc,
    int DurationMinutes,
    int CreditCost,
    DateTime? CompletionRequestedAtUtc,

    // Taraflar
    DisputePartyDto Tutor,
    DisputePartyDto Student,

    // Kanıt
    IReadOnlyList<DisputeProofDto> Proofs,

    /// <summary>
    /// Bu ders için henüz puan basılmadı mı? true ise hakemin kararı eğitmenin puanını
    /// belirleyecek demektir (eğitmen lehine karar basım üretir, öğrenci lehine üretmez).
    /// </summary>
    bool MintPending);

public sealed class GetDisputeDetailHandler : IRequestHandler<GetDisputeDetailQuery, DisputeDetailDto>
{
    private readonly IAppDbContext _db;

    public GetDisputeDetailHandler(IAppDbContext db) => _db = db;

    public async Task<DisputeDetailDto> Handle(GetDisputeDetailQuery request, CancellationToken ct)
    {
        var row = await (
                from d in _db.Disputes.AsNoTracking()
                join s in _db.LessonSessions.AsNoTracking() on d.SessionId equals s.Id
                join topic in _db.Topics.AsNoTracking() on s.TopicId equals topic.Id
                join subject in _db.Subjects.AsNoTracking() on topic.SubjectId equals subject.Id
                join tutor in _db.Users.AsNoTracking() on s.TutorUserId equals tutor.Id
                join student in _db.Users.AsNoTracking() on s.StudentUserId equals student.Id
                where d.Id == request.DisputeId
                select new { d, s, TopicName = topic.Name, SubjectName = subject.Name, tutor, student })
            .SingleOrDefaultAsync(ct)
            ?? throw new AppException(ErrorCodes.DisputeNotFound, "İtiraz bulunamadı.", statusCode: 404);

        var proofs = await _db.SessionProofs.AsNoTracking()
            .Where(p => p.SessionId == row.s.Id)
            .OrderBy(p => p.CreatedAtUtc)
            .Select(p => new DisputeProofDto(p.Id, p.UploadedByUserId, p.Sha256Hash,
                p.IsDuplicateHash, p.CreatedAtUtc))
            .ToListAsync(ct);

        /*
          Basım henüz yapılmadı mı? Karar öncesi hakem, kararının puan üretip üretmeyeceğini
          görmeli. Escrow kalktığı için "kredi bloke mi" sorusunun yerini bu aldı: ders için
          daha önce bir LessonEarning hareketi yazılmışsa karar puan üretmez.
        */
        var basimBekliyor = !await _db.CreditTransactions.AsNoTracking()
            .AnyAsync(t => t.RelatedSessionId == row.s.Id
                           && t.Type == CreditTransactionType.LessonEarning, ct);

        // Geçmiş itiraz sayısı: tekrar eden şikâyetçi/şikâyet edilen davranışı hakem için
        // güçlü bir sinyal. Yalnızca ÇÖZÜLMÜŞ ve aleyhe sonuçlanmış olanlar sayılır —
        // açılmış her itirazı saymak, haklı çıkan kullanıcıyı da suçlu gösterirdi.
        var tutorLost = await _db.Disputes.AsNoTracking()
            .Join(_db.LessonSessions.AsNoTracking(), d => d.SessionId, s => s.Id, (d, s) => new { d, s })
            .CountAsync(x => x.s.TutorUserId == row.tutor.Id
                             && x.d.Status == DisputeStatus.ResolvedForStudent, ct);

        var studentLost = await _db.Disputes.AsNoTracking()
            .Join(_db.LessonSessions.AsNoTracking(), d => d.SessionId, s => s.Id, (d, s) => new { d, s })
            .CountAsync(x => x.s.StudentUserId == row.student.Id
                             && x.d.Status == DisputeStatus.ResolvedForTutor, ct);

        return new DisputeDetailDto(
            row.d.Id,
            row.d.Reason.ToString(),
            row.d.Status.ToString(),
            row.d.Description,
            row.d.CreatedAtUtc,
            row.d.TutorStatement,
            row.d.TutorStatementAtUtc,
            row.d.ResolutionNote,
            row.d.ResolvedAtUtc,
            row.s.Id,
            row.s.Status.ToString(),
            row.s.VerificationCode,
            row.TopicName,
            row.SubjectName,
            row.s.ScheduledStartUtc,
            row.s.ScheduledEndUtc,
            row.s.DurationMinutes,
            row.s.CreditCost,
            row.s.CompletionRequestedAtUtc,
            new DisputePartyDto(row.tutor.Id, row.tutor.DisplayName, row.tutor.Email,
                row.tutor.Status.ToString(), row.tutor.AverageRating, row.tutor.RatingCount,
                tutorLost, row.tutor.CreatedAtUtc),
            new DisputePartyDto(row.student.Id, row.student.DisplayName, row.student.Email,
                row.student.Status.ToString(), row.student.AverageRating, row.student.RatingCount,
                studentLost, row.student.CreatedAtUtc),
            proofs,
            basimBekliyor);
    }
}

// ---------------------------------------------------------------------------
// 2) Ekonomi izleme
// ---------------------------------------------------------------------------

public sealed record GetEconomyMetricsQuery : IRequest<EconomyMetricsDto>;

public sealed record EconomyMetricsDto(
    /// <summary>
    /// Dolaşımdaki toplam puan. Artık AvailableCredits ile aynıdır ve ikisi de duruyor:
    /// "defter tutuyor mu" kontrolü dolaşımı defter toplamıyla karşılaştırıyor, kart ise
    /// kullanılabilir bakiyeyi gösteriyor. Eskiden aradaki fark bloke (escrow) bakiyeydi;
    /// bloke kavramı kalktı, alan adları anlamlarını koruyor.
    /// </summary>
    int CirculatingCredits,
    int AvailableCredits,
    int TotalMinted,
    int TotalExpired,
    int ExpiringWithin7Days,
    int WalletCount,
    int ActiveSessions,
    int AwaitingApproval,
    int DisputedSessions,
    int OpenDisputes,

    /// <summary>Açık şikayet sayısı — yönetim sekmesindeki rozet.</summary>
    int OpenReports,
    int PendingTeacherCandidates,
    int BannedUsers,
    int ActiveHwidBans,

    /// <summary>
    /// Arka plan süpürücüsünün üst üste başarısız olduğu ve geri çekilmeye alınmış kayıt
    /// sayısı. Sıfırdan büyükse otomatik onay/iade o kayıtlarda İŞLEMİYOR demektir —
    /// hiçbir kullanıcı şikâyeti gelmeden görülmesi gereken tek sinyal budur.
    /// </summary>
    int StuckSweepRecords,

    bool LedgerBalanced);

public sealed class GetEconomyMetricsHandler : IRequestHandler<GetEconomyMetricsQuery, EconomyMetricsDto>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public GetEconomyMetricsHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<EconomyMetricsDto> Handle(GetEconomyMetricsQuery request, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        /*
          TEK ANLIK GÖRÜNTÜ (REPEATABLE READ).

          Bu panelin tek gerçek iddiası "dolaşımdaki kredi = basılan − yakılan" eşitliği.
          Sorgular varsayılan ReadCommitted'ta ayrı ayrı koşarken her biri BAŞKA bir ana
          ait görüntü okuyabiliyordu: cüzdanlar okunduktan sonra araya giren tek bir hoş
          geldin kredisi, eşitliği hiçbir şey bozulmamışken bozuk gösteriyordu. Sürekli
          yanlış alarm veren bir alarm, kapatılan alarmdır — bu bayrağın kıymeti tam da
          nadiren yanmasında.

          Yalnızca okuma yapıldığı için serileştirme çakışması riski yok; işlem sonunda
          commit değil rollback edilir (hiçbir şey yazılmadı, niyeti de yok).
        */
        await using var snapshot = await _db.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);

        /*
          AYNI TABLOYU BİRDEN FAZLA KEZ TARAMA.

          Bu üç sayı (kullanılabilir toplam, bloke toplam, cüzdan adedi) önce üç ayrı
          sorguyla okunuyordu; PostgreSQL Wallets'ı ÜÇ KEZ baştan sona tarıyordu. Aynı
          hata CreditTransactions'ta iki, LessonSessions'ta üç kez tekrarlanıyordu:
          panel tek açılışta sekiz gereksiz tam tarama üretiyordu.

          Tek toplulaştırma sorgusu hepsini bir taramada verir.
        */
        var cuzdanToplami = await _db.Wallets.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Available = g.Sum(w => w.AvailableBalance),
                Count = g.Count()
            })
            .SingleOrDefaultAsync(ct);

        // Hiç cüzdan yoksa GroupBy hiç satır üretmez (null) — sıfırlarla devam edilir.
        var available = cuzdanToplami?.Available ?? 0;
        var walletCount = cuzdanToplami?.Count ?? 0;

        /*
          ARZ MUHASEBESİ — TANIM DEĞİŞTİ.

          Sıfır toplamlı modelde basım yalnızca hoş geldin kredisiydi; ders kazancı bir
          transferin bacağıydı ve arzı değiştirmezdi. Artık ders kazancı KARŞILIKSIZ BASIM,
          yani arzı doğrudan büyütüyor. LessonEarning basılan tarafa eklenmezse panel her
          onaylanan derste "defter tutmuyor" derdi — doğru olan sayı değil, tanım eskiydi.

          Tüm türler tek sorguda okunuyor; hangi türün basım hangisinin yakım olduğu
          aşağıda tek yerde kararlaştırılıyor.
        */
        var arzHareketleri = await _db.CreditTransactions.AsNoTracking()
            .GroupBy(t => t.Type)
            .Select(g => new { Type = g.Key, Total = g.Sum(t => t.Amount) })
            .ToListAsync(ct);

        int TurToplami(CreditTransactionType tur) =>
            arzHareketleri.FirstOrDefault(x => x.Type == tur)?.Total ?? 0;

        var minted = TurToplami(CreditTransactionType.WelcomeBonus)
                     + TurToplami(CreditTransactionType.LessonEarning);

        var expired = TurToplami(CreditTransactionType.Expiry);

        // Defterin TAMAMI: hangi tür olursa olsun tüm hareketlerin toplamı.
        var defterToplami = arzHareketleri.Sum(x => x.Total);

        // Expiry hareketleri negatif yazılır; panelde pozitif göstermek için işaret çevrilir.
        var burned = Math.Abs(expired);

        var expiringSoon = await _db.CreditLots.AsNoTracking()
            .Where(l => l.RemainingAmount > 0 && l.ExpiresAtUtc > now && l.ExpiresAtUtc <= now.AddDays(7))
            .SumAsync(l => (int?)l.RemainingAmount, ct) ?? 0;

        // Üç ders durumu da tek gruplu sayımdan gelir.
        var dersDurumlari = await _db.LessonSessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Booked ||
                        s.Status == SessionStatus.AwaitingApproval ||
                        s.Status == SessionStatus.Disputed)
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int DersSayisi(SessionStatus durum) =>
            dersDurumlari.FirstOrDefault(x => x.Status == durum)?.Count ?? 0;

        var activeSessions = DersSayisi(SessionStatus.Booked);
        var awaitingApproval = DersSayisi(SessionStatus.AwaitingApproval);
        var disputedSessions = DersSayisi(SessionStatus.Disputed);

        var openReports = await _db.Reports.AsNoTracking()
            .CountAsync(x => x.Status == ReportStatus.Open, ct);

        var openDisputes = await _db.Disputes.AsNoTracking()
            .CountAsync(d => d.Status == DisputeStatus.Open || d.Status == DisputeStatus.UnderReview, ct);

        // Öğretmen adaylığı kuyruğu: panele girmeden bekleyen iş olduğu görülebilsin.
        var pendingCandidates = await _db.TeacherCandidateProfiles.AsNoTracking()
            .CountAsync(p => p.VerifiedAtUtc == null && p.RejectedAtUtc == null, ct);

        var bannedUsers = await _db.Users.AsNoTracking()
            .CountAsync(u => u.Status == UserStatus.Banned, ct);

        var activeHwidBans = await _db.HwidBans.AsNoTracking()
            .CountAsync(b => b.IsActive && (b.ExpiresAtUtc == null || b.ExpiresAtUtc > now), ct);

        // Takılı süpürme kayıtları: tek başarısızlık geçici bir aksaklık olabilir, ikincisi
        // artık desen demektir — panelde ancak ikinciden itibaren gösterilir.
        var stuckSweepRecords = await _db.SweepFailures.AsNoTracking()
            .CountAsync(f => f.FailureCount >= 2, ct);

        /*
          DEFTER DENETİMİ — panelin en önemli tek sayısı.

          ESKİ DEĞİŞMEZ: "toplam sabittir" (sıfır toplam). Bu artık geçerli değil, çünkü
          her onaylanan ders yeni puan basıyor.

          YENİ DEĞİŞMEZ: "her kredi kayıtlı bir hareketten gelir" — cüzdanlardaki toplam,
          defterdeki tüm hareketlerin toplamına eşit olmalı. Bu daha zayıf bir denetim
          değil, DAHA GENEL bir denetim: hangi ekonomi modeli seçilirse seçilsin geçerli
          kalır ve tam olarak yakalaması gereken şeyi yakalar — defterde karşılığı olmadan
          bir cüzdana yazılmış kredi.

          Bu yüzden karşılaştırma "basılan − yakılan" değil, doğrudan defter toplamı.
        */
        var circulating = available;
        var ledgerBalanced = circulating == defterToplami;

        return new EconomyMetricsDto(
            circulating, available,
            minted, burned, expiringSoon, walletCount,
            activeSessions, awaitingApproval, disputedSessions, openDisputes, openReports,
            pendingCandidates,
            bannedUsers, activeHwidBans,
            stuckSweepRecords,
            ledgerBalanced);
    }
}

// ---------------------------------------------------------------------------
// 3) Denetim izi listesi
// ---------------------------------------------------------------------------

public sealed record GetAuditLogQuery(int Page = 1, int PageSize = 25) : IRequest<PagedResult<AuditLogRowDto>>;

public sealed record AuditLogRowDto(
    Guid Id,
    Guid ActorUserId,
    string ActorDisplayName,
    string ActorRole,
    string Action,
    string TargetType,
    Guid? TargetId,
    string Summary,
    DateTime CreatedAtUtc);

public sealed class GetAuditLogHandler : IRequestHandler<GetAuditLogQuery, PagedResult<AuditLogRowDto>>
{
    private readonly IAppDbContext _db;

    public GetAuditLogHandler(IAppDbContext db) => _db = db;

    public async Task<PagedResult<AuditLogRowDto>> Handle(GetAuditLogQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query =
            from log in _db.AdminActionLogs.AsNoTracking()
            join actor in _db.Users.AsNoTracking() on log.ActorUserId equals actor.Id
            select new { log, actor };

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(x => x.log.CreatedAtUtc)
            .ThenByDescending(x => x.log.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AuditLogRowDto(
                x.log.Id,
                x.log.ActorUserId,
                x.actor.DisplayName,
                x.log.ActorRole.ToString(),
                x.log.Action.ToString(),
                x.log.TargetType,
                x.log.TargetId,
                x.log.Summary,
                x.log.CreatedAtUtc))
            .ToListAsync(ct);

        return new PagedResult<AuditLogRowDto>(rows, total, page, pageSize);
    }
}
