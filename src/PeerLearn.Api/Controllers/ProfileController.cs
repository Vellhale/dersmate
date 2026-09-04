using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Features.Community;
using PeerLearn.Application.Features.Identity;
using PeerLearn.Application.Features.Moderation;
using PeerLearn.Domain.Moderation;
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

    public sealed record DeleteAccountRequest(string Password);

    /// <summary>
    /// Hesabı sil (Google Play'in hesap silme politikası + KVKK/GDPR silme hakkı).
    ///
    /// VERB NEDEN DELETE DEĞİL: parola gövdede taşınıyor ve gövdeli DELETE isteklerini
    /// bazı vekiller/istemciler kırpıyor — istek sunucuya parolasız ulaşıp 401'e düşerdi.
    /// Teşhisi zor, sebebi görünmez bir hata sınıfı; POST bu riski hiç doğurmuyor.
    ///
    /// Kişisel verinin ne olduğu ve neyin KALDIĞI DeleteAccountHandler'da yazılı.
    /// </summary>
    [HttpPost("profile/delete")]
    public async Task<IActionResult> DeleteAccount(
        DeleteAccountRequest request,
        [FromServices] IProofStorage storage,
        [FromServices] ILogger<ProfileController> logger,
        CancellationToken ct)
    {
        var sonuc = await _mediator.Send(new DeleteAccountCommand(User.GetUserId(), request.Password), ct);

        /*
          FOTOĞRAF COMMIT'TEN SONRA SİLİNİYOR ve hatası YUTULUYOR.

          Sıra bilinçli: önce satırdaki referans temizlenip kaydedildi, dosya ancak ondan
          sonra siliniyor. Tersi olsaydı — dosyayı silip sonra kayıt başarısız olsaydı —
          profil var olmayan bir dosyaya işaret ederdi (projedeki avatar güncelleme notu
          da aynı gerekçeyle eski dosyaya dokunmuyor).

          Silme başarısız olursa istek BAŞARISIZ SAYILMAZ: hesap zaten silindi ve kullanıcıya
          "silinemedi" demek yanlış olurdu. Dosya artık hiçbir satırdan referanslı olmadığı
          için depo bakım işi (CleanupStorage faz 2) onu artık dosya olarak topluyor.
        */
        if (!string.IsNullOrEmpty(sonuc.AvatarStorageKey))
        {
            try
            {
                await storage.DeleteAsync(sonuc.AvatarStorageKey, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Silinen hesabın profil fotoğrafı kaldırılamadı: {Key}. " +
                                    "Depo bakımı artık dosya olarak toplayacak.", sonuc.AvatarStorageKey);
            }
        }

        return NoContent();
    }

    /// <summary>
    /// Öğrenci belgesi (PDF/görsel, en fazla 10 MB). Yeni belge, önceki doğrulama/ret
    /// kararını sıfırlar ve beyanı yeniden kuyruğa sokar.
    ///
    /// ⚠️ BU UÇ AÇILDI ama özellik HENÜZ TAM DEĞİL: yönetim ekranı belgeyi gösteren bir
    /// görüntüleyici taşımıyor ve operatöre hâlâ "sistemde belge yükleme kanalı yok"
    /// diyor. Belge yüklenebilir ve GET ile okunabilir; hakem arayüzü bağlanana kadar
    /// doğrulama pratikte sistem dışı kanıta dayanmaya devam eder.
    ///
    /// Boyut/tür doğrulaması ve karar sıfırlama UploadTeacherDocumentHandler'da.
    /// Controller sınırı handler'ın 10 MB'ının bir tık üstünde: aşan istek, gövde
    /// okunmadan 413 yerine handler'ın anlaşılır hatasına düşsün.
    /// </summary>
    [HttpPost("profile/teacher-candidate/document")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> UploadTeacherDocument(IFormFile document, CancellationToken ct)
    {
        if (document is null || document.Length == 0)
        {
            return BadRequest(new { code = "VALIDATION_FAILED", detail = "Belge seçilmedi." });
        }

        await using var stream = document.OpenReadStream();
        await _mediator.Send(new UploadTeacherDocumentCommand(
            User.GetUserId(), stream, document.ContentType, document.Length), ct);

        return NoContent();
    }

    /// <summary>
    /// Kendi yüklediğin öğrenci belgesini geri okur. Yetki kontrolü
    /// GetTeacherDocumentHandler'da: moderatör değilsen yalnızca KENDİ beyanının
    /// belgesine erişebilirsin (aksi 403).
    /// </summary>
    [HttpGet("profile/teacher-candidate/{profileId:guid}/document")]
    public async Task<IActionResult> GetTeacherDocument(Guid profileId, CancellationToken ct)
    {
        var belge = await _mediator.Send(
            new GetTeacherDocumentQuery(profileId, User.GetUserId(), AsModerator: false), ct);

        return File(belge.Content, belge.ContentType);
    }

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

    public sealed record ReportUserRequest(ReportReason Reason, string Description);

    /// <summary>
    /// KULLANICI ŞİKAYETİ — ders bağlamı OLMADAN.
    ///
    /// NEDEN EKLENDİ (2026-08-27): şikayet açmanın tek yolu bir DERS üzerindendi
    /// (<c>POST api/sessions/{id}/report</c>). Yani sohbette taciz eden, uygunsuz içerik
    /// gönderen ya da kişisel bilgi isteyen biri, henüz o kişiyle tamamlanmış bir dersi
    /// yoksa <b>hiçbir şekilde bildirilemiyordu</b>. Öğrencilerin eşleşip birebir
    /// yazıştığı bir üründe en olası taciz anı tam olarak orası — ders öncesi sohbet.
    ///
    /// Handler bu dalı zaten destekliyordu (CreateReportHandler, SessionId null yolu);
    /// eksik olan yalnızca HTTP kapısıydı. Aynı handler'ı kullanmak, iki şikayet türünün
    /// aynı kuyruğa ve aynı denetim izine düşmesini de garanti ediyor.
    ///
    /// Mükerrerlik kapısı handler'da: aynı kişi hakkında AÇIK bir şikayetin varken
    /// ikincisi 409 döner. Kapanmış şikayetten sonra yeni olay bildirilebilir.
    /// </summary>
    [HttpPost("users/{userId:guid}/report")]
    public async Task<ActionResult<Guid>> ReportUser(
        Guid userId, ReportUserRequest request, CancellationToken ct)
        => Ok(await _mediator.Send(new CreateReportCommand(
            User.GetUserId(), null, userId, request.Reason, request.Description), ct));
}
