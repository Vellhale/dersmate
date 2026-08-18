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

    public sealed record RegisterRequest(string Email, string Password, string DisplayName);

    [HttpPost("register")]
    public async Task<RegisterResult> Register(RegisterRequest request, CancellationToken ct)
        => await _mediator.Send(new RegisterCommand(request.Email, request.Password, request.DisplayName), ct);

    public sealed record VerifyEmailRequest(string Token);

    [HttpPost("verify-email")]
    public async Task<VerifyEmailResult> VerifyEmail(VerifyEmailRequest request, CancellationToken ct)
        => await _mediator.Send(new VerifyEmailCommand(request.Token), ct);

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

    public sealed record LoginRequest(string Email, string Password, string? HwidHash);

    [HttpPost("login")]
    public async Task<LoginResult> Login(LoginRequest request, CancellationToken ct)
        => await _mediator.Send(new LoginCommand(request.Email, request.Password, request.HwidHash), ct);
}
