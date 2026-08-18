using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Api.Authorization;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Features.Economy;
using PeerLearn.Application.Features.Maintenance;
using PeerLearn.Application.Features.Moderation;
using PeerLearn.Application.Features.Scheduling;
using PeerLearn.Domain.Identity;
using PeerLearn.Domain.Moderation;

namespace PeerLearn.Api.Controllers;

/// <summary>
/// İtiraz kuyruğu ve yaptırımlar (Modül 4.3). Sınıf düzeyinde moderatör yetkisi yeter;
/// ban gibi geri alınamaz uçlar ayrıca <see cref="Policies.AdminOnly"/> ile daraltılır.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.CanModerate)]
[Route("api/admin")]
public sealed class AdminController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IAppDbContext _db;

    public AdminController(IMediator mediator, IAppDbContext db)
    {
        _mediator = mediator;
        _db = db;
    }

    public sealed record DisputeRow(
        Guid DisputeId,
        Guid SessionId,
        string Reason,
        string Description,
        string Status,
        DateTime CreatedAtUtc);

    /// <summary>Açık itiraz kuyruğu ((Status, CreatedAtUtc) index'i ile).</summary>
    [HttpGet("disputes")]
    public async Task<IReadOnlyList<DisputeRow>> GetOpenDisputes(CancellationToken ct)
    {
        var rows = await _db.Disputes.AsNoTracking()
            .Where(d => d.Status == DisputeStatus.Open || d.Status == DisputeStatus.UnderReview)
            .OrderBy(d => d.CreatedAtUtc)
            .Take(100)
            .Select(d => new { d.Id, d.SessionId, d.Reason, d.Description, d.Status, d.CreatedAtUtc })
            .ToListAsync(ct);

        return rows
            .Select(d => new DisputeRow(d.Id, d.SessionId, d.Reason.ToString(), d.Description,
                d.Status.ToString(), d.CreatedAtUtc))
            .ToList();
    }

    /// <summary>
    /// İtiraz detayı: ders bilgisi, iki tarafın kimliği/geçmişi, kanıtlar, escrow durumu ve
    /// iki tarafın da beyanı. Hakem karar verirken kuyruktaki özet satırla yetinmemeli.
    /// </summary>
    [HttpGet("disputes/{disputeId:guid}")]
    public async Task<DisputeDetailDto> GetDisputeDetail(Guid disputeId, CancellationToken ct)
        => await _mediator.Send(new GetDisputeDetailQuery(disputeId), ct);

    /// <summary>Ekonomi izleme: dolaşımdaki kredi, vade kayıpları, aktif ders metrikleri.</summary>
    [HttpGet("metrics")]
    public async Task<EconomyMetricsDto> GetMetrics(CancellationToken ct)
        => await _mediator.Send(new GetEconomyMetricsQuery(), ct);

    /// <summary>
    /// Öğretmen adaylığı beyanları. Varsayılan görünüm karar bekleyen kuyruk;
    /// verilmiş kararlar da aynı uçtan filtreyle okunur.
    /// </summary>
    [HttpGet("teacher-candidates")]
    public async Task<PagedResult<TeacherCandidateRowDto>> GetTeacherCandidates(
        [FromQuery] TeacherCandidateFilter status = TeacherCandidateFilter.Pending,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
        => await _mediator.Send(new GetTeacherCandidatesQuery(status, page, pageSize), ct);

    public sealed record TeacherCandidateReviewRequest(TeacherCandidateDecision Decision, string Note);

    /// <summary>
    /// Beyanı doğrular, reddeder ya da önceki kararı geri alır.
    ///
    /// Moderatöre AÇIK (ban'dan farklı olarak): karar krediye dokunmaz ve geri alınabilir.
    /// Gerekçe zorunludur — sistemde öğrenci belgesi kanalı olmadığı için doğrulamanın
    /// dayanağı yalnızca bu notta kalır.
    /// </summary>
    [HttpPost("teacher-candidates/{profileId:guid}/review")]
    public async Task<ReviewTeacherCandidateResult> ReviewTeacherCandidate(
        Guid profileId, TeacherCandidateReviewRequest request, CancellationToken ct)
        => await _mediator.Send(new ReviewTeacherCandidateCommand(
            profileId, User.GetUserId(), request.Decision, request.Note), ct);

    /// <summary>Denetim izi — kim, ne zaman, neye karar verdi.</summary>
    [HttpGet("audit-log")]
    public async Task<PagedResult<AuditLogRowDto>> GetAuditLog(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
        => await _mediator.Send(new GetAuditLogQuery(page, pageSize), ct);

    public sealed record ResolveRequest(DisputeResolution Resolution, string? Note);

    [HttpPost("disputes/{disputeId:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid disputeId, ResolveRequest request, CancellationToken ct)
    {
        await _mediator.Send(new ResolveDisputeCommand(
            disputeId, User.GetUserId(), request.Resolution, request.Note), ct);

        return NoContent();
    }

    public sealed record ProofRow(
        Guid ProofId,
        Guid UploadedByUserId,
        string StorageKey,
        string Sha256Hash,
        bool IsDuplicateHash,
        DateTime UploadedAtUtc);

    /// <summary>İtiraz incelemesi için dersin kanıtları — tekrar kullanılan görseller işaretli.</summary>
    [HttpGet("sessions/{sessionId:guid}/proofs")]
    public async Task<IReadOnlyList<ProofRow>> GetSessionProofs(Guid sessionId, CancellationToken ct)
    {
        return await _db.SessionProofs.AsNoTracking()
            .Where(p => p.SessionId == sessionId)
            .OrderBy(p => p.CreatedAtUtc)
            .Select(p => new ProofRow(p.Id, p.UploadedByUserId, p.StorageKey,
                p.Sha256Hash, p.IsDuplicateHash, p.CreatedAtUtc))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Kanıt görselinin kendisi. "Sahte kanıt" itirazına karar veren yöneticinin görseli
    /// GÖRMESİ şart; hash ve dosya adı tek başına karar için yeterli değil.
    /// AsAdmin=true, katılımcı kontrolünü atlar (yetki zaten rol bazlı sağlanıyor).
    /// </summary>
    [HttpGet("sessions/{sessionId:guid}/proofs/{proofId:guid}/content")]
    public async Task<IActionResult> GetProofContent(Guid sessionId, Guid proofId, CancellationToken ct)
    {
        var proof = await _mediator.Send(
            new GetProofContentQuery(sessionId, proofId, User.GetUserId(), AsAdmin: true), ct);

        return File(proof.Content, proof.ContentType);
    }

    /// <summary>
    /// Oturum süpürmesini ŞİMDİ çalıştırır (otomatik onay + düşmüş rezervasyonlar).
    /// Normalde 10 dakikada bir arka planda koşar; bu uç iki ihtiyaç için var:
    /// (1) operasyon — bir aksaklıktan sonra beklemeden telafi, (2) test edilebilirlik.
    /// Komut idempotenttir: kapsam dışı kayıtlara dokunmaz, iki kez çalışması zarar vermez.
    /// </summary>
    [HttpPost("jobs/session-sweep")]
    public async Task<SweepSessionsResult> RunSessionSweep(CancellationToken ct)
        => await _mediator.Send(new SweepSessionsCommand(), ct);

    /// <summary>Kredi vade süpürmesini ŞİMDİ çalıştırır (30 gün kuralı). Normalde 15 dakikada bir.</summary>
    [HttpPost("jobs/credit-expiry")]
    public async Task<ExpireCreditsResult> RunCreditExpiry(CancellationToken ct)
        => await _mediator.Send(new ExpireCreditsCommand(), ct);

    /// <summary>
    /// Depo bakımını ŞİMDİ çalıştırır: saklama süresi dolan kanıt görselleri + artık dosyalar.
    /// Normalde günde bir koşar; burada elle tetiklenebilmesi hem operasyon (disk doldu) hem de
    /// test edilebilirlik içindir. Silme yaptığı için yalnızca ADMIN — moderatöre kapalı.
    /// </summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPost("jobs/storage-cleanup")]
    public async Task<CleanupStorageResult> RunStorageCleanup(CancellationToken ct)
        => await _mediator.Send(new CleanupStorageCommand(), ct);

    public sealed record BanRequest(string Reason);

    /// <summary>
    /// Kalıcı ban: hesap + kullanıcının tüm bilinen cihaz kimlikleri (HWID).
    /// Moderatöre KAPALI: geri alınması en pahalı işlem, tek moderatör kararıyla verilmemeli.
    /// </summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPost("users/{userId:guid}/ban")]
    public async Task<BanUserResult> Ban(Guid userId, BanRequest request, CancellationToken ct)
        => await _mediator.Send(new BanUserCommand(userId, User.GetUserId(), request.Reason), ct);

    /// <summary>
    /// Ban kaldırma. Hesabı ve bu kullanıcı yüzünden konmuş cihaz banlarını serbest bırakır.
    /// Ban ile aynı yetki seviyesinde: kararı verebilen geri de alabilmeli.
    /// </summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPost("users/{userId:guid}/unban")]
    public async Task<UnbanUserResult> Unban(Guid userId, BanRequest request, CancellationToken ct)
        => await _mediator.Send(new UnbanUserCommand(userId, User.GetUserId(), request.Reason), ct);

    public sealed record SanctionRequest(SanctionType Type, string Reason, int? DurationHours);

    /// <summary>
    /// Uyarı ya da SÜRELİ askı. Moderatöre açık: geri alınabilir ve cihaz banı uygulamaz —
    /// yaptırım ölçeğinin "hiçbir şey yapma / kalıcı ban" ikileminden çıkması için gerekli.
    /// </summary>
    [HttpPost("users/{userId:guid}/sanction")]
    public async Task<ApplySanctionResult> Sanction(
        Guid userId, SanctionRequest request, CancellationToken ct)
        => await _mediator.Send(new ApplySanctionCommand(
            userId, User.GetUserId(), request.Type, request.Reason, request.DurationHours), ct);

    // -----------------------------------------------------------------------
    // Şikayet kuyruğu (tek yönlü). İtiraz kuyruğunun yerini aldı.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Açık şikayetler. Yalnızca yönetim/moderasyon görür — şikayet edilen kişiye hiçbir
    /// uçtan sızmaz (kendi profilinde, derslerinde ya da bildirimlerinde karşılığı yoktur).
    /// </summary>
    [HttpGet("reports")]
    public async Task<IReadOnlyList<ReportListItemDto>> Reports(
        CancellationToken ct, [FromQuery] bool onlyOpen = true)
        => await _mediator.Send(new GetReportsQuery(onlyOpen), ct);

    public sealed record ResolveReportRequest(bool ActionTaken, string? Note);

    /// <summary>
    /// Şikayeti kapatır. <c>ActionTaken</c> yalnızca "yaptırım uygulandı mı" bilgisidir;
    /// yaptırımın kendisi ayrı uçtan (sanction/ban) verilir ve ayrı kaydedilir.
    /// </summary>
    [HttpPost("reports/{reportId:guid}/resolve")]
    public async Task<IActionResult> ResolveReport(
        Guid reportId, ResolveReportRequest request, CancellationToken ct)
    {
        await _mediator.Send(new ResolveReportCommand(
            reportId, User.GetUserId(), request.ActionTaken, request.Note), ct);
        return NoContent();
    }

    public sealed record CreditAdjustmentRequest(int Amount, string Reason);

    /// <summary>
    /// Yönetim eliyle puan tanımlama/düzeltme. Pozitif tutar ekler, negatif düşer.
    /// Gerekçe zorunludur ve denetim izine yazılır.
    /// </summary>
    /// <remarks>
    /// YALNIZCA ADMIN — moderatöre kapalı. Bu uç, ekonominin kullanıcı akışları dışındaki
    /// TEK yazma yolu: basımı ders onayı, yakımı vade süpürücüsü belirler, burası ise
    /// ikisini de atlar. Ban ile aynı sınıfta bir yetki: geri alınabilir olması (ters
    /// işaretli ikinci bir düzeltme) yetki gerekçesini zayıflatmıyor, çünkü asıl risk
    /// geri alınamazlık değil, defterin sessizce şişirilebilmesi.
    /// </remarks>
    /// <param name="idempotencyKey">
    /// ZORUNLU. Aynı anahtarla gelen ikinci istek düzeltmeyi TEKRAR UYGULAMAZ, ilkinin
    /// sonucunu döndürür (<c>replayed: true</c>). Farklı bir yükle tekrar kullanılırsa 409.
    ///
    /// Neden gövdede değil BAŞLIKTA: anahtar işlemin ne olduğuna dair bir bilgi değil,
    /// isteğin taşıma katmanına ait kimliği. Gövdeye konsaydı komutun alanlarından biri
    /// gibi görünür ve — daha önemlisi — yükün parçası olduğu için "yük aynı mı"
    /// karşılaştırmasına kendisi de girerdi.
    /// </param>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPost("users/{userId:guid}/credits")]
    public async Task<AdjustCreditsResult> AdjustCredits(
        Guid userId,
        CreditAdjustmentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
        => await _mediator.Send(new AdjustCreditsCommand(
            userId, User.GetUserId(), request.Amount, request.Reason, idempotencyKey ?? string.Empty), ct);

    public sealed record RoleRequest(UserRole Role);

    /// <summary>
    /// Rol atama. Yalnızca Admin: moderatör kendine ya da başkasına yetki dağıtabilseydi
    /// rol ayrımı anlamını yitirirdi.
    /// </summary>
    [Authorize(Policy = Policies.AdminOnly)]
    [HttpPut("users/{userId:guid}/role")]
    public async Task<IActionResult> ChangeRole(Guid userId, RoleRequest request, CancellationToken ct)
    {
        await _mediator.Send(new ChangeUserRoleCommand(userId, User.GetUserId(), request.Role), ct);
        return NoContent();
    }
}
