using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PeerLearn.Api.Startup;
using PeerLearn.Application.Features.Communication.Bildirimler;
using PeerLearn.Domain.Communication;

namespace PeerLearn.Api.Controllers;

/// <summary>
/// Push bildirimleri: cihaz kaydı, tercihler, aydınlatma kararı ve test bildirimi. Yalnızca
/// mobil çağırır; web'de ayar gösterilmez (web api.js'te ince sarmalayıcılar var, arayüz yok).
/// </summary>
/// <remarks>
/// Yol <c>api/push</c>; <c>/api/v1/push</c> kopyasını SurumOnekiKurali ekliyor (mobil
/// yalnızca v1'i çağırır). Mevcut <see cref="PreferencesController"/> DEĞİŞMEDİ: bildirim
/// tercihleri ayrı tabloda, web'in okuduğu DTO'ya dokunulmadı.
///
/// Genel hız sınırı (kimlikli istekte kullanıcı başına) dışında sınıf düzeyinde politika
/// YOK — bilerek: forget ucu eylem düzeyinde <see cref="RateLimiting.RefreshPolicy"/>
/// taşıyor ve sınıf düzeyinde ikinci bir politika olsaydı üst üste binme davranışı sürüme
/// bağlı kalırdı (bkz. SessionController).
/// </remarks>
[ApiController]
[Authorize]
[Route("api/push")]
public sealed class PushController : ControllerBase
{
    private readonly IMediator _mediator;

    public PushController(IMediator mediator) => _mediator = mediator;

    /// <param name="Token">ExponentPushToken[…].</param>
    /// <param name="Platform">"Android" | "Ios" (adla; KesinEnumDonusturucu tanımsızı reddeder).</param>
    /// <param name="HwidHash">getHwidHash(): girişte gönderilenle aynı değer.</param>
    /// <param name="KapaliKanallar">Android'de telefon ayarlarından kapatılmış kanal kimlikleri.</param>
    public sealed record CihazKaydiRequest(
        string? Token, PushPlatform? Platform, string? HwidHash, string[]? KapaliKanallar);

    /// <summary>
    /// Bu cihazın token'ını hesaba bağlar. İdempotent. <c>kayitli:false</c> hata değil:
    /// aydınlatma görülmemiş ya da bu cihazın oturumu kapalı (hiçbir şey yazılmadı).
    /// </summary>
    [HttpPut("devices")]
    public async Task<PushCihaziKaydiSonucu> RegisterDevice(CihazKaydiRequest request, CancellationToken ct)
        => await _mediator.Send(new PushCihaziKaydetCommand(
            User.GetUserId(), request.Token, request.Platform, request.HwidHash, request.KapaliKanallar), ct);

    public sealed record CihazUnutRequest(string? Token);

    /// <summary>
    /// Çevrimdışı çıkıştan sonraki ilk açılışta: token'ı taşıyan cihaz satırını siler.
    /// Her durumda 204 (varlığı sızdırmaz).
    /// </summary>
    /// <remarks>
    /// KİMLİKSİZ: çağrı anında oturum yok. Sınır yenileme ucununki (IP başına, cömert):
    /// CGNAT arkasındaki mobil kullanıcılar tek IP'yi paylaşıyor; kimlik politikasının
    /// 10/dk'sı orada dolardı. Token tahmin edilemez olduğu için kaba kuvvet tehdit değil.
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.RefreshPolicy)]
    [HttpPost("devices/forget")]
    public async Task<IActionResult> ForgetDevice(CihazUnutRequest request, CancellationToken ct)
    {
        await _mediator.Send(new PushCihaziUnutCommand(request.Token), ct);
        return NoContent();
    }

    /// <summary>Dört kategori, aydınlatma durumu ve alıcı etiketi. Satır yoksa varsayılanlar.</summary>
    [HttpGet("preferences")]
    public async Task<BildirimTercihleriDto> GetPreferences(CancellationToken ct)
        => await _mediator.Send(new BildirimTercihleriQuery(User.GetUserId()), ct);

    /// <param name="Acik">Zorunlu. bool? — eksik alan sessizce "kapalı" sayılmasın.</param>
    public sealed record TercihRequest(bool? Acik);

    /// <summary>Tek kategori: mesajlar | istekler | ders-onayi | ders-plani. Tek sütunluk yazım.</summary>
    [HttpPut("preferences/{kategori}")]
    public async Task<IActionResult> SetPreference(string kategori, TercihRequest request, CancellationToken ct)
    {
        await _mediator.Send(new BildirimTercihiDegistirCommand(User.GetUserId(), kategori, request.Acik), ct);
        return NoContent();
    }

    /// <param name="Karar">"Acildi" | "Ertelendi".</param>
    public sealed record IzinKarariRequest(PushIzinKarari? Karar);

    /// <summary>Aydınlatma ekranının sonucu: ilk "Aç" anı ya da bir erteleme daha.</summary>
    [HttpPut("prompt")]
    public async Task<IActionResult> PromptDecision(IzinKarariRequest request, CancellationToken ct)
    {
        await _mediator.Send(new PushIzinKarariCommand(User.GetUserId(), request.Karar), ct);
        return NoContent();
    }

    /// <param name="Tur">"mesaj" | "istek" | "onay" | "ders".</param>
    public sealed record TestRequest(string? Tur);

    /// <summary>
    /// Kendi bağlı cihazlarına tek bir test bildirimi (kuyruktan geçer). Son 10 dakikada 3'ten
    /// fazlası 429.
    /// </summary>
    [HttpPost("test")]
    public async Task<TestBildirimiSonucu> SendTest(TestRequest request, CancellationToken ct)
        => await _mediator.Send(new TestBildirimiCommand(User.GetUserId(), request.Tur), ct);
}
