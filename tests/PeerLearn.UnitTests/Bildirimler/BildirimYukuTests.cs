using System.Text.Json;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Features.Communication.Bildirimler;
using PeerLearn.Domain.Communication;
using Xunit;

namespace PeerLearn.UnitTests.Bildirimler;

/// <summary>
/// Platforma göre Expo mesajı. Testler SERİLEŞTİRİLMİŞ JSON üzerinden yazılıyor, çünkü
/// Expo'ya giden şey o: bir alanın nesnede null olması yetmez, JSON'da hiç görünmemeli.
/// </summary>
public class BildirimYukuTests
{
    private const string JwtAnahtari = "birim-testi-anahtari-en-az-otuz-iki-karakter-uzun";
    private static readonly BildirimEtiketi Etiket = new(JwtAnahtari);

    private static readonly Guid Sohbet = Guid.Parse("a3bb189e-8bf9-3888-9912-ace4e6543002");
    private static readonly Guid Alici = Guid.Parse("c8f2a1d4-5b6e-4f70-8a91-b2c3d4e5f607");
    private const string Token = "ExponentPushToken[xxxxxxxxxxxxxxxxxxxxxx]";

    private static BildirimTaslagi Taslak(
        NotificationType tur = NotificationType.NewMessage, string kanal = BildirimKanallari.Mesajlar,
        BildirimIcerigi? icerik = null, int? ttl = 21600, int? rozet = 4)
        => new(tur, kanal, icerik ?? BildirimMetni.YeniMesaj("Şükrü", 2), $"/sohbet/{Sohbet}",
            Etiket.Alici(Alici), Etiket.Sohbet(Sohbet), ttl, rozet);

    private static JsonElement Json(PushMesaji m)
        => JsonDocument.Parse(JsonSerializer.Serialize(m, BildirimYuku.JsonAyarlari)).RootElement;

    private static string[] Alanlar(JsonElement e) => e.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    [Fact]
    public void Android_yukunde_collapseId_yok_tag_ve_kanal_var()
    {
        // FCM çevrimdışı cihaz için yalnızca 4 collapse anahtarı saklıyor: beşinci sohbetin
        // mesajı telefona hiç ulaşmayabilirdi. Ekrandakini değiştiren alan zaten "tag".
        var j = Json(BildirimYuku.Kur(Taslak(), Token, PushPlatform.Android));

        Assert.Equal(new[] { "body", "channelId", "data", "priority", "tag", "title", "to", "ttl" }, Alanlar(j));
        Assert.Equal("mesajlar", j.GetProperty("channelId").GetString());
        Assert.Equal("high", j.GetProperty("priority").GetString());
        Assert.Equal(Etiket.Sohbet(Sohbet), j.GetProperty("tag").GetString());
    }

    [Fact]
    public void Android_yukunde_kanal_her_turde_var()
    {
        // Kanal verilmezse Android bildirimi varsayılan kanala düşürür ve kullanıcının o
        // kategori için telefonda seçtiği ayar atlanır.
        foreach (var tur in Enum.GetValues<NotificationType>())
        {
            var kanal = tur == NotificationType.Test ? BildirimKanallari.DersPlani : BildirimKanallari.Kanal(tur);
            var j = Json(BildirimYuku.Kur(Taslak(tur, kanal), Token, PushPlatform.Android));
            Assert.Equal(kanal, j.GetProperty("channelId").GetString());
            Assert.False(j.TryGetProperty("collapseId", out _), tur.ToString());
        }
    }

    [Fact]
    public void Ios_yukunde_collapseId_threadId_badge_var_android_alanlari_yok()
    {
        var j = Json(BildirimYuku.Kur(Taslak(), Token, PushPlatform.Ios));

        Assert.Equal(new[] { "badge", "body", "collapseId", "data", "sound", "threadId", "title", "to", "ttl" }, Alanlar(j));
        Assert.Equal(Etiket.Sohbet(Sohbet), j.GetProperty("collapseId").GetString());
        Assert.Equal(Etiket.Sohbet(Sohbet), j.GetProperty("threadId").GetString()); // mesajda grup = sohbet
        Assert.Equal(4, j.GetProperty("badge").GetInt32());
        Assert.Equal("default", j.GetProperty("sound").GetString());
    }

    [Theory]
    [InlineData(NotificationType.MatchRequest, "istekler", "istekler")]
    [InlineData(NotificationType.ApprovalPending, "ders-onayi", "dersler")]
    [InlineData(NotificationType.LessonSoon, "ders-plani", "dersler")]
    public void Ios_mesaj_disinda_kategori_grubunda_toplanir(NotificationType tur, string kanal, string grup)
    {
        var j = Json(BildirimYuku.Kur(Taslak(tur, kanal, rozet: null), Token, PushPlatform.Ios));
        Assert.Equal(grup, j.GetProperty("threadId").GetString());
        Assert.False(j.TryGetProperty("badge", out _)); // rozet null: dokunma
    }

    [Theory]
    [InlineData(PushPlatform.Android)]
    [InlineData(PushPlatform.Ios)]
    public void Data_yalnizca_tur_url_alici(PushPlatform platform)
    {
        var data = Json(BildirimYuku.Kur(Taslak(), Token, platform)).GetProperty("data");

        Assert.Equal(new[] { "alici", "tur", "url" }, Alanlar(data));
        Assert.Equal("mesaj", data.GetProperty("tur").GetString());
        Assert.Equal($"/sohbet/{Sohbet}", data.GetProperty("url").GetString());
        Assert.Equal(Etiket.Alici(Alici), data.GetProperty("alici").GetString());
    }

