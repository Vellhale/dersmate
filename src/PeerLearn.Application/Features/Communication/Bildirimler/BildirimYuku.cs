using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using PeerLearn.Application.Abstractions;
using PeerLearn.Domain.Communication;

namespace PeerLearn.Application.Features.Communication.Bildirimler;

/// <summary>
/// Bir bildirimin cihazdan bağımsız kısmı: dağıtıcı bunu bir kez kurar, BildirimYuku her
/// cihaz için platformuna göre bir <see cref="PushMesaji"/> üretir.
/// </summary>
/// <param name="Tur">Defter satırının türü (data.tur buradan).</param>
/// <param name="Kanal">BildirimKanallari.Kanal(tur, testKanali).</param>
/// <param name="Icerik">BildirimMetni'nden.</param>
/// <param name="Url">Dokununca açılacak rota; mobil beyaz listesine uymalı.</param>
/// <param name="Alici">BildirimEtiketi.Alici(alıcı).</param>
/// <param name="Etiket">BildirimEtiketi'nden (s-/i-/id-/d-/t-). Android tag, iOS collapseId.</param>
/// <param name="TtlSaniye">Bu süreden sonra sağlayıcı teslim etmeye çalışmaz. Null: sağlayıcı varsayılanı.</param>
/// <param name="Rozet">iOS uygulama ikonu rozeti: alıcının TOPLAM okunmamış mesaj sayısı. Null: dokunma.</param>
public sealed record BildirimTaslagi(
    NotificationType Tur,
    string Kanal,
    BildirimIcerigi Icerik,
    string Url,
    string Alici,
    string Etiket,
    int? TtlSaniye,
    int? Rozet);

/// <summary>
/// Expo mesajını platforma göre kurar. Saf; birim testli (BildirimYukuTests).
/// </summary>
/// <remarks>
/// ─── ANDROID'DE collapseId YOK ──────────────────────────────────────────────
/// Expo collapseId'yi FCM collapse_key'e yazıyor. FCM, çevrimdışı cihaz için yalnızca
/// DÖRT farklı collapse anahtarı saklıyor ve hangisini tutacağı belirsiz: beşinci
/// sohbetin mesajı telefona hiç ulaşmayabilirdi. Ekrandaki bildirimi değiştiren alan
/// zaten "tag"; Android'de yalnızca o gönderilir.
///
/// ─── iOS'TA collapseId VAR ──────────────────────────────────────────────────
/// apns-collapse-id hem teslimde birleştirir hem ekrandakini değiştirir; iOS'ta "tag"
/// karşılığı yok. threadId bildirim merkezinde gruplama.
///
/// ─── data YALNIZCA {tur, url, alici} ────────────────────────────────────────
/// Kişi kimliği, ad, içerik data'ya konmaz. Etiketlerde GUID yok (HMAC).
///
/// ⚠️ ttl İKİ PLATFORMDA da gönderiliyor (tasarım metni yalnızca Android'de sayıyordu):
/// iOS'ta ttl APNs son kullanma tarihine dönüşüyor. Gönderilmezse gece kapalı kalan bir
/// iPhone "10 dakika sonra başlıyor" bildirimini sabah alırdı.
/// </remarks>
public static class BildirimYuku
{
    /// <summary>
    /// Expo istek gövdesinin serileştirme ayarı: camelCase (özellik adları Expo alan
    /// adlarıyla birebir), null alanlar yazılmaz. Gönderici de testler de BUNU kullanır;
    /// bayt sınırı ölçümü gönderilecek baytla aynı olsun.
    /// </summary>
    /// <remarks>
    /// Kodlayıcı Türkçe harfleri kaçışlamıyor ("ş" → "ş" altı bayt olurdu ve 4096
    /// sınırını boşuna yerdi). HTML'e gömülmeyen bir JSON API gövdesi için güvenli.
    /// </remarks>
    public static readonly JsonSerializerOptions JsonAyarlari = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>Bu cihaz için Expo mesajı.</summary>
    public static PushMesaji Kur(BildirimTaslagi t, string token, PushPlatform platform)
    {
        var veri = new PushVerisi(BildirimKanallari.VeriTuru(t.Tur), t.Url, t.Alici);
        var baslik = BildirimMetni.Kes(t.Icerik.Baslik, PushSinirlari.BaslikEnFazla);
        var govde = BildirimMetni.Kes(t.Icerik.Govde, PushSinirlari.GovdeEnFazla);

        return platform switch
        {
            PushPlatform.Android => new PushMesaji
            {
                To = token,
                Title = baslik,
                Body = govde,
                Data = veri,
                Ttl = t.TtlSaniye,
                // Kanal HER ZAMAN: verilmezse Android bildirimi varsayılan kanala düşürür ve
                // kullanıcının o kategori için telefonda seçtiği ayar (ör. sessiz) atlanır.
                ChannelId = t.Kanal,
                Priority = BildirimKanallari.Oncelik(t.Kanal),
                Tag = t.Etiket,
            },
            PushPlatform.Ios => new PushMesaji
            {
                To = token,
                Title = baslik,
                Body = govde,
                Data = veri,
                Ttl = t.TtlSaniye,
                Sound = "default",
                Badge = t.Rozet,
                CollapseId = t.Etiket,
                ThreadId = BildirimKanallari.IosGrubu(t.Kanal) ?? t.Etiket,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Bilinmeyen push platformu.")
        };
    }

    /// <summary>Mesajın gönderilecek JSON'daki bayt boyutu (UTF-8).</summary>
    public static int BaytBoyutu(PushMesaji mesaj)
        => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(mesaj, JsonAyarlari));

    /// <summary>
    /// Kalan süreyi ttl saniyesine çevirir (aşağı yuvarlar), en az 1. Süresi geçmiş satırı
    /// dağıtıcı zaten göndermiyor; alt sınır yalnızca negatif ya da sıfır değerin
    /// sağlayıcıya gitmemesi için.
    /// </summary>
    public static int TtlSaniye(TimeSpan kalan) => (int)Math.Clamp(Math.Floor(kalan.TotalSeconds), 1, int.MaxValue);
}
