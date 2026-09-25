using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Features.Communication.Bildirimler;
using PeerLearn.Infrastructure.Services;
using Xunit;

namespace PeerLearn.UnitTests.Bildirimler;

/// <summary>
/// Expo'nun /push/send ve /push/getReceipts yanıtlarının çözümü ve maskelenmesi.
/// Biçimler docs.expo.dev/push-notifications/sending-notifications'dan.
/// </summary>
/// <remarks>
/// Sınıflandırma (hangi kod ne demek) PushHataKurali'nde; burada yanıtın o sınıflandırmaya
/// DOĞRU girdiyi verdiği de sınanıyor (çözümle → sınıflandır zinciri).
/// </remarks>
public class ExpoYanitCozumleyiciTests
{
    private const string Ic = "abcdefghijklmnopqrstuv";
    private const string Token = "ExponentPushToken[" + Ic + "]";

    private static PushGonderimSonucu Gonder(string govde, int durum = 200) => ExpoYanitCozumleyici.Gonderim(durum, govde);

    private static string BiletHatasi(string kod, string mesaj)
        => $$$"""{"status":"error","message":"{{{mesaj}}}","details":{"error":"{{{kod}}}","expoPushToken":"{{{Token}}}"}}""";

    [Fact]
    public void Biletler_mesaj_sirasiyla_eslesir()
    {
        var sonuc = Gonder($$"""
            {"data":[
              {"status":"ok","id":"bilet-1"},
              {{BiletHatasi("DeviceNotRegistered", $"\\\"{Token}\\\" is not a registered push notification recipient")}},
              {"status":"ok","id":"bilet-3"},
              {{BiletHatasi("MessageRateExceeded", "too fast")}},
              {{BiletHatasi("MessageTooBig", "too big")}},
              {{BiletHatasi("InvalidCredentials", "bad credentials")}}
            ]}
            """);

        Assert.Null(sonuc.IstekHatasi);
        Assert.Equal(6, sonuc.Biletler.Count);
        Assert.Equal(new PushBileti(true, "bilet-1", null, null), sonuc.Biletler[0]);
        Assert.Equal("bilet-3", sonuc.Biletler[2].BiletId);

        Assert.Equal(
            new[] { BiletKarari.Basarili, BiletKarari.CihazOlu, BiletKarari.Basarili, BiletKarari.Gecici, BiletKarari.Kalici, BiletKarari.Kalici },
            sonuc.Biletler.Select(PushHataKurali.Bilet));
    }

    [Fact]
    public void Bilet_hata_mesaji_maskelenir()
    {
        var sonuc = Gonder($$"""{"data":[{{BiletHatasi("DeviceNotRegistered", $"\\\"{Token}\\\" is not a registered push notification recipient")}}]}""");
        var mesaj = sonuc.Biletler[0].HataMesaji!;

        Assert.DoesNotContain(Ic, mesaj);
        Assert.Contains("…qrstuv]", mesaj);
    }

