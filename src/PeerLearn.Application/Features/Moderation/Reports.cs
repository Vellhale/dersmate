using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Moderation;

namespace PeerLearn.Application.Features.Moderation;

// ---------------------------------------------------------------------------
// 1) Şikayet oluşturma (herhangi bir kullanıcı)
// ---------------------------------------------------------------------------

/// <summary>
/// Tek yönlü şikayet açar. Şikayet edilen kişiye HİÇBİR ŞEY bildirilmez.
/// </summary>
/// <remarks>
/// DERSE DOKUNULMAZ. Eski itiraz akışı dersi <c>Disputed</c> yapıp puan basımını
/// dondururdu; şikayet bunu yapmaz. Gerekçe: şikayet edilen taraf ne şikayeti görüyor ne de
/// yanıt verebiliyor — tek taraflı bir beyanla puanını dondurmak, savunma hakkı olmayan
/// birini cezalandırmak olurdu. Yaptırım KİŞİYE uygulanır (uyarı/askı/ban), derse değil.
/// </remarks>
public sealed record CreateReportCommand(
    Guid ReporterUserId,
    Guid? SessionId,
    Guid? ReportedUserId,
    ReportReason Reason,
    string Description) : IRequest<Guid>;

public sealed class CreateReportHandler : IRequestHandler<CreateReportCommand, Guid>
{
    private const int MinDescriptionLength = 15;

    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public CreateReportHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Guid> Handle(CreateReportCommand request, CancellationToken ct)
    {
        var aciklama = request.Description?.Trim();
        if (string.IsNullOrEmpty(aciklama) || aciklama.Length < MinDescriptionLength)
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                $"Ne olduğunu kısaca anlat (en az {MinDescriptionLength} karakter). " +
                "Yönetim yalnızca senin anlattığını görecek.");
        }

        Guid sikayetEdilen;

        if (request.SessionId is { } sessionId)
        {
            /*
              Ders üzerinden şikayet: şikayet edilen taraf DERSTEN türetilir, istemciden
              alınmaz. İstemciye bırakılsaydı biri, hiç ilgisi olmayan bir kullanıcıyı
              kendi dersine iliştirip şikayet edebilirdi.
            */
            var ders = await _db.LessonSessions.AsNoTracking()
                           .Where(s => s.Id == sessionId)
                           .Select(s => new { s.Id, s.TutorUserId, s.StudentUserId })
                           .SingleOrDefaultAsync(ct)
                       ?? throw new AppException(ErrorCodes.SessionNotFound, "Ders bulunamadı.", statusCode: 404);

            if (ders.TutorUserId != request.ReporterUserId && ders.StudentUserId != request.ReporterUserId)
            {
                throw new AppException(ErrorCodes.NotSessionParticipant,
                    "Bu dersin tarafı değilsin.", statusCode: 403);
            }

            sikayetEdilen = ders.TutorUserId == request.ReporterUserId
                ? ders.StudentUserId
                : ders.TutorUserId;

            var zatenVar = await _db.Reports.AsNoTracking()
                .AnyAsync(x => x.ReporterUserId == request.ReporterUserId && x.SessionId == sessionId, ct);

            if (zatenVar)
            {
                throw new AppException(ErrorCodes.ReportAlreadyExists,
                    "Bu ders için zaten bir şikayet oluşturdun. Yönetim inceliyor.", statusCode: 409);
            }
        }
        else
        {
            // Ders dışı şikayet (sohbet, profil). Hedef zorunlu.
            sikayetEdilen = request.ReportedUserId
                ?? throw new AppException(ErrorCodes.ValidationFailed, "Şikayet edilen kullanıcı belirtilmedi.");

            var varMi = await _db.Users.AsNoTracking().AnyAsync(u => u.Id == sikayetEdilen, ct);
            if (!varMi) throw new AppException(ErrorCodes.UserNotFound, "Kullanıcı bulunamadı.", statusCode: 404);
        }

        if (sikayetEdilen == request.ReporterUserId)
        {
            throw new AppException(ErrorCodes.ValidationFailed, "Kendini şikayet edemezsin.");
        }

        var sikayet = new Report
        {
            ReporterUserId = request.ReporterUserId,
            ReportedUserId = sikayetEdilen,
            SessionId = request.SessionId,
            Reason = request.Reason,
            Description = aciklama,
            Status = ReportStatus.Open,
            CreatedAtUtc = _clock.UtcNow
        };

        _db.Reports.Add(sikayet);
        await _db.SaveChangesAsync(ct);

        return sikayet.Id;
    }
}

// ---------------------------------------------------------------------------
// 2) Yönetim kuyruğu
// ---------------------------------------------------------------------------

