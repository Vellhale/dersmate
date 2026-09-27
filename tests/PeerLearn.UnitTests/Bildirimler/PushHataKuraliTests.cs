using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Features.Communication.Bildirimler;
using Xunit;

namespace PeerLearn.UnitTests.Bildirimler;

/// <summary>
/// Expo yanıtlarının dağıtıcıdaki anlamı: hangi kod cihazı öldürür, hangisi beklenir,
/// hangisi bölünür. Yanlış sınıflandırmanın iki pahalı yüzü var: yapılandırma hatasında
/// cihazları silmek (herkes bildirimsiz kalır) ve geçici hatada Failed yazmak (kesinti
/// bitince bildirim kaybolmuş olur).
/// </summary>
public class PushHataKuraliTests
{
    private static PushBileti Hata(string? kod) => new(false, null, kod, "maskeli");

    [Fact]
    public void Basarili_bilet() => Assert.Equal(BiletKarari.Basarili, PushHataKurali.Bilet(new PushBileti(true, "b1", null, null)));

    [Theory]
    [InlineData("DeviceNotRegistered", BiletKarari.CihazOlu)]
    [InlineData("MessageRateExceeded", BiletKarari.Gecici)]
    [InlineData("MessageTooBig", BiletKarari.Kalici)]
    [InlineData("InvalidCredentials", BiletKarari.Kalici)]   // FCM/APNs kimliği yanlış: cihazın suçu değil
    [InlineData("MismatchSenderId", BiletKarari.Kalici)]
    [InlineData("YeniBirKod", BiletKarari.Kalici)]
    [InlineData(null, BiletKarari.Kalici)]
    public void Bilet_hatalari(string? kod, BiletKarari karar) => Assert.Equal(karar, PushHataKurali.Bilet(Hata(kod)));

    [Theory]
    [InlineData("MessageTooBig", true)]
    [InlineData("InvalidCredentials", true)]
    [InlineData("MismatchSenderId", true)]
    [InlineData("DeviceNotRegistered", false)]
    [InlineData("MessageRateExceeded", false)]
    public void Yapilandirma_hatalari_herkesi_etkiler(string kod, bool yapilandirma)
        => Assert.Equal(yapilandirma, PushHataKurali.YapilandirmaHatasi(kod));

    [Fact]
    public void Yabanci_deneyim_haritasiyla_ayrilir()
    {
        var harita = new Dictionary<string, IReadOnlyList<string>> { ["@baska/proje"] = ["ExponentPushToken[x]"] };
        Assert.Equal(IstekKarari.YabanciDeneyim,
            PushHataKurali.Istek(new PushIstekHatasi(400, PushHataKurali.CokDeneyim, "m", harita)));
    }

    [Fact]
    public void Haritasiz_cok_deneyim_hatasi_bolunur()
        => Assert.Equal(IstekKarari.Bolunebilir,
            PushHataKurali.Istek(new PushIstekHatasi(400, PushHataKurali.CokDeneyim, "m", null)));

    [Theory]
    [InlineData(400, IstekKarari.Bolunebilir)]
    [InlineData(413, IstekKarari.Bolunebilir)]
    [InlineData(401, IstekKarari.Gecici)]   // erişim token'ı / Enhanced Security: hiçbir mesajın suçu değil
    [InlineData(403, IstekKarari.Gecici)]
    [InlineData(404, IstekKarari.Gecici)]
    [InlineData(429, IstekKarari.Gecici)]
    [InlineData(500, IstekKarari.Gecici)]
    [InlineData(502, IstekKarari.Gecici)]
    [InlineData(503, IstekKarari.Gecici)]
    public void Istek_hatalari(int durum, IstekKarari karar)
        => Assert.Equal(karar, PushHataKurali.Istek(new PushIstekHatasi(durum, "KOD", "m", null)));

    [Theory]
    [InlineData("ZamanAsimi")]
    [InlineData("AgHatasi")]
    public void Yanitsizlik_gecicidir(string kod)
        => Assert.Equal(IstekKarari.Gecici, PushHataKurali.Istek(new PushIstekHatasi(null, kod, null, null)));

    [Theory]
    [InlineData(401, true)]
    [InlineData(403, true)]
    [InlineData(429, false)]
    [InlineData(null, false)]
    public void Yetki_hatasi(int? durum, bool yetki)
        => Assert.Equal(yetki, PushHataKurali.YetkiHatasi(new PushIstekHatasi(durum, null, null, null)));

    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(4, 240)]
    [InlineData(5, 480)]
    [InlineData(6, 960)]
    [InlineData(7, 1800)]   // tavan: 30 dk
    [InlineData(40, 1800)]
    [InlineData(int.MaxValue, 1800)]  // üs kırpılıyor: taşıp negatif bekleme üretmez
    public void Bekleme_ustel_ve_tavanli(int deneme, int saniye)
        => Assert.Equal(TimeSpan.FromSeconds(saniye), PushHataKurali.Bekleme(deneme));

    [Fact]
    public void Deneme_siniri_alti()
        => Assert.Equal(6, PushHataKurali.EnFazlaDeneme);

    [Fact]
    public void Son_hata_maskeli_ve_kolon_sinirinda()
    {
        const string token = "ExponentPushToken[abcdefghijklmnopqrstuv]";
        var hata = PushHataKurali.SonHata("DeviceNotRegistered", $"\"{token}\" is not registered " + new string('x', 500));

        Assert.StartsWith("DeviceNotRegistered: ", hata);
        Assert.DoesNotContain("abcdefghijklmnop", hata);
        Assert.Equal(PushHataKurali.SonHataEnFazla, hata.Length);
    }

    [Fact]
    public void Son_hata_mesajsiz_ve_kodsuz()
    {
        Assert.Equal("ZamanAsimi", PushHataKurali.SonHata("ZamanAsimi", null));
        Assert.Equal("Bilinmeyen hata", PushHataKurali.SonHata(null, "  "));
        Assert.Equal("Hata: bir şey oldu", PushHataKurali.SonHata(null, "bir şey oldu"));
    }
}