    [Fact]
    public void Etiketlerde_ve_datada_kullanici_kimligi_yok()
    {
        foreach (var platform in new[] { PushPlatform.Android, PushPlatform.Ios })
        {
            var metin = JsonSerializer.Serialize(BildirimYuku.Kur(Taslak(), Token, platform), BildirimYuku.JsonAyarlari);
            Assert.DoesNotContain(Alici.ToString("D"), metin, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Alici.ToString("N"), metin, StringComparison.OrdinalIgnoreCase);
            // Sohbet kimliği YALNIZCA url'de (dokunuş rotası); etikette HMAC.
            Assert.Equal(1, CountOf(metin, Sohbet.ToString("D")));
        }
    }

    private static int CountOf(string metin, string aranan)
        => (metin.Length - metin.Replace(aranan, string.Empty, StringComparison.OrdinalIgnoreCase).Length) / aranan.Length;

    [Theory]
    [InlineData(PushPlatform.Android)]
    [InlineData(PushPlatform.Ios)]
    public void Ttl_iki_platformda_da_gider(PushPlatform platform)
    {
        // iOS'ta APNs son kullanma tarihine dönüşüyor: gece kapalı kalan iPhone
        // "10 dakika sonra başlıyor"u sabah almasın.
        Assert.Equal(600, Json(BildirimYuku.Kur(Taslak(ttl: 600), Token, platform)).GetProperty("ttl").GetInt32());
        Assert.False(Json(BildirimYuku.Kur(Taslak(ttl: null), Token, platform)).TryGetProperty("ttl", out _));
    }

    [Fact]
    public void Istek_normal_oncelikli_digerleri_high()
    {
        var istek = BildirimYuku.Kur(Taslak(NotificationType.MatchRequest, BildirimKanallari.Istekler), Token, PushPlatform.Android);
        var ders = BildirimYuku.Kur(Taslak(NotificationType.LessonSoon, BildirimKanallari.DersPlani), Token, PushPlatform.Android);
        Assert.Equal("normal", istek.Priority);
        Assert.Equal("high", ders.Priority);
    }

    [Fact]
    public void En_uzun_ad_konu_ve_token_ile_bile_4096_baytin_altinda()
    {
        var uzunAd = new string('ğ', 200);
        var uzunKonu = string.Concat(Enumerable.Repeat("Çok uzun konu ", 20));
        var uzunToken = "ExponentPushToken[" + new string('x', PushTokenKurali.EnFazlaUzunluk - 19) + "]";
        Assert.True(PushTokenKurali.Gecerli(uzunToken));

        var taslaklar = new[]
        {
            Taslak(icerik: BildirimMetni.YeniMesaj(uzunAd, 99)),
            Taslak(NotificationType.ApprovalPending, BildirimKanallari.DersOnayi, BildirimMetni.OnayBekliyor(uzunKonu, TimeSpan.FromHours(47), true)),
            Taslak(NotificationType.LessonBooked, BildirimKanallari.DersPlani, BildirimMetni.DersPlanlandi(uzunKonu, TimeSpan.FromDays(30))),
        };

        foreach (var t in taslaklar)
        {
            foreach (var platform in new[] { PushPlatform.Android, PushPlatform.Ios })
            {
                var boyut = BildirimYuku.BaytBoyutu(BildirimYuku.Kur(t, uzunToken, platform));
                Assert.True(boyut < PushSinirlari.EnFazlaBayt, $"{t.Tur}/{platform}: {boyut} bayt");
            }
        }
    }

    [Fact]
    public void Baslik_ve_govde_taslakta_uzun_gelse_de_sinirda_kesilir()
    {
        var t = Taslak(icerik: new BildirimIcerigi(new string('B', 80), new string('G', 400)));
        var m = BildirimYuku.Kur(t, Token, PushPlatform.Android);
        Assert.Equal(PushSinirlari.BaslikEnFazla, m.Title.Length);
        Assert.Equal(PushSinirlari.GovdeEnFazla, m.Body.Length);
    }

    [Fact]
    public void Turkce_harfler_kacislanmaz()
    {
        // "ş" → "ş" altı bayt olurdu ve 4096 sınırını boşuna yerdi.
        var metin = JsonSerializer.Serialize(BildirimYuku.Kur(Taslak(), Token, PushPlatform.Android), BildirimYuku.JsonAyarlari);
        Assert.Contains("Şükrü", metin);
        Assert.DoesNotContain("\\u", metin);
    }

    [Fact]
    public void Bilinmeyen_platform_firlatir()
        => Assert.Throws<ArgumentOutOfRangeException>(() => BildirimYuku.Kur(Taslak(), Token, (PushPlatform)99));

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(0.9, 1)]
    [InlineData(599.99, 599)]
    [InlineData(21600, 21600)]
    public void Ttl_saniyesi_asagi_yuvarlanir_en_az_1(double saniye, int beklenen)
        => Assert.Equal(beklenen, BildirimYuku.TtlSaniye(TimeSpan.FromSeconds(saniye)));
}
