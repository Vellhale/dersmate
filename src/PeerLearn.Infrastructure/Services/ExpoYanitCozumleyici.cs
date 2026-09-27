using System.Text.Json;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Features.Communication.Bildirimler;

namespace PeerLearn.Infrastructure.Services;

/// <summary>
/// Expo push API yanıtlarını (send ve getReceipts) sözleşme tiplerine çevirir ve dışarıdan
/// gelen her metni maskeler. Saf; birim testli (ExpoYanitCozumleyiciTests).
/// </summary>
/// <remarks>
/// ─── BİÇİMLER (docs.expo.dev/push-notifications/sending-notifications) ─────
/// <code>
/// send, başarılı:   { "data": [ { "status": "ok", "id": "…" },
///                             { "status": "error", "message": "…", "details": { "error": "DeviceNotRegistered" } } ] }
/// istek hatası:     { "errors": [ { "code": "PUSH_TOO_MANY_EXPERIENCE_IDS", "message": "…",
///                                   "details": { "@sahip/proje": [ "ExponentPushToken[…]" ] } } ] }
/// getReceipts:      { "data": { "&lt;bilet&gt;": { "status": "ok" } | { "status": "error", … } } }
/// </code>
/// data dizisi gönderilen mesajlarla AYNI SIRADA; bilet ↔ mesaj eşlemesi yalnızca sırayla.
///
/// ─── MASKELEME ──────────────────────────────────────────────────────────────
/// Expo'nun hata metni token'ı cümlenin içinde taşıyor ("ExponentPushToken[…] is not a
/// registered push notification recipient"). Buradan çıkan HER mesaj metni
/// PushTokenKurali.MetniMaskele'den geçiyor; LastError'a ve günlüğe yalnızca bu hâl gider.
/// Deneyim → token haritası (PUSH_TOO_MANY_EXPERIENCE_IDS) MASKELENMEZ: dağıtıcı yabancı
/// token'ları o haritadaki tam değerle eşleştirip ayırıyor; harita günlüğe yazılmıyor.
///
/// ─── SINIFLANDIRMA BURADA DEĞİL ─────────────────────────────────────────────
/// Hangi kodun ne anlama geldiği (geçici/kalıcı/cihaz öldü) sağlayıcıdan bağımsız bir karar
/// ve onu uygulayan dağıtıcı Application'da: PushHataKurali. Bu sınıf yalnızca Expo'nun
/// JSON'unu okur.
/// </remarks>
public static class ExpoYanitCozumleyici
{
    /// <summary>Gövde çözülemedi ya da beklenen alanları taşımıyor.</summary>
    public const string CozulemediKodu = "YanitCozulemedi";

    /// <summary>
    /// /push/send yanıtı. 2xx ve "data" dizisi varsa biletler (mesaj sırasıyla); aksi hâlde
    /// istek düzeyi hata.
    /// </summary>
    public static PushGonderimSonucu Gonderim(int httpDurumu, string? govde)
    {
        JsonDocument belge;
        try
        {
            belge = JsonDocument.Parse(string.IsNullOrWhiteSpace(govde) ? "{}" : govde);
        }
        catch (JsonException)
        {
            return PushGonderimSonucu.Hata(new PushIstekHatasi(httpDurumu, CozulemediKodu, Kisalt(govde), null));
        }

        using (belge)
        {
            var kok = belge.RootElement;

            if (httpDurumu is >= 200 and < 300
                && kok.ValueKind == JsonValueKind.Object
                && kok.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array)
            {
                var biletler = new List<PushBileti>(data.GetArrayLength());
                foreach (var o in data.EnumerateArray())
                {
                    biletler.Add(Bilet(o));
                }

                return new PushGonderimSonucu(null, biletler);
            }

            return PushGonderimSonucu.Hata(IstekHatasi(httpDurumu, kok, govde));
        }
    }