public sealed record GetReportsQuery(bool OnlyOpen = true) : IRequest<IReadOnlyList<ReportListItemDto>>;

public sealed record ReportListItemDto(
    Guid ReportId,
    string Reason,
    string Status,
    string Description,
    DateTime CreatedAtUtc,
    Guid ReporterUserId,
    string ReporterDisplayName,
    Guid ReportedUserId,
    string ReportedDisplayName,

    /// <summary>Şikayet edilen hakkındaki TOPLAM şikayet sayısı — yaptırım kararının ağırlığı.</summary>
    int ReportedUserTotalReports,

    Guid? SessionId,
    string? TopicName);

public sealed class GetReportsHandler : IRequestHandler<GetReportsQuery, IReadOnlyList<ReportListItemDto>>
{
    private readonly IAppDbContext _db;

    public GetReportsHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ReportListItemDto>> Handle(GetReportsQuery request, CancellationToken ct)
    {
        var sorgu = _db.Reports.AsNoTracking();
        if (request.OnlyOpen)
        {
            sorgu = sorgu.Where(x => x.Status == ReportStatus.Open);
        }

        var ham = await sorgu
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.Reason,
                x.Status,
                x.Description,
                x.CreatedAtUtc,
                x.ReporterUserId,
                x.ReportedUserId,
                x.SessionId,
                ReporterAd = _db.Users.Where(u => u.Id == x.ReporterUserId).Select(u => u.DisplayName).First(),
                ReportedAd = _db.Users.Where(u => u.Id == x.ReportedUserId).Select(u => u.DisplayName).First(),
                Toplam = _db.Reports.Count(r => r.ReportedUserId == x.ReportedUserId),
                Konu = _db.LessonSessions
                    .Where(s => s.Id == x.SessionId)
                    .Select(s => _db.Topics.Where(t => t.Id == s.TopicId).Select(t => t.Name).First())
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return ham.Select(x => new ReportListItemDto(
            x.Id,
            x.Reason.ToString(),
            x.Status.ToString(),
            x.Description,
            x.CreatedAtUtc,
            x.ReporterUserId,
            x.ReporterAd,
            x.ReportedUserId,
            x.ReportedAd,
            x.Toplam,
            x.SessionId,
            x.Konu)).ToList();
    }
}

// ---------------------------------------------------------------------------
// 3) Şikayeti kapatma
// ---------------------------------------------------------------------------

public sealed record ResolveReportCommand(
    Guid ReportId, Guid AdminUserId, bool ActionTaken, string? Note) : IRequest;

public sealed class ResolveReportHandler : IRequestHandler<ResolveReportCommand>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public ResolveReportHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task Handle(ResolveReportCommand request, CancellationToken ct)
    {
        var sikayet = await _db.Reports.SingleOrDefaultAsync(x => x.Id == request.ReportId, ct)
            ?? throw new AppException(ErrorCodes.ReportNotFound, "Şikayet bulunamadı.", statusCode: 404);

        if (sikayet.Status != ReportStatus.Open)
        {
            throw new AppException(ErrorCodes.InvalidSessionState,
                "Bu şikayet zaten kapatılmış.", statusCode: 409);
        }

        var actorRole = await _db.Users.AsNoTracking()
            .Where(u => u.Id == request.AdminUserId).Select(u => u.Role).SingleAsync(ct);

        sikayet.Status = request.ActionTaken ? ReportStatus.ActionTaken : ReportStatus.Dismissed;
        sikayet.ReviewedByAdminId = request.AdminUserId;
        sikayet.ReviewedAtUtc = _clock.UtcNow;
        sikayet.AdminNote = request.Note?.Trim();

        /*
          Denetim izi ŞİKAYET EDİLENE değil ŞİKAYETE bağlanıyor (TargetType="Report").
          Hedef kullanıcı yazılsaydı, o kullanıcının denetim geçmişinde "hakkında karar
          verildi" satırları birikir ve yaptırım uygulanmamış şikayetler bile onu suçlu
          gösterirdi. Uygulanan yaptırımın kendi kaydı zaten ayrıca yazılıyor.
        */
        AdminAudit.Record(
            _db, request.AdminUserId, actorRole,
            AdminActionType.ReportReviewed,
            targetType: "Report",
            targetId: sikayet.Id,
            summary: request.ActionTaken ? "Şikayet: yaptırım uygulandı" : "Şikayet: işlem gerekmedi",
            metadata: new { actionTaken = request.ActionTaken, note = sikayet.AdminNote });

        await _db.SaveChangesAsync(ct);
    }
}
