using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Features.Community;
using PeerLearn.Domain.Community;
using PeerLearn.Domain.Identity;
using PeerLearn.Domain.Matchmaking;
using PeerLearn.Domain.Moderation;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Application.Features.Moderation;

// ---------------------------------------------------------------------------
// 1) Kuyruk — öğretmen adaylığı beyanları
// ---------------------------------------------------------------------------

public enum TeacherCandidateFilter
{
    /// <summary>Karar bekleyenler (varsayılan kuyruk görünümü).</summary>
    Pending = 0,
    Verified = 1,
    Rejected = 2,
    All = 3
}

public sealed record GetTeacherCandidatesQuery(
    TeacherCandidateFilter Filter = TeacherCandidateFilter.Pending,
    int Page = 1,
    int PageSize = 25) : IRequest<PagedResult<TeacherCandidateRowDto>>;

public sealed record TeacherCandidateRowDto(
    Guid ProfileId,
    Guid UserId,
    string DisplayName,
    string Email,
    string UserStatus,
    DateTime JoinedAtUtc,

    // Beyan
    string University,
    string Faculty,
    string Department,
    int? GradeYear,
    bool HasPedagogicalCertificate,
    DateTime DeclaredAtUtc,

    // Karar
    string ReviewStatus,
    DateTime? ReviewedAtUtc,
    Guid? ReviewedByAdminId,
    string? ReviewedByDisplayName,
    string? ReviewNote,

    /// <summary>Kullanıcının profilinde yazan (serbest) okul bilgisi — beyanla tutarlı mı?</summary>
    string? ProfileUniversity,
    string? ProfileDepartment,

    /// <summary>Açık gönüllü ilan sayısı: beyanın fiilen kullanılıp kullanılmadığını gösterir.</summary>
    int VolunteerOfferCount,

    /// <summary>Tamamlanmış gönüllü ders sayısı — kararın en güçlü davranışsal sinyali.</summary>
    int CompletedVolunteerSessions,

    decimal AverageRating,
    int RatingCount,

    /// <summary>
    /// Aday öğrenci belgesi yükledi mi. Belge yükleme kanalı 2026-09-01'de açıldı; bu
    /// alan olmadan panel belgenin varlığını bilemiyor ve görüntüleyiciyi hiç gösteremiyordu.
    /// Belgenin KENDİSİ ayrı uçtan okunuyor (GET admin/teacher-candidates/{id}/document) —
    /// listede taşımak, her satırda dosya çekmek olurdu.
    /// </summary>
    bool HasDocument);

