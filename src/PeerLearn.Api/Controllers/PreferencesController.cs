using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PeerLearn.Application.Features.Identity;

namespace PeerLearn.Api.Controllers;

/// <summary>Kullanıcı tercihleri: çerez rızası (Modül 3) ve ürün turu durumu (Modül 5).</summary>
[ApiController]
[Authorize]
[Route("api/preferences")]
public sealed class PreferencesController : ControllerBase
{
    private readonly IMediator _mediator;

    public PreferencesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<UserPreferencesDto> GetMine(CancellationToken ct)
        => await _mediator.Send(new GetMyPreferencesQuery(User.GetUserId()), ct);

    public sealed record CookieConsentRequest(bool Analytics, bool Functional, string ConsentVersion);

    /// <summary>
    /// Çerez rızasını kaydeder. Giriş yapmış kullanıcı için sunucu kaydı otoritedir;
    /// tarayıcıdaki kopya yalnızca anonim ziyaret ve anlık okuma içindir.
    /// </summary>
    [HttpPut("cookie-consent")]
    public async Task<IActionResult> UpdateCookieConsent(CookieConsentRequest request, CancellationToken ct)
    {
        await _mediator.Send(new UpdateCookieConsentCommand(
            User.GetUserId(),
            request.Analytics,
            request.Functional,
            request.ConsentVersion,
            HashClientIp()), ct);

        return NoContent();
    }

    public sealed record OnboardingRequest(int LastStep, bool Completed, bool Suppressed);

    /// <summary>
    /// Ürün turu ilerlemesi (Modül 5). Sunucuda tutulur, çerezde DEĞİL: kullanıcı zaten
    /// giriş yapmış durumda ve tercih cihaz değişince de korunmalı.
    /// </summary>
    [HttpPut("onboarding")]
    public async Task<IActionResult> UpdateOnboarding(OnboardingRequest request, CancellationToken ct)
    {
        await _mediator.Send(new UpdateOnboardingCommand(
            User.GetUserId(), request.LastStep, request.Completed, request.Suppressed), ct);

        return NoContent();
    }

    /// <summary>
    /// Rızanın verildiği andaki IP'nin SHA-256'sı. Ham IP SAKLANMAZ: kanıt için "aynı
    /// ağdan mı geldi" karşılaştırması yeterlidir, ham adres ise başlı başına kişisel
    /// veridir ve veri minimizasyonu ilkesine aykırı olurdu.
    ///
    /// Not: hash tuzsuzdur. IPv4 uzayı kaba kuvvetle taranabilir olduğundan bu, adresi
    /// geri döndürülemez kılmaz — amaç anonimleştirme değil, ham adresi loglarda ve
    /// yedeklerde açıkta tutmamaktır. Gerçek anonimleştirme gerekirse sunucuda tutulan
    /// bir gizli tuzla HMAC'e geçilmeli.
    /// </summary>
    private string? HashClientIp()
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrEmpty(ip))
        {
            return null;
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ip))).ToLowerInvariant();
    }
}
