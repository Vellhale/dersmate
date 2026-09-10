using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PeerLearn.Api.Startup;
using PeerLearn.Application.Features.Identity;

namespace PeerLearn.Api.Controllers;

/// <summary>
/// Oturum yenileme. <see cref="AuthController"/>'DAN AYRI BİR SINIF — ve bu, kozmetik
/// bir düzen tercihi değil, bir arızayı önleyen tasarım kararı.
/// </summary>
/// <remarks>
/// ⛔ NEDEN AuthController'A KONULMADI.
///
/// AuthController sınıf düzeyinde <c>[EnableRateLimiting(AuthPolicy)]</c> taşıyor; o
/// politika IP başına bölümlüyor ve üretim varsayılanı dakikada 10. Yenileme isteği ise
/// tanımı gereği ÖLMÜŞ bir erişim token'ıyla gelir — <c>HttpContext.User</c> boştur, yani
/// istek her hâlükârda IP kovasına düşer. Mobil operatörler CGNAT kullandığı için binlerce
/// abone tek bir genel IPv4'ün arkasındadır ve o kova dakikalar içinde dolardı.
///
/// Sonuç sessiz olurdu: sunucu sağlıklı, günlük temiz, kullanıcıların bir kısmı çalışıyor
/// bir kısmı çalışmıyor. Aynı arıza 2026-09-05'te genel sınır için kapatılmıştı; buraya
/// dikkat edilmeseydi yenileme yolundan geri gelecekti.
///
/// Eylem düzeyinde ikinci bir <c>[EnableRateLimiting]</c> ile de çözülebilirdi ama
/// politikaların üst üste binme davranışı sürüme bağlı ve sessizce yanlış tarafa
/// düşebilir. Ayrı sınıf, o belirsizliği tamamen ortadan kaldırıyor.
///
/// ⚠️ UÇ <c>[AllowAnonymous]</c>: kimlik doğrulaması İSTENMEZ, çünkü erişim token'ı
/// ölmüş olacak. Bunun bedeli, <c>AccountStatusMiddleware</c>'in bu isteğe HİÇ
/// dokunmaması — ban/askı/silinme/damga kontrollerinin hepsi
/// <see cref="RefreshSessionHandler"/> içinde ELLE tekrarlanıyor. O kontrolleri oradan
/// kaldırmak, banı yenileme yoluyla delmek demektir.
/// </remarks>
[ApiController]
[EnableRateLimiting(RateLimiting.RefreshPolicy)]
[Route("api/session")]
public sealed class SessionController : ControllerBase
{
    private readonly IMediator _mediator;

    public SessionController(IMediator mediator) => _mediator = mediator;

    /// <param name="HwidHash">
    /// Girişteki ile AYNI cihaz parmak izi. Zorunlu — cihaz banı bu uçtan atlanamasın diye.
    /// </param>
    public sealed record RefreshRequest(string RefreshToken, string? HwidHash);

    /// <summary>
    /// Yenileme token'ını dönüştürür ve taze bir erişim token'ı döner. Yanıt biçimi
    /// girişinkiyle AYNI (<see cref="LoginResult"/>) — istemci iki yanıtı aynı kodla
    /// işleyebilsin diye.
    /// </summary>
    [HttpPost("refresh")]
    public async Task<LoginResult> Refresh(RefreshRequest request, CancellationToken ct)
        => await _mediator.Send(new RefreshSessionCommand(request.RefreshToken, request.HwidHash), ct);
}
