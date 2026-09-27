using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Features.Communication.Bildirimler;
using PeerLearn.Application.Options;

namespace PeerLearn.Infrastructure.Services;

/// <summary>
/// Expo Push Service üzerinden gerçek gönderim. Düz HTTP; typed HttpClient
/// (DependencyInjection: AddHttpClient&lt;ExpoPushGonderici&gt;).
/// </summary>
/// <remarks>
/// ─── NEDEN SDK YOK ──────────────────────────────────────────────────────────
/// Topluluğun .NET SDK'sı tag, collapseId ve threadId alanlarını taşımıyor; üçü de bu
/// tasarımın temeli (cihazdaki bildirimi yenisiyle değiştirme). SmtpEmailSender'daki "harici
/// paket yok" tercihiyle aynı: iki uç, düz JSON — bağımlılık getirmeye değmez.
///
/// ─── ERİŞİM TOKEN'I İSTEK BAŞINA ────────────────────────────────────────────
/// Authorization başlığı her HttpRequestMessage'a ayrı ekleniyor, HttpClient'ın paylaşılan
/// DefaultRequestHeaders'ına DEĞİL: typed client'ın başlıkları aynı handler havuzunu kullanan
/// her örnekte görünür ve değişikliği iş parçacığı güvenli değildir. Token boşsa başlık hiç
/// eklenmez (Enhanced Security kapalıyken Expo token'sız da kabul ediyor; ProductionGuard
/// üretimde boş token'a zaten izin vermiyor).
///
/// ─── FIRLATMAZ (İPTAL HARİÇ) ────────────────────────────────────────────────
/// Ağ hatası, zaman aşımı, HTTP hatası ve bozuk yanıt PushIstekHatasi olarak DÖNER; dağıtıcı
/// her birini satır bazında sınıflandırıp yazmak zorunda (PushHataKurali). Yalnızca çağıranın
/// iptali (kapanış) OperationCanceledException olarak çıkar.
///
/// ⚠️ Token'lar ve erişim token'ı günlüğe ASLA tam yazılmaz; Expo'nun hata metinleri
/// ExpoYanitCozumleyici'de maskelenir.
/// </remarks>
public sealed class ExpoPushGonderici : IPushGonderici
{
    public const string TemelAdres = "https://exp.host";
    public const string GonderimYolu = "/--/api/v2/push/send";
    public const string MakbuzYolu = "/--/api/v2/push/getReceipts";

    private readonly HttpClient _http;
    private readonly PushOptions _ayar;
    private readonly ILogger<ExpoPushGonderici> _logger;

    public ExpoPushGonderici(HttpClient http, IOptions<PushOptions> ayar, ILogger<ExpoPushGonderici> logger)
    {
        _http = http;
        _ayar = ayar.Value;
        _logger = logger;
    }

    public async Task<PushGonderimSonucu> GonderAsync(IReadOnlyList<PushMesaji> mesajlar, CancellationToken ct)
    {
        /* Sınır sözleşmenin parçası ve PARÇALAMA BURADA YAPILMIYOR (bilinçli): parçalardan biri
           istek düzeyinde düşerse sonuç "bütün istek düştü" diye tek bir PushGonderimSonucu'na
           sığmaz; kabul edilmiş parçanın biletleri kaybolur ve dağıtıcı onları yeniden
           gönderirdi. Dağıtıcı zaten 100'lük alt partilerle çağırıyor. */
        if (mesajlar.Count > PushSinirlari.EnFazlaMesaj)
        {
            throw new ArgumentOutOfRangeException(nameof(mesajlar), mesajlar.Count,
                $"Tek istekte en fazla {PushSinirlari.EnFazlaMesaj} mesaj; parçalamak çağıranın işi.");
        }

        if (mesajlar.Count == 0)
        {
            return new PushGonderimSonucu(null, []);
        }

        var govde = JsonSerializer.Serialize(mesajlar, BildirimYuku.JsonAyarlari);
        using var istek = Istek(GonderimYolu, govde);

        try
        {
            using var yanit = await _http.SendAsync(istek, ct);
            var metin = await yanit.Content.ReadAsStringAsync(ct);
            return ExpoYanitCozumleyici.Gonderim((int)yanit.StatusCode, metin);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // Çağıran iptal etmediyse bu HttpClient.Timeout (ZamanAsimiSaniye).
            return PushGonderimSonucu.Hata(ExpoYanitCozumleyici.Yanitsiz("ZamanAsimi", $"{_ayar.ZamanAsimiSaniye} sn içinde yanıt yok"));
        }
        catch (HttpRequestException ex)
        {
            return PushGonderimSonucu.Hata(ExpoYanitCozumleyici.Yanitsiz("AgHatasi", ex.Message));
        }
    }

    public async Task<IReadOnlyDictionary<string, PushMakbuzu>> MakbuzlariAlAsync(IReadOnlyList<string> biletler, CancellationToken ct)
    {
        if (biletler.Count > PushSinirlari.EnFazlaBilet)
        {
            throw new ArgumentOutOfRangeException(nameof(biletler), biletler.Count,
                $"Tek istekte en fazla {PushSinirlari.EnFazlaBilet} bilet.");
        }

        if (biletler.Count == 0)
        {
            return new Dictionary<string, PushMakbuzu>();
        }

        var govde = JsonSerializer.Serialize(new { ids = biletler }, BildirimYuku.JsonAyarlari);
        using var istek = Istek(MakbuzYolu, govde);

        try
        {
            using var yanit = await _http.SendAsync(istek, ct);
            var metin = await yanit.Content.ReadAsStringAsync(ct);

            if (!yanit.IsSuccessStatusCode)
            {
                // 401/403 herkesi etkiler (erişim token'ı ya da Enhanced Security): kritik.
                var durum = (int)yanit.StatusCode;
                var hata = ExpoYanitCozumleyici.Gonderim(durum, metin).IstekHatasi;
                _logger.Log(durum is 401 or 403 ? LogLevel.Critical : LogLevel.Warning,
                    "Push makbuzları alınamadı (HTTP {Durum}, {Kod}): {Mesaj}", durum, hata?.Kod, hata?.Mesaj);
                return new Dictionary<string, PushMakbuzu>();
            }

            return ExpoYanitCozumleyici.Makbuzlar(metin);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException)
        {
            _logger.LogWarning("Push makbuzları alınamadı ({Tur}): {Mesaj}", ex.GetType().Name, PushTokenKurali.MetniMaskele(ex.Message));
            return new Dictionary<string, PushMakbuzu>();
        }
    }

    private HttpRequestMessage Istek(string yol, string jsonGovde)
    {
        var istek = new HttpRequestMessage(HttpMethod.Post, yol)
        {
            Content = new StringContent(jsonGovde, Encoding.UTF8, "application/json"),
        };

        if (!string.IsNullOrWhiteSpace(_ayar.AccessToken))
        {
            istek.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _ayar.AccessToken.Trim());
        }

        return istek;
    }
}
