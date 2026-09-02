using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PeerLearn.Api.Startup;
using PeerLearn.Application.Features.Identity;

namespace PeerLearn.Api.Controllers;

/// <summary>
/// Kimlik uçları. Sınıf düzeyinde SIKI hız sınırı: parola denemesi, hesap sayımı ve
/// doğrulama e-postası bombardımanı hep bu üç ucu hedefler.
/// </summary>
[ApiController]
[EnableRateLimiting(RateLimiting.AuthPolicy)]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator) => _mediator = mediator;

    /// <param name="TermsVersion">
    /// Arayüzün gösterdiği sözleşme sürümü. Sunucu bunu KAYDETMİYOR, yalnızca
    /// yürürlüktekiyle karşılaştırıyor (bkz. Domain/Identity/LegalDocuments.cs).
    /// </param>
    public sealed record RegisterRequest(
        string Email,
        string Password,
        string DisplayName,
        string? TermsVersion,
        bool AgeConfirmed);

    [HttpPost("register")]
    public async Task<RegisterResult> Register(RegisterRequest request, CancellationToken ct)
        => await _mediator.Send(new RegisterCommand(
            request.Email, request.Password, request.DisplayName,
            request.TermsVersion, request.AgeConfirmed), ct);

    public sealed record VerifyEmailRequest(string Email, string Code);

    /// <summary>
    /// E-postayı doğrular. Bağlantı yerine 6 haneli KOD (2026-09-02).
    /// </summary>
    /// <remarks>
    /// E-POSTA DA İSTENİYOR, kod tek başına değil: altı hane kullanıcıya özgü değil ve
    /// aynı anda yüzlerce hesapta aynı kod olabilir. Kod tek başına kabul edilseydi,
    /// rastgele kod deneyen biri er ya da geç BİRİNİN hesabını doğrulardı — kimin
    /// olduğunu bilmeden ama yine de doğrulamış olurdu.
    /// </remarks>
    [HttpPost("verify-email")]
    public async Task<VerifyEmailResult> VerifyEmail(VerifyEmailRequest request, CancellationToken ct)
        => await _mediator.Send(new VerifyEmailCommand(request.Email, request.Code), ct);

    public sealed record ResendVerificationRequest(string Email);

    /// <summary>
    /// Doğrulama bağlantısını yeniden gönderir. Token'ın ömrü 24 saat; süresi dolan
    /// kullanıcının başka çıkış yolu yok (giriş kapalı, aynı e-postayla yeniden kayıt kapalı).
    ///
    /// Yanıt her durumda AYNI: e-posta kayıtlı olsun olmasın 204. Aksi halde uç, bir
    /// e-postanın bu platformda kayıtlı olup olmadığını herkese söylerdi.
    /// </summary>
    [HttpPost("resend-verification")]
    public async Task<ResendVerificationResult> ResendVerification(
        ResendVerificationRequest request, CancellationToken ct)
        => await _mediator.Send(new ResendVerificationCommand(request.Email), ct);

    public sealed record ForgotPasswordRequest(string Email);

    /// <summary>
    /// Parola sıfırlama bağlantısı ister.
    ///
    /// Yanıt HER DURUMDA 204: e-posta kayıtlı olsun olmasın, doğrulanmış olsun olmasın,
    /// banlı olsun olmasın. Farklı yanıt vermek bu ucu "bu e-posta kayıtlı mı" sorusunu
    /// herkese yanıtlayan bir araca çevirirdi (resend-verification ile aynı karar).
    ///
    /// Sınıf düzeyindeki sıkı hız sınırı burada da geçerli: sıfırlama e-postası
    /// bombardımanı, kurbanın gelen kutusunu doldurmanın ucuz bir yolu.
    /// </summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        await _mediator.Send(new ForgotPasswordCommand(request.Email), ct);
        return NoContent();
    }

    public sealed record ResetPasswordRequest(string Token, string NewPassword);

    /// <summary>
    /// Bağlantıdaki token'la yeni parolayı yazar. Token tek kullanımlık ve 1 saat
    /// geçerli (bkz. ParolaSifirlama).
    /// </summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await _mediator.Send(new ResetPasswordCommand(request.Token, request.NewPassword), ct);
        return NoContent();
    }

    public sealed record LoginRequest(string Email, string Password, string? HwidHash);

    [HttpPost("login")]
    public async Task<LoginResult> Login(LoginRequest request, CancellationToken ct)
        => await _mediator.Send(new LoginCommand(request.Email, request.Password, request.HwidHash), ct);
}