    /// <summary>
    /// /push/getReceipts yanıtı: bilet kimliği → makbuz. Çözülemeyen yanıt BOŞ sözlük
    /// (makbuzlar hazır değilmiş gibi; biletler bir sonraki turda yeniden sorulur).
    /// </summary>
    public static IReadOnlyDictionary<string, PushMakbuzu> Makbuzlar(string? govde)
    {
        var sonuc = new Dictionary<string, PushMakbuzu>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(govde))
        {
            return sonuc;
        }

        try
        {
            using var belge = JsonDocument.Parse(govde);
            if (belge.RootElement.ValueKind != JsonValueKind.Object
                || !belge.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object)
            {
                return sonuc;
            }

            foreach (var ozellik in data.EnumerateObject())
            {
                var o = ozellik.Value;
                if (o.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                sonuc[ozellik.Name] = Metin(o, "status") == "ok"
                    ? new PushMakbuzu(true, null, null)
                    : new PushMakbuzu(false, HataKodu(o), Maskeli(Metin(o, "message")));
            }
        }
        catch (JsonException)
        {
            // Kısmen okunmuş sonuç da bırakılmaz: yarım bir sözlük "hazır değil" ile
            // "yanıt bozuk"u karıştırırdı.
            return new Dictionary<string, PushMakbuzu>();
        }

        return sonuc;
    }

    /// <summary>Hiç yanıt gelmeyen durumlar: zaman aşımı ve ağ hatası (HttpDurumu null).</summary>
    public static PushIstekHatasi Yanitsiz(string kod, string? mesaj) => new(null, kod, Maskeli(mesaj), null);

    private static PushBileti Bilet(JsonElement o)
    {
        if (o.ValueKind != JsonValueKind.Object)
        {
            return new PushBileti(false, null, CozulemediKodu, null);
        }

        if (Metin(o, "status") == "ok")
        {
            var id = Metin(o, "id");
            return string.IsNullOrEmpty(id)
                ? new PushBileti(false, null, CozulemediKodu, "Bilet kimliği yok")
                : new PushBileti(true, id, null, null);
        }

        return new PushBileti(false, null, HataKodu(o), Maskeli(Metin(o, "message")));
    }

    /// <summary>details.error ("DeviceNotRegistered"…); yoksa null.</summary>
    private static string? HataKodu(JsonElement o)
        => o.TryGetProperty("details", out var d) && d.ValueKind == JsonValueKind.Object ? Metin(d, "error") : null;

    private static PushIstekHatasi IstekHatasi(int httpDurumu, JsonElement kok, string? govde)
    {
        if (kok.ValueKind == JsonValueKind.Object
            && kok.TryGetProperty("errors", out var hatalar)
            && hatalar.ValueKind == JsonValueKind.Array
            && hatalar.GetArrayLength() > 0)
        {
            var ilk = hatalar[0];
            var kod = Metin(ilk, "code");
            var mesaj = Maskeli(Metin(ilk, "message"));

            Dictionary<string, IReadOnlyList<string>>? deneyimler = null;
            if (kod == PushHataKurali.CokDeneyim
                && ilk.TryGetProperty("details", out var d)
                && d.ValueKind == JsonValueKind.Object)
            {
                deneyimler = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                foreach (var deneyim in d.EnumerateObject())
                {
                    if (deneyim.Value.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    deneyimler[deneyim.Name] = deneyim.Value.EnumerateArray()
                        .Where(t => t.ValueKind == JsonValueKind.String)
                        .Select(t => t.GetString()!)
                        .ToList();
                }
            }

            return new PushIstekHatasi(httpDurumu, kod, mesaj, deneyimler);
        }

        return new PushIstekHatasi(httpDurumu, httpDurumu is >= 200 and < 300 ? CozulemediKodu : null, Kisalt(govde), null);
    }

    private static string? Metin(JsonElement o, string ad)
        => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(ad, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static string? Maskeli(string? metin) => PushTokenKurali.MetniMaskele(metin);

    /// <summary>Tanımadığımız gövde: maskeli ve kısa (günlükte bir HTML hata sayfası akmasın).</summary>
    private static string? Kisalt(string? govde)
    {
        var m = Maskeli(govde);
        return m is { Length: > 200 } ? m[..200] + "…" : m;
    }
}