    [Fact]
    public void Kimliksiz_ok_bileti_cozulemedi_sayilir()
    {
        var sonuc = Gonder("""{"data":[{"status":"ok"}]}""");
        Assert.Equal(new PushBileti(false, null, ExpoYanitCozumleyici.CozulemediKodu, "Bilet kimliği yok"), sonuc.Biletler[0]);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public void Yetki_hatasi_istek_duzeyinde_ve_gecici(int durum)
    {
        var sonuc = Gonder("""{"errors":[{"code":"UNAUTHORIZED","message":"The bearer token is invalid"}]}""", durum);

        var hata = Assert.IsType<PushIstekHatasi>(sonuc.IstekHatasi);
        Assert.Empty(sonuc.Biletler);
        Assert.Equal(durum, hata.HttpDurumu);
        Assert.Equal("UNAUTHORIZED", hata.Kod);
        Assert.True(PushHataKurali.YetkiHatasi(hata));
        Assert.Equal(IstekKarari.Gecici, PushHataKurali.Istek(hata));
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public void Asiri_yuk_ve_sunucu_hatasi_beklenir(int durum)
    {
        var sonuc = Gonder("""{"errors":[{"code":"TOO_MANY_REQUESTS","message":"slow down"}]}""", durum);
        Assert.Equal(IstekKarari.Gecici, PushHataKurali.Istek(sonuc.IstekHatasi!));
    }

    [Fact]
    public void Yabanci_deneyim_haritasi_tam_tokenla_cikar()
    {
        // Harita MASKELENMEZ: dağıtıcı yabancı token'ları bu tam değerle eşleştirip ayırıyor.
        var sonuc = Gonder($$"""
            {"errors":[{"code":"PUSH_TOO_MANY_EXPERIENCE_IDS",
              "message":"All push notification messages in the same request must be for the same project",
              "details":{"@yabanci/proje":["{{Token}}"],"@ardaerenguler/dersmate":["ExponentPushToken[x1]","ExponentPushToken[x2]"]},
              "isTransient":false}]}
            """, 400);

        var hata = sonuc.IstekHatasi!;
        Assert.Equal(PushHataKurali.CokDeneyim, hata.Kod);
        Assert.Equal(new[] { Token }, hata.DeneyimTokenlari!["@yabanci/proje"]);
        Assert.Equal(2, hata.DeneyimTokenlari["@ardaerenguler/dersmate"].Count);
        Assert.Equal(IstekKarari.YabanciDeneyim, PushHataKurali.Istek(hata));
    }

    [Fact]
    public void Diger_400_bolunerek_yalitilir()
    {
        var sonuc = Gonder("""{"errors":[{"code":"VALIDATION_ERROR","message":"\"to\" must be a string"}]}""", 400);
        Assert.Null(sonuc.IstekHatasi!.DeneyimTokenlari);
        Assert.Equal(IstekKarari.Bolunebilir, PushHataKurali.Istek(sonuc.IstekHatasi));
    }

    [Fact]
    public void Tanimsiz_govde_kisaltilir_ve_maskelenir()
    {
        var html = "<html>Bad gateway " + Token + new string('x', 400) + "</html>";
        var hata = Gonder(html, 502).IstekHatasi!;

        Assert.Equal(502, hata.HttpDurumu);
        Assert.DoesNotContain(Ic, hata.Mesaj);
        Assert.True(hata.Mesaj!.Length <= 201);
        Assert.Equal(IstekKarari.Gecici, PushHataKurali.Istek(hata));
    }

    [Theory]
    [InlineData("""{"sonuc":"ok"}""")]
    [InlineData("""{"data":{}}""")]
    [InlineData("")]
    public void Basarili_durumda_beklenmeyen_govde_cozulemedi(string govde)
        => Assert.Equal(ExpoYanitCozumleyici.CozulemediKodu, Gonder(govde).IstekHatasi!.Kod);

    [Fact]
    public void Bozuk_json_cozulemedi()
        => Assert.Equal(ExpoYanitCozumleyici.CozulemediKodu, Gonder("{bozuk", 200).IstekHatasi!.Kod);

    [Fact]
    public void Yanitsiz_hata_maskelenir()
    {
        var hata = ExpoYanitCozumleyici.Yanitsiz("AgHatasi", "bağlantı reddedildi " + Token);
        Assert.Null(hata.HttpDurumu);
        Assert.Equal("AgHatasi", hata.Kod);
        Assert.DoesNotContain(Ic, hata.Mesaj);
    }

    // ─── Makbuzlar ───────────────────────────────────────────────────────────

    [Fact]
    public void Makbuzlar_bilet_kimligine_gore()
    {
        var m = ExpoYanitCozumleyici.Makbuzlar($$$"""
            {"data":{
              "b1":{"status":"ok"},
              "b2":{"status":"error","message":"\"{{{Token}}}\" gone","details":{"error":"DeviceNotRegistered"}},
              "b3":{"status":"error","message":"rate","details":{"error":"MessageRateExceeded"}}
            }}
            """);

        Assert.Equal(3, m.Count);
        Assert.Equal(new PushMakbuzu(true, null, null), m["b1"]);
        Assert.Equal("DeviceNotRegistered", m["b2"].HataKodu);
        Assert.DoesNotContain(Ic, m["b2"].HataMesaji);
        Assert.Equal("MessageRateExceeded", m["b3"].HataKodu);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bozuk{")]
    [InlineData("""{"data":[]}""")]
    [InlineData("""{"errors":[]}""")]
    public void Okunamayan_makbuz_yaniti_bos_sozluk_biletler_sonra_yeniden_sorulur(string govde)
        => Assert.Empty(ExpoYanitCozumleyici.Makbuzlar(govde));

    [Fact]
    public void Hicbir_daldan_tam_token_cikmaz()
    {
        var govdeler = new[]
        {
            $$"""{"data":[{{BiletHatasi("DeviceNotRegistered", Token)}}]}""",
            $$"""{"errors":[{"code":"X","message":"{{Token}}"}]}""",
            "<html>" + Token + "</html>",
        };

        foreach (var g in govdeler)
        {
            foreach (var durum in new[] { 200, 400, 502 })
            {
                var s = ExpoYanitCozumleyici.Gonderim(durum, g);
                Assert.DoesNotContain(Ic, s.IstekHatasi?.Mesaj ?? string.Empty);
                Assert.All(s.Biletler, b => Assert.DoesNotContain(Ic, b.HataMesaji ?? string.Empty));
                Assert.DoesNotContain(Ic, PushHataKurali.SonHata(s.IstekHatasi?.Kod, s.IstekHatasi?.Mesaj));
            }
        }
    }
}
