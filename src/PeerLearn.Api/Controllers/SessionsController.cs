using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeerLearn.Application.Common;
using PeerLearn.Application.Features.Moderation;
using PeerLearn.Application.Features.Scheduling;
using PeerLearn.Domain.Moderation;

namespace PeerLearn.Api.Controllers;

/// <summary>Ders yaşam döngüsü: rezervasyon → (time-lock) tamamlama+kanıt → onay/itiraz.</summary>
[ApiController]
[Authorize]
[Route("api/sessions")]
public sealed class SessionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SessionsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Derslerim (eğitmen + öğrenci rolleri), aksiyon bayraklarıyla birlikte.
    /// Aktif dersler her zaman tam döner; yalnızca geçmiş sayfalanır.
    /// </summary>
    [HttpGet]
    public async Task<MySessionsDto> GetMine(
        CancellationToken ct, [FromQuery] int pastPage = 1, [FromQuery] int pastPageSize = 20)
        => await _mediator.Send(new GetMySessionsQuery(User.GetUserId(), pastPage, pastPageSize), ct);

    public sealed record BookRequest(Guid MatchId, Guid TopicId, DateTime ScheduledStartUtc, int DurationMinutes);

    /// <summary>Rezervasyon: kredi escrow'a alınır, benzersiz doğrulama kodu üretilir.</summary>
    [HttpPost]
    public async Task<BookSessionResult> Book(BookRequest request, CancellationToken ct)
        => await _mediator.Send(new BookSessionCommand(
            User.GetUserId(), request.MatchId, request.TopicId,
            request.ScheduledStartUtc, request.DurationMinutes), ct);

    /// <summary>
    /// "Dersi Tamamladım" (eğitmen): TIME-LOCK + doğrulama kodu + ekran görüntüsü kanıtı.
    /// multipart/form-data: proof (dosya), verificationCode (metin).
    /// </summary>
    [HttpPost("{sessionId:guid}/complete")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<CompleteSessionResult> Complete(
        Guid sessionId,
        [FromForm] string verificationCode,
        IFormFile proof,
        CancellationToken ct)
    {
        if (proof is null || proof.Length == 0)
        {
            throw new AppException(ErrorCodes.ProofInvalid, "Kanıt ekran görüntüsü zorunludur.");
        }

        await using var stream = proof.OpenReadStream();

        return await _mediator.Send(new CompleteSessionCommand(
            sessionId, User.GetUserId(), verificationCode,
            stream, proof.ContentType, proof.Length), ct);
    }

    /// <summary>
    /// Dersin kanıtları (her iki taraf görebilir). Öğrenci, onaylamadan önce eğitmenin
    /// yüklediği ekran görüntüsünü incelemek zorundadır — çift taraflı onayın anlamı budur.
    /// </summary>
    [HttpGet("{sessionId:guid}/proofs")]
    public async Task<IReadOnlyList<SessionProofDto>> GetProofs(Guid sessionId, CancellationToken ct)
        => await _mediator.Send(new GetSessionProofsQuery(sessionId, User.GetUserId()), ct);

    /// <summary>Kanıt görselinin kendisi (yalnızca dersin tarafları).</summary>
    [HttpGet("{sessionId:guid}/proofs/{proofId:guid}/content")]
    public async Task<IActionResult> GetProofContent(Guid sessionId, Guid proofId, CancellationToken ct)
    {
        var proof = await _mediator.Send(new GetProofContentQuery(sessionId, proofId, User.GetUserId()), ct);
        return File(proof.Content, proof.ContentType);
    }

    /// <summary>Çift taraflı onay (öğrenci): atomik kredi transferini tetikler.</summary>
    [HttpPost("{sessionId:guid}/approve")]
    public async Task<ApproveSessionResult> Approve(Guid sessionId, CancellationToken ct)
        => await _mediator.Send(new ApproveSessionCommand(sessionId, User.GetUserId()), ct);

    public sealed record CancelRequest(string? Reason);

    [HttpPost("{sessionId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid sessionId, CancelRequest request, CancellationToken ct)
    {
        await _mediator.Send(new CancelSessionCommand(sessionId, User.GetUserId(), request.Reason), ct);
        return NoContent();
    }

    public sealed record DisputeRequest(DisputeReason Reason, string Description);

    /// <summary>
    /// İtiraz (öğrenci): "ders yapılmadı" / "sahte kanıt". Ders Disputed'a geçer, kredi
    /// transferi DONAR ve konu yönetim hakemliğine düşer.
    ///
    /// GERİ EKLENDİ (bilerek). Aşağıdaki nottaki gibi bu uç şikayet modeline geçilirken
    /// kaldırılmıştı, ama kaldırma YARIM kalmış: yönetim tarafı olduğu gibi duruyor
    /// (AdminController → GetOpenDisputes + ResolveDispute), OpenDisputeHandler duruyor,
    /// iki istemci de Disputed durumunu çiziyor. Eksik olan tek şey oluşturma yoluydu ve
    /// sonucu şuydu: itiraz kuyruğu HİÇ dolmuyor, kanıt inceleme ekranı erişilemez kalıyor
    /// ve öğrencinin "sahte kanıt yüklendi" diyip kredi basımını durdurmasının bir yolu
    /// bulunmuyor. Şikayet bunun yerine geçemez — CreateReportCommand'ın kendi notu da
    /// "şikayet puan basımını DURDURMAZ" diyor.
    ///
    /// Yetki, durum ve süre kuralları SessionRules.EnsureCanDispute'ta; cüzdan kilidi ve
    /// transaction OpenDisputeHandler'da. Bu aksiyon yalnızca kapıyı açıyor.
    /// </summary>
    [HttpPost("{sessionId:guid}/dispute")]
    public async Task<ActionResult<Guid>> Dispute(Guid sessionId, DisputeRequest request, CancellationToken ct)
        => Ok(await _mediator.Send(new OpenDisputeCommand(
            sessionId, User.GetUserId(), request.Reason, request.Description), ct));

    /// <summary>İtiraz (öğrenci): "ders yapılmadı" / "sahte kanıt". Transfer donar, konu admine düşer.</summary>
    public sealed record ReportRequest(ReportReason Reason, string Description);

    /// <summary>
    /// Ders hakkında TEK YÖNLÜ şikayet. Şikayet edilen kişi bunu görmez ve yanıt veremez;
    /// kayıt doğrudan yönetim kuyruğuna düşer.
    /// </summary>
    /// <remarks>
    /// ESKİ UÇLAR KALDIRILDI: <c>POST {id}/dispute</c> iki taraflı itiraz açıyordu ve
    /// <c>POST {id}/dispute-response</c> eğitmene savunma hakkı veriyordu. Şikayet modelinde
    /// savunma diye bir aşama yok — o yüzden ikinci ucun karşılığı da yok.
    ///
    /// DERS ETKİLENMEZ: şikayet puan basımını durdurmaz (gerekçe CreateReportCommand'da).
    /// </remarks>
    [HttpPost("{sessionId:guid}/report")]
    public async Task<ActionResult<Guid>> Report(Guid sessionId, ReportRequest request, CancellationToken ct)
        => Ok(await _mediator.Send(new CreateReportCommand(
            User.GetUserId(), sessionId, null, request.Reason, request.Description), ct));
}
