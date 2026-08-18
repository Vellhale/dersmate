using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Features.Community;
using PeerLearn.Domain.Community;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Api.Controllers;

/// <summary>Samimi profil, rozetler ve ders değerlendirmeleri.</summary>
[ApiController]
[Authorize]
[Route("api")]
public sealed class ProfileController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProfileController(IMediator mediator) => _mediator = mediator;

    public sealed record UpdateProfileRequest(string DisplayName, string? Bio, string? University, string? Department);

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest request, CancellationToken ct)
    {
        await _mediator.Send(new UpdateProfileCommand(
            User.GetUserId(), request.DisplayName, request.Bio, request.University, request.Department), ct);

        return NoContent();
    }

    /// <summary>
    /// Profil fotoğrafı. İstemci canvas ile kırpıp küçülterek gönderir; sunucu boyut,
    /// tür ve İÇERİK İMZASINI yeniden doğrular (istemciye güvenilmez).
    /// </summary>
    [HttpPost("profile/avatar")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<ActionResult<string>> UploadAvatar(IFormFile avatar, CancellationToken ct)
    {
        if (avatar is null || avatar.Length == 0)
        {
            return BadRequest(new { code = "VALIDATION_FAILED", detail = "Dosya seçilmedi." });
        }

        await using var stream = avatar.OpenReadStream();
        var key = await _mediator.Send(
            new UpdateAvatarCommand(User.GetUserId(), stream, avatar.ContentType, avatar.Length), ct);

        return Ok(key);
    }

    /// <summary>
    /// Profil fotoğrafını servis eder. Depo anahtarı doğrudan istemciye verilmez; görsel
    /// bu uçtan okunur ki depo düzeni (yol, sağlayıcı) dışarıya sızmasın.
    /// </summary>
    [HttpGet("users/{userId:guid}/avatar")]
    public async Task<IActionResult> GetAvatar(Guid userId, [FromServices] IAppDbContext db,
        [FromServices] IProofStorage storage, CancellationToken ct)
    {
        var key = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.AvatarUrl)
            .SingleOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(key))
        {
            return NotFound();
        }

        var content = await storage.OpenAsync(key, ct);
        if (content is null)
        {
            // Depo ile DB tutarsız: 500 değil 404 — çağıran için sonuç aynı (görsel yok).
            return NotFound();
        }

        var contentType = Path.GetExtension(key).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };

        return File(content, contentType);
    }

    // PUT profile/featured-badges KALDIRILDI — rozet vitrini emekli edildi
    // (bkz. ProfileCommands.cs'teki not).

    public sealed record TeacherCandidateRequest(
        string University, string Faculty, string Department, int? GradeYear, bool HasPedagogicalCertificate);

    /// <summary>Öğretmen adaylığı beyanı. Gönüllü ders ilanı açmanın ön koşulu.</summary>
    [HttpPut("profile/teacher-candidate")]
    public async Task<IActionResult> DeclareTeacherCandidate(TeacherCandidateRequest request, CancellationToken ct)
    {
        await _mediator.Send(new DeclareTeacherCandidateCommand(
            User.GetUserId(), request.University, request.Faculty, request.Department,
            request.GradeYear, request.HasPedagogicalCertificate), ct);

        return NoContent();
    }

    /// <summary>Herkese açık profil kartı (giriş yapmış kullanıcılar için).</summary>
    [HttpGet("users/{userId:guid}/profile")]
    public async Task<UserProfileDto> GetProfile(Guid userId, CancellationToken ct)
        => await _mediator.Send(new GetUserProfileQuery(userId, User.GetUserId()), ct);

    /// <summary>
    /// Branş rozetleri + branş bazlı anlatım saatleri. Profil ekranındaki rozet şeridi.
    /// </summary>
    /// <remarks>
    /// PROFİL UCUNDAN AYRI TUTULDU. Rozet listesi, profil kartından bağımsız olarak ve
    /// daha seyrek değişir; birleştirilseydi her profil açılışı iki ek sorgu (rozetler +
    /// süre toplamı) ödemek zorunda kalırdı. Ayrı uç, istemcinin rozet şeridini gecikmeli
    /// (lazy) yüklemesine de izin veriyor.
    /// </remarks>
    [HttpGet("users/{userId:guid}/subject-badges")]
    public async Task<SubjectBadgesDto> GetSubjectBadges(Guid userId, CancellationToken ct)
        => await _mediator.Send(new GetSubjectBadgesQuery(userId), ct);

    [HttpGet("users/{userId:guid}/reviews")]
    public async Task<TeacherReviewsDto> GetReviews(
        Guid userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
        => await _mediator.Send(new GetTeacherReviewsQuery(userId, page, pageSize), ct);

    public sealed record ReviewRequest(
        int Score,
        int TeachingScore,
        int PunctualityScore,
        IReadOnlyList<ReviewTag>? Tags,
        string? Comment);

    /// <summary>
    /// Ders değerlendirmesi. Yalnızca tamamlanmış dersin öğrencisi, ders başına bir kez —
    /// kurallar handler'da, istemciden bağımsız olarak uygulanır.
    /// </summary>
    [HttpPost("sessions/{sessionId:guid}/review")]
    public async Task<CreateReviewResult> CreateReview(
        Guid sessionId, ReviewRequest request, CancellationToken ct)
        => await _mediator.Send(new CreateReviewCommand(
            sessionId,
            User.GetUserId(),
            request.Score,
            request.TeachingScore,
            request.PunctualityScore,
            request.Tags ?? [],
            request.Comment), ct);
}