public sealed class GetTeacherCandidatesHandler
    : IRequestHandler<GetTeacherCandidatesQuery, PagedResult<TeacherCandidateRowDto>>
{
    private readonly IAppDbContext _db;

    public GetTeacherCandidatesHandler(IAppDbContext db) => _db = db;

    public async Task<PagedResult<TeacherCandidateRowDto>> Handle(
        GetTeacherCandidatesQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var taban = _db.TeacherCandidateProfiles.AsNoTracking();

        taban = request.Filter switch
        {
            TeacherCandidateFilter.Pending => taban.Where(p => p.VerifiedAtUtc == null && p.RejectedAtUtc == null),
            TeacherCandidateFilter.Verified => taban.Where(p => p.VerifiedAtUtc != null),
            TeacherCandidateFilter.Rejected => taban.Where(p => p.RejectedAtUtc != null),
            _ => taban
        };

        var total = await taban.CountAsync(ct);
        if (total == 0)
        {
            return PagedResult<TeacherCandidateRowDto>.Empty(page, pageSize);
        }

        /*
          Gönüllü ilan ve tamamlanmış gönüllü ders sayıları KORELE ALT SORGU olarak
          alınıyor — tek SQL, tek gidiş-dönüş. Sayfa başına 25 satır için bu, iki ek
          sorgu açıp bellekte eşlemekten hem daha kısa hem de yeterince ucuz; N+1
          DEĞİL (satır başına ayrı istek yok).
        */
        var birlesik =
            from p in taban
            join u in _db.Users.AsNoTracking() on p.UserId equals u.Id
            select new
            {
                Beyan = p,
                u.DisplayName,
                u.Email,
                u.Status,
                u.CreatedAtUtc,
                u.University,
                u.Department,
                u.AverageRating,
                u.RatingCount,
                VolunteerOffers = _db.PortfolioEntries.Count(e =>
                    e.UserId == p.UserId
                    && e.IsActive
                    && e.IsVolunteer
                    && e.Direction == PortfolioDirection.Offer),
                VolunteerSessions = _db.LessonSessions.Count(s =>
                    s.TutorUserId == p.UserId
                    && s.IsVolunteer
                    && s.Status == SessionStatus.Completed)
            };

        /*
          Sıralama JOIN'DEN SONRA uygulanıyor: EF'te sıralanmış bir kaynağı join'lemek
          ORDER BY'ı koruma garantisi vermez, sayfalama ise sıralamaya bağlıdır.

          Bekleyen kuyruğu en eskiden yeniye (adalet: sırada bekleyen önce), karar
          verilmişler en yeni karardan geriye — o liste bir kuyruk değil, "son ne yapıldı"
          geçmişi.
        */
        var sirali = request.Filter == TeacherCandidateFilter.Pending
            ? birlesik.OrderBy(x => x.Beyan.DeclaredAtUtc).ThenBy(x => x.Beyan.Id)
            : birlesik
                .OrderByDescending(x => x.Beyan.VerifiedAtUtc ?? x.Beyan.RejectedAtUtc ?? x.Beyan.DeclaredAtUtc)
                .ThenBy(x => x.Beyan.Id);

        var sayfa = await sirali
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // Kararı veren moderatörün adı ayrı çekilir: karar iki ayrı kolonda (Verified/
        // Rejected) tutulduğu için tek bir LEFT JOIN'e indirgemek sorguyu okunmaz yapardı.
        var kararVerenler = sayfa
            .Select(x => x.Beyan.VerifiedByAdminId ?? x.Beyan.RejectedByAdminId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, string> isimler = kararVerenler.Count == 0
            ? []
            : await _db.Users.AsNoTracking()
                .Where(u => kararVerenler.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var rows = sayfa.Select(x =>
        {
            var kararVeren = x.Beyan.VerifiedByAdminId ?? x.Beyan.RejectedByAdminId;

            return new TeacherCandidateRowDto(
                x.Beyan.Id,
                x.Beyan.UserId,
                x.DisplayName,
                x.Email,
                x.Status.ToString(),
                x.CreatedAtUtc,
                x.Beyan.University,
                x.Beyan.Faculty,
                x.Beyan.Department,
                x.Beyan.GradeYear,
                x.Beyan.HasPedagogicalCertificate,
                x.Beyan.DeclaredAtUtc,
                x.Beyan.ReviewStatus.ToString(),
                x.Beyan.VerifiedAtUtc ?? x.Beyan.RejectedAtUtc,
                kararVeren,
                kararVeren is not null && isimler.TryGetValue(kararVeren.Value, out var ad) ? ad : null,
                x.Beyan.ReviewNote,
                x.University,
                x.Department,
                x.VolunteerOffers,
                x.VolunteerSessions,
                x.AverageRating,
                x.RatingCount,
                x.Beyan.DocumentStorageKey != null);
        }).ToList();

        return new PagedResult<TeacherCandidateRowDto>(rows, total, page, pageSize);
    }
}

// ---------------------------------------------------------------------------
// 2) Karar
// ---------------------------------------------------------------------------

public enum TeacherCandidateDecision
{
    /// <summary>Belge görüldü: beyan doğrulandı.</summary>
    Verify = 0,

    /// <summary>Beyan uygun bulunmadı. 🌱 rozeti geri alınır (aşağıdaki nota bakınız).</summary>
    Reject = 1,

    /// <summary>Önceki karar geri alınır; beyan yeniden kuyruğa döner.</summary>
    Revert = 2
}

public sealed record ReviewTeacherCandidateCommand(
    Guid ProfileId,
    Guid ModeratorUserId,
    TeacherCandidateDecision Decision,
    string Note) : IRequest<ReviewTeacherCandidateResult>;

public sealed record ReviewTeacherCandidateResult(
    Guid ProfileId,
    string ReviewStatus,
    bool BadgeRemoved,

    /// <summary>Karar geri alındığı (ya da ret sonrası doğrulandığı) için 🌱 rozeti iade edildi mi.</summary>
    bool BadgeRestored);

/// <remarks>
/// NEDEN NOT ZORUNLU (itiraz kararında opsiyonelken): itiraz dosyasında kanıt, iki tarafın
/// beyanı ve ders kaydı sistemin içindedir — karar sonradan yeniden okunabilir. Öğretmen
/// adaylığında ise sistemde HİÇBİR kanıt yoktur; doğrulama tamamen sistem dışı bir belgeye
/// dayanır. Not yazılmazsa "bu kişi neden doğrulandı?" sorusunun cevabı hiçbir yerde kalmaz.
///
/// NEDEN MODERATÖRE AÇIK: karar geri alınabilir (Revert) ve krediye dokunmaz. Ban'dan farkı
/// budur; AdminOnly'ye daraltmak kuyruğu gereksiz yere tıkardı.
/// </remarks>
public sealed class ReviewTeacherCandidateHandler
    : IRequestHandler<ReviewTeacherCandidateCommand, ReviewTeacherCandidateResult>
{
    private const int MinNoteLength = 5;
    private const int MaxNoteLength = 500;

    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly BadgeEngine _badges;

    public ReviewTeacherCandidateHandler(IAppDbContext db, IClock clock, BadgeEngine badges)
    {
        _db = db;
        _clock = clock;
        _badges = badges;
    }

    public async Task<ReviewTeacherCandidateResult> Handle(
        ReviewTeacherCandidateCommand request, CancellationToken ct)
    {
        var note = request.Note?.Trim();
        if (string.IsNullOrEmpty(note) || note.Length < MinNoteLength)
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                $"Karar gerekçesi zorunludur (en az {MinNoteLength} karakter).");
        }

        if (note.Length > MaxNoteLength)
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                $"Karar gerekçesi en fazla {MaxNoteLength} karakter olabilir.");
        }

        var profile = await _db.TeacherCandidateProfiles
                          .SingleOrDefaultAsync(p => p.Id == request.ProfileId, ct)
                      ?? throw new AppException(ErrorCodes.ValidationFailed,
                          "Öğretmen adaylığı beyanı bulunamadı.", statusCode: 404);

        var now = _clock.UtcNow;
        var badgeRemoved = false;

        // Karar, rozet değişikliği ve denetim izi tek atomik sınırda: "karar uygulandı ama
        // iz yok" ya da "rozet silindi ama karar yazılmadı" durumları mümkün olmamalı.
        await using var tx = await _db.BeginTransactionAsync(cancellationToken: ct);

        switch (request.Decision)
        {
            case TeacherCandidateDecision.Verify:
                if (profile.IsVerified)
                {
                    throw new AppException(ErrorCodes.ValidationFailed,
                        "Bu beyan zaten doğrulanmış.", statusCode: 409);
                }

                profile.VerifiedAtUtc = now;
                profile.VerifiedByAdminId = request.ModeratorUserId;
                profile.RejectedAtUtc = null;
                profile.RejectedByAdminId = null;
                break;

            case TeacherCandidateDecision.Reject:
                if (profile.IsRejected)
                {
                    throw new AppException(ErrorCodes.ValidationFailed,
                        "Bu beyan zaten reddedilmiş.", statusCode: 409);
                }

                profile.RejectedAtUtc = now;
                profile.RejectedByAdminId = request.ModeratorUserId;
                profile.VerifiedAtUtc = null;
                profile.VerifiedByAdminId = null;
                badgeRemoved = await RemoveFutureTeacherBadgeAsync(profile.UserId, ct);
                break;

            case TeacherCandidateDecision.Revert:
                if (profile.IsPendingReview)
                {
                    throw new AppException(ErrorCodes.ValidationFailed,
                        "Bu beyan için verilmiş bir karar yok.", statusCode: 409);
                }

                profile.VerifiedAtUtc = null;
                profile.VerifiedByAdminId = null;
                profile.RejectedAtUtc = null;
                profile.RejectedByAdminId = null;
                break;

            default:
                throw new AppException(ErrorCodes.ValidationFailed, "Geçersiz karar.");
        }

        profile.ReviewNote = note;

        var durum = profile.ReviewStatus.ToString();

        var actorRole = await _db.Users.AsNoTracking()
            .Where(u => u.Id == request.ModeratorUserId)
            .Select(u => u.Role)
            .SingleAsync(ct);

        /*
          SIRA: karar ÖNCE yazılır, rozetler SONRA yeniden değerlendirilir — ikisi tek
          transaction'da.

          Neden değerlendirme şart: ret, 🌱 rozetinin SATIRINI siliyor. Karar geri alındığında
          (Revert) ya da ret sonrası doğrulandığında kullanıcı kurala göre rozeti yeniden hak
          ediyor, ama satırı geri koyacak başka hiçbir yol yok — beyanını yeniden gönderene
          kadar rozet kayıp kalıyordu. Yani "kararı geri al" kendi yan etkisini geri almıyordu.

          Neden SaveChanges'ten SONRA: BadgeEngine veriyi DB'den okur. Önce çağrılsaydı ret
          dalında RejectedAtUtc henüz yazılmamış olur ve motor, az önce sildiğim rozeti anında
          geri verirdi. (Aynı tuzağa bu projede iki kez düşüldü; bkz. CreateReview notu.)

          (Eskiden burada bir "vitrin slotu geri gelmez" sınırı yazılıydı; rozet vitrini
          emekli edildiği için o sınır ortadan kalktı — iade edilen satır artık tam olarak
          eskisi kadar iyi.)
        */
        await _db.SaveChangesAsync(ct);

        var yenidenVerilen = await _badges.EvaluateAsync(profile.UserId, ct);
        var badgeRestored = yenidenVerilen.Contains(BadgeCode.FutureTeacher);

        AdminAudit.Record(
            _db,
            request.ModeratorUserId,
            actorRole,
            request.Decision switch
            {
                TeacherCandidateDecision.Verify => AdminActionType.TeacherCandidateVerified,
                TeacherCandidateDecision.Reject => AdminActionType.TeacherCandidateRejected,
                _ => AdminActionType.TeacherCandidateReviewReverted
            },
            targetType: "TeacherCandidateProfile",
            targetId: profile.Id,
            summary: request.Decision switch
            {
                TeacherCandidateDecision.Verify =>
                    $"Öğretmen adaylığı doğrulandı: {profile.University} · {profile.Department}.",
                TeacherCandidateDecision.Reject =>
                    $"Öğretmen adaylığı reddedildi: {profile.University} · {profile.Department}.",
                _ => "Öğretmen adaylığı kararı geri alındı; beyan yeniden incelemede."
            },
            metadata: new
            {
                userId = profile.UserId,
                decision = request.Decision.ToString(),
                statusAfter = durum,
                university = profile.University,
                faculty = profile.Faculty,
                department = profile.Department,
                badgeRemoved,
                badgeRestored,
                note
            });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new ReviewTeacherCandidateResult(profile.Id, durum, badgeRemoved, badgeRestored);
    }

    /// <summary>
    /// 🌱 rozeti reddedilen beyandan geri alınır — "rozet geri alınmaz" kuralının BİLİNÇLİ
    /// tek istisnası.
    ///
    /// Gerekçe: diğer rozetler bir BAŞARIYI ödüllendirir (ilk ders, 10 saat, yüksek puan);
    /// hak edildikten sonra veri değişse bile o gün gerçekten yapılmıştır. Bu rozet ise
    /// kişi hakkında bir OLGU iddiasıdır ("eğitim fakültesi öğrencisi"). Moderasyon bu
    /// iddiayı asılsız bulduysa rozeti bırakmak, platformun yanlış olduğunu bildiği bir
    /// bilgiyi yayınlamaya devam etmesi demektir. Puanı düşen kullanıcıdan rozet almakla
    /// aynı şey değildir.
    ///
    /// BadgeEngine de reddedilmiş beyana rozet vermez; aksi halde sonraki değerlendirmede
    /// geri gelirdi.
    /// </summary>
    private async Task<bool> RemoveFutureTeacherBadgeAsync(Guid userId, CancellationToken ct)
    {
        var rozet = await _db.UserBadges
            .Join(_db.Badges, ub => ub.BadgeId, b => b.Id, (ub, b) => new { ub, b.Code })
            .Where(x => x.ub.UserId == userId && x.Code == BadgeCode.FutureTeacher)
            .Select(x => x.ub)
            .SingleOrDefaultAsync(ct);

        if (rozet is null)
        {
            return false;
        }

        _db.UserBadges.Remove(rozet);
        return true;
    }
}
