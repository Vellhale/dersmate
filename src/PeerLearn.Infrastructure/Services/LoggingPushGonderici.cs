using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Features.Communication.Bildirimler;
using PeerLearn.Application.Options;

namespace PeerLearn.Infrastructure.Services;

/// <summary>
/// Push:Provider = "Log": hiçbir şey göndermez, yalnızca maskeli günlük yazar ve Expo gibi
/// bilet döndürür. Geliştirmenin ve e2e testlerinin göndericisi (LoggingEmailSender emsali).
/// </summary>
/// <remarks>
/// ─── NE YAZILIYOR, NE YAZILMIYOR ────────────────────────────────────────────
/// Tür, alıcı etiketi (data.alici, opak) ve token'ın maskeli hâli. BAŞLIK VE GÖVDE
/// YAZILMAZ: gövdede arkadaşın adı var ve günlükler kişisel veri deposu değil. Teşhis için
/// satırın kendisi (Notifications) ve bu türe ait metin kodu yeterli.
///
/// ─── TEST KANCALARI (yalnızca bu sağlayıcıda) ──────────────────────────────
/// Token'ın son kısmı davranışı seçer; e2e paketi (tools/e2e-bildirim.ps1) bu token'larla
/// cihaz kaydedip Expo'nun hata yollarını gerçek bir Expo hesabı olmadan sınar:
/// <list type="bullet">
/// <item><c>…OLU]</c>: bilet ok, MAKBUZ DeviceNotRegistered (makbuz işi cihazı silmeli).
/// Durumsuz: bilet kimliğinin önekinden okunuyor, süreç yeniden başlasa da çalışır.</item>
/// <item><c>…YABANCI]</c>: istek düzeyi PUSH_TOO_MANY_EXPERIENCE_IDS; bu token'lar başka bir
/// deneyimin altında listelenir. Gerçek Expo bu hatayı yalnızca istekte İKİ proje varken
/// verir; kanca tek başına da verir ki sonuç partinin bileşimine bağlı olmasın.</item>
/// <item><c>…YAVAS]</c>: yanıt 3 saniye gecikir (gönderim sürerken cihaz silme senaryosu).</item>
/// </list>
/// Gerçek Expo token'ı rastgele 22 karakter; bu eklerle bitmesi pratikte imkânsız ve Log
/// sağlayıcısı zaten hiçbir şeyi dışarı göndermiyor.
/// </remarks>
public sealed class LoggingPushGonderici : IPushGonderici
{
    public const string OluEki = "OLU]";
    public const string YabanciEki = "YABANCI]";
    public const string YavasEki = "YAVAS]";

    /// <summary>Kancadaki yabancı deneyimin adı.</summary>
    public const string YabanciDeneyim = "@yabanci/baska-proje";

    private const string BiletOneki = "log-";
    private const string OluBiletOneki = "log-olu-";

    public static readonly TimeSpan YavasGecikme = TimeSpan.FromSeconds(3);

    private readonly PushOptions _ayar;
    private readonly ILogger<LoggingPushGonderici> _logger;

    public LoggingPushGonderici(IOptions<PushOptions> ayar, ILogger<LoggingPushGonderici> logger)
    {
        _ayar = ayar.Value;
        _logger = logger;
    }

    public async Task<PushGonderimSonucu> GonderAsync(IReadOnlyList<PushMesaji> mesajlar, CancellationToken ct)
    {
        // Gerçek göndericiyle aynı sözleşme: sınırı aşan çağrı kod hatası, burada da yakalansın.
        if (mesajlar.Count > PushSinirlari.EnFazlaMesaj)
        {
            throw new ArgumentOutOfRangeException(nameof(mesajlar), mesajlar.Count,
                $"Tek istekte en fazla {PushSinirlari.EnFazlaMesaj} mesaj; parçalamak çağıranın işi.");
        }

        if (mesajlar.Any(m => m.To.EndsWith(YavasEki, StringComparison.Ordinal)))
        {
            await Task.Delay(YavasGecikme, ct);
        }

        var yabancilar = mesajlar.Where(m => m.To.EndsWith(YabanciEki, StringComparison.Ordinal)).Select(m => m.To).ToList();
        if (yabancilar.Count > 0)
        {
            var bizim = string.IsNullOrWhiteSpace(_ayar.DeneyimKimligi) ? "@yerel/log" : _ayar.DeneyimKimligi.Trim();
            var deneyimler = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                [YabanciDeneyim] = yabancilar,
            };

            var bizimkiler = mesajlar.Select(m => m.To).Except(yabancilar).ToList();
            if (bizimkiler.Count > 0)
            {
                deneyimler[bizim] = bizimkiler;
            }

            _logger.LogInformation("[PUSH-LOG] {Sayi} mesajlık istek PUSH_TOO_MANY_EXPERIENCE_IDS ile reddedildi (test kancası).", mesajlar.Count);
            return PushGonderimSonucu.Hata(new PushIstekHatasi(
                400, PushHataKurali.CokDeneyim,
                "All push notification messages in the same request must be for the same project.",
                deneyimler));
        }

        var biletler = new List<PushBileti>(mesajlar.Count);
        foreach (var m in mesajlar)
        {
            var olu = m.To.EndsWith(OluEki, StringComparison.Ordinal);
            var bilet = (olu ? OluBiletOneki : BiletOneki) + Guid.NewGuid().ToString("N");
            biletler.Add(new PushBileti(true, bilet, null, null));

            _logger.LogInformation(
                "[PUSH-LOG] {Tur} → alıcı {Alici}, kanal {Kanal}, token {Token}, bilet {Bilet}",
                m.Data.Tur, m.Data.Alici, m.ChannelId ?? m.ThreadId ?? "-", PushTokenKurali.Maskele(m.To), bilet);
        }

        return new PushGonderimSonucu(null, biletler);
    }

    public Task<IReadOnlyDictionary<string, PushMakbuzu>> MakbuzlariAlAsync(IReadOnlyList<string> biletler, CancellationToken ct)
    {
        var sonuc = new Dictionary<string, PushMakbuzu>(StringComparer.Ordinal);
        foreach (var b in biletler)
        {
            // Yalnızca bu sağlayıcının verdiği biletler: sağlayıcı Expo'dan Log'a çevrildiyse
            // eski gerçek biletler "hazır değil" kalır ve 24 saatte sorulmadan silinir.
            if (b.StartsWith(OluBiletOneki, StringComparison.Ordinal))
            {
                sonuc[b] = new PushMakbuzu(false, PushHataKurali.CihazKayitliDegil, "Cihaz kayıtlı değil (test kancası).");
            }
            else if (b.StartsWith(BiletOneki, StringComparison.Ordinal))
            {
                sonuc[b] = new PushMakbuzu(true, null, null);
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, PushMakbuzu>>(sonuc);
    }
}
