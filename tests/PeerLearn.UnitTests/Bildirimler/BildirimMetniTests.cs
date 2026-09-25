using System.Globalization;
using System.Reflection;
using PeerLearn.Application.Features.Communication.Bildirimler;
using Xunit;

namespace PeerLearn.UnitTests.Bildirimler;

/// <summary>
/// Bildirim metinleri, ad temizleyici ve göreli süreler.
/// </summary>
/// <remarks>
/// Gizlilik güvenceleri bu dosyada YAPISAL olarak da sınanıyor: istek ve ders metinleri
/// kişi adı ALAMAZ (imzalarında ad parametresi yok) ve hiçbir metin mesaj içeriği alamaz.
/// Değer testleri bugünkü metni kilitler; yapısal testler yarın eklenecek bir parametrenin
/// kuralı sessizce delmesini engeller.
/// </remarks>
public class BildirimMetniTests
{
    // ─── Tür başına başlık ve gövde ──────────────────────────────────────────

    [Fact]
    public void Yeni_mesaj_tek_ve_cok()
    {
        Assert.Equal(new BildirimIcerigi("Yeni mesaj", "Ayşe sana yeni bir mesaj gönderdi."), BildirimMetni.YeniMesaj("Ayşe", 1));
        Assert.Equal("Ayşe sana 3 yeni mesaj gönderdi.", BildirimMetni.YeniMesaj("Ayşe", 3).Govde);
        Assert.Equal("Ayşe sana yeni bir mesaj gönderdi.", BildirimMetni.YeniMesaj("Ayşe", 0).Govde);
    }

    [Fact]
    public void Yeni_istek_konulu_ve_konusuz_ad_tasimaz()
    {
        Assert.Equal(new BildirimIcerigi("Yeni arkadaş isteği", "Türev konusunda sana yeni bir arkadaş isteği geldi."), BildirimMetni.YeniIstek("Türev"));
        Assert.Equal("Sana yeni bir arkadaş isteği geldi.", BildirimMetni.YeniIstek(null).Govde);
        Assert.Equal("Sana yeni bir arkadaş isteği geldi.", BildirimMetni.YeniIstek("   ").Govde);
    }

    [Fact]
    public void Istek_kabulu_adli_ve_genel()
    {
        Assert.Equal(new BildirimIcerigi("Arkadaş isteğin kabul edildi", "Ayşe isteğini kabul etti. Artık mesajlaşabilirsiniz."), BildirimMetni.IstekKabul("Ayşe"));
        Assert.Equal("Arkadaş isteğin kabul edildi. Artık mesajlaşabilirsiniz.", BildirimMetni.IstekKabul("admin").Govde);
        Assert.Equal("Arkadaş isteğin kabul edildi. Artık mesajlaşabilirsiniz.", BildirimMetni.IstekKabul(null).Govde);
    }

    [Fact]
    public void Istek_dusecek_ozeti_tek_ve_cok()
    {
        Assert.Equal(new BildirimIcerigi("Yanıt bekleyen isteklerin var",
            "Bir arkadaş isteğin yanıt bekliyor. Yanıtlamazsan yaklaşık 20 saat sonra düşecek."),
            BildirimMetni.IstekDusecek(1, TimeSpan.FromHours(20.4)));
        Assert.Equal("3 arkadaş isteğin yanıt bekliyor. İlki yaklaşık 7 saat sonra düşecek.",
            BildirimMetni.IstekDusecek(3, TimeSpan.FromHours(7)).Govde);
    }

    [Fact]
    public void Onay_bekliyor_ilk_ve_itiraz_sonrasi()
    {
        Assert.Equal(new BildirimIcerigi("Dersin onay bekliyor", "Türev dersin tamamlandı olarak işaretlendi. 47 saat içinde onayla ya da itiraz et."),
            BildirimMetni.OnayBekliyor("Türev", TimeSpan.FromHours(47.9), itirazSonrasi: false));
        Assert.Equal("İtiraz sonuçlandı. Türev dersin yeniden onayını bekliyor; 40 dakika içinde onaylamazsan otomatik onaylanır.",
            BildirimMetni.OnayBekliyor("Türev", TimeSpan.FromMinutes(40), itirazSonrasi: true).Govde);
    }

    [Fact]
    public void Ders_metinleri()
    {
        Assert.Equal(new BildirimIcerigi("Dersin yakında otomatik onaylanacak",
            "Türev dersin yaklaşık 24 saat sonra otomatik olarak onaylanacak. Bir sorun varsa itiraz etmek için dokun."),
            BildirimMetni.OtoOnayYaklasiyor("Türev", TimeSpan.FromHours(23.8)));
        Assert.Equal(new BildirimIcerigi("Yeni ders planlandı", "Türev dersin 5 dakika sonra başlıyor. Ayrıntılar için dokun."),
            BildirimMetni.DersPlanlandi("Türev", TimeSpan.FromMinutes(7)));
        Assert.Equal(new BildirimIcerigi("Ders iptal edildi", "Türev dersin iptal edildi."), BildirimMetni.DersIptal("Türev"));
        Assert.Equal(new BildirimIcerigi("Dersin yaklaşıyor", "Türev dersin 1 saat sonra başlıyor."),
            BildirimMetni.DersYaklasiyor("Türev", TimeSpan.FromMinutes(57), 60));
        Assert.Equal(new BildirimIcerigi("Dersin birazdan başlıyor", "Türev dersin 10 dakika sonra başlıyor."),
            BildirimMetni.DersYaklasiyor("Türev", TimeSpan.FromMinutes(9), 10));
    }

    [Theory]
    [InlineData("mesajlar", "Mesaj bildirimleri bu cihaza ulaşıyor.")]
    [InlineData("istekler", "Arkadaş isteği bildirimleri bu cihaza ulaşıyor.")]
    [InlineData("ders-onayi", "Ders onayı bildirimleri bu cihaza ulaşıyor.")]
    [InlineData("ders-plani", "Ders planı bildirimleri bu cihaza ulaşıyor.")]
    public void Test_metni_kanala_gore(string kanal, string govde)
        => Assert.Equal(new BildirimIcerigi("Test bildirimi", govde), BildirimMetni.Test(kanal));

    // ─── Gizlilik: yapısal güvenceler ────────────────────────────────────────

    private static IEnumerable<MethodInfo> MetinFonksiyonlari()
        => typeof(BildirimMetni).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(BildirimIcerigi));

    [Fact]
    public void Kisi_adini_yalnizca_mesaj_ve_kabul_metinleri_alir()
    {
        // İstek önceden ilişki gerektirmeden herkese gidebiliyor; ad serbest metin. Ders
        // metinleri engelli/engelsiz vakada aynı kalmalı: ad parametresi almayan bir fonksiyon
        // aynı girdiyle iki farklı metin üretemez.
        var adAlanlar = MetinFonksiyonlari()
            .Where(m => m.GetParameters().Any(p => p.Name!.Contains("Adi", StringComparison.OrdinalIgnoreCase)))
            .Select(m => m.Name)
            .OrderBy(x => x)
            .ToList();

        Assert.Equal(new[] { nameof(BildirimMetni.IstekKabul), nameof(BildirimMetni.YeniMesaj) }, adAlanlar);
    }

    [Fact]
    public void Hicbir_metin_mesaj_icerigi_alamaz()
    {
        var yasak = new[] { "icerik", "content", "mesaj", "metin", "govde" };
        foreach (var m in MetinFonksiyonlari())
        {
            foreach (var p in m.GetParameters())
            {
                Assert.DoesNotContain(yasak, y => p.Name!.Equals(y, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public void Ders_ve_istek_metinleri_yalnizca_konu_sure_ve_bayrak_alir()
    {
        var izinli = new[] { typeof(string), typeof(TimeSpan), typeof(bool), typeof(int) };
        foreach (var ad in new[] { nameof(BildirimMetni.YeniIstek), nameof(BildirimMetni.OnayBekliyor), nameof(BildirimMetni.OtoOnayYaklasiyor),
                     nameof(BildirimMetni.DersPlanlandi), nameof(BildirimMetni.DersIptal), nameof(BildirimMetni.DersYaklasiyor), nameof(BildirimMetni.IstekDusecek) })
        {
            var m = typeof(BildirimMetni).GetMethod(ad)!;
            Assert.All(m.GetParameters(), p =>
            {
                Assert.Contains(p.ParameterType, izinli);
                if (p.ParameterType == typeof(string))
                {
                    Assert.Equal("konu", p.Name);
                }
            });
        }
    }

    [Fact]
    public void Basliklar_sabit_ad_basliga_girmez()
    {
        foreach (var ad in new[] { "Ayşe", "Zeynep Nur", "Ali\nVeli" })
        {
            Assert.Equal("Yeni mesaj", BildirimMetni.YeniMesaj(ad, 2).Baslik);
            Assert.Equal("Arkadaş isteğin kabul edildi", BildirimMetni.IstekKabul(ad).Baslik);
        }
    }

    // ─── Ad temizleyici ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("Ayşe Yılmaz", "Ayşe Yılmaz")]
    [InlineData("  Ayşe   Yılmaz  ", "Ayşe Yılmaz")]
    [InlineData("Ali\nVeli", "Ali Veli")]
    [InlineData("Ali\r\n\tVeli", "Ali Veli")]
    [InlineData("Ali\u00A0Veli", "Ali Veli")]       // NBSP
    [InlineData("Ali\u202Eilev", "Aliilev")]        // RLO: "evil.com" → "moc.live" oyunu
    [InlineData("\u2066Ali\u2069", "Ali")]          // LRI/PDI
    [InlineData("A\u200Bli", "Ali")]                // sıfır genişlikli boşluk
    [InlineData("M.Ali Kaya", "M.Ali Kaya")]         // kısaltma alan adı sayılmaz
    [InlineData("Resmiye", "Resmiye")]               // 'resmi' kökü bilerek rezerve değil
    public void Ad_temizlenir(string ham, string beklenen)
        => Assert.Equal(beklenen, BildirimMetni.AdTemizle(ham));

    [Theory]
    [InlineData("https://kotu.site")]
    [InlineData("www.kotu")]
    [InlineData("WWW.Kotu")]
    [InlineData("kanalim . com")]
    [InlineData("t.me/xyz")]
    [InlineData("dersmate Destek")]
    [InlineData("DERSMÂTE")]
    [InlineData("Ders Mate")]
    [InlineData("D.E.R.S.M.A.T.E")]
    [InlineData("\u0501ersmate")]                    // Kiril ԁ
    [InlineData("\uFF44\uFF45\uFF52\uFF53\uFF4D\uFF41\uFF54\uFF45")] // tam genişlik
    [InlineData("PeerLearn")]
    [InlineData("Yönetim Ekibi")]
    [InlineData("adm1n")]
    [InlineData("Moderatör")]
    [InlineData("GÜVENLİK")]
    [InlineData("Support")]
    public void Kotuye_kullanilabilir_ad_genel_metne_duser(string ham)
    {
        Assert.Null(BildirimMetni.AdTemizle(ham));
        Assert.Equal("Bir arkadaşın sana yeni bir mesaj gönderdi.", BildirimMetni.YeniMesaj(ham, 1).Govde);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u200B\u200E")]
    [InlineData("\u200D")]
    public void Bos_ya_da_gorunmez_ad_null(string? ham)
        => Assert.Null(BildirimMetni.AdTemizle(ham));

    [Fact]
    public void Yuz_karakterlik_ad_24_grafeme_kesilir()
    {
        var kesik = BildirimMetni.AdTemizle(new string('a', 100))!;
        Assert.Equal(BildirimMetni.AdEnFazla, new StringInfo(kesik).LengthInTextElements);
        Assert.EndsWith("…", kesik);
    }

    [Fact]
    public void Kesim_ZWJ_emojisini_bolmez()
    {
        const string aile = "\U0001F468\u200D\U0001F469\u200D\U0001F467"; // ZWJ ile tek emoji
        var kesik = BildirimMetni.AdTemizle(string.Concat(Enumerable.Repeat(aile, 30)))!;

        Assert.Equal(24, new StringInfo(kesik).LengthInTextElements);
        Assert.StartsWith(aile, kesik);
        Assert.Equal(string.Empty, kesik[..^1].Replace(aile, string.Empty));
    }

    [Fact]
    public void Kesim_birlesik_harfi_bolmez()
    {
        const string e = "e\u0301"; // e + birleşik akut
        var kesik = BildirimMetni.AdTemizle(string.Concat(Enumerable.Repeat(e, 30)))!;

        Assert.Equal(24, new StringInfo(kesik).LengthInTextElements);
        Assert.Equal(string.Empty, kesik[..^1].Replace(e, string.Empty));
    }

    [Fact]
    public void Uzun_konu_40_grafeme_bosluklar_tek()
    {
        var konu = BildirimMetni.KonuKisalt("Çok   uzun bir\tkonu adı ki kırk grafemi rahatlıkla aşsın ve kesilsin");
        Assert.Equal(BildirimMetni.KonuEnFazla, new StringInfo(konu).LengthInTextElements);
        Assert.DoesNotContain("  ", konu);
        Assert.EndsWith("…", konu);
    }

    [Fact]
    public void Govde_ve_baslik_sinirlari_asilmaz()
    {
        var uzunKonu = new string('x', 150);
        var metinler = new[]
        {
            BildirimMetni.YeniMesaj(new string('ğ', 100), 99),
            BildirimMetni.YeniIstek(uzunKonu),
            BildirimMetni.OnayBekliyor(uzunKonu, TimeSpan.FromHours(47), true),
            BildirimMetni.OtoOnayYaklasiyor(uzunKonu, TimeSpan.FromHours(2)),
            BildirimMetni.DersPlanlandi(uzunKonu, TimeSpan.FromDays(9)),
        };
        Assert.All(metinler, m =>
        {
            Assert.True(new StringInfo(m.Baslik).LengthInTextElements <= 50);
            Assert.True(new StringInfo(m.Govde).LengthInTextElements <= 150);
        });
    }

    // ─── Göreli süreler ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(4.99, "birkaç dakika sonra")]
    [InlineData(5, "5 dakika sonra")]
    [InlineData(7, "5 dakika sonra")]
    [InlineData(8, "10 dakika sonra")]
    [InlineData(54, "55 dakika sonra")]
    [InlineData(55, "1 saat sonra")]
    [InlineData(90, "2 saat sonra")]
    [InlineData(47 * 60, "47 saat sonra")]
    [InlineData(48 * 60, "2 gün sonra")]
    [InlineData(3.2 * 24 * 60, "3 gün sonra")]
    public void Baslama_suresi(double dakika, string beklenen)
        => Assert.Equal(beklenen, BildirimMetni.BaslamaSuresi(TimeSpan.FromMinutes(dakika)));

    [Theory]
    [InlineData(47.99 * 60, "47 saat")]
    [InlineData(60, "1 saat")]
    [InlineData(59.9, "59 dakika")]
    [InlineData(0.33, "1 dakika")]
    public void Son_sure_asagi_yuvarlanir(double dakika, string beklenen)
        => Assert.Equal(beklenen, BildirimMetni.SureIcinde(TimeSpan.FromMinutes(dakika)));

    [Theory]
    [InlineData(54, "55 dakika")]
    [InlineData(55, "1 saat")]
    [InlineData(118, "2 saat")]
    public void Yaklasik_sure(double dakika, string beklenen)
        => Assert.Equal(beklenen, BildirimMetni.YaklasikSure(TimeSpan.FromMinutes(dakika)));

    /// <summary>
    /// Takvim sözcüğü ve mutlak saat hiçbir çıktıda yok: kullanıcının saat dilimi bilinmiyor
    /// ve bildirim gecikmeli okunabiliyor. Başlangıç noktaları gece yarısının iki yanı.
    /// </summary>
    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(23 * 60 + 50)]  // "gece yarısına 10 dakika"
    [InlineData(24 * 60 + 10)]
    [InlineData(47 * 60 + 59)]
    public void Takvim_sozcugu_ve_mutlak_saat_hicbir_ciktida_yok(int baslangicDakika)
    {
        var yasak = new[] { "yarın", "bugün", "dün", "gece", "sabah", "akşam" };
        for (var dk = baslangicDakika; dk < baslangicDakika + 5 * 24 * 60; dk += 13)
        {
            var kalan = TimeSpan.FromMinutes(dk);
            var metinler = new[]
            {
                BildirimMetni.DersPlanlandi("X", kalan), BildirimMetni.DersYaklasiyor("X", kalan, 60),
                BildirimMetni.DersYaklasiyor("X", kalan, 10), BildirimMetni.OtoOnayYaklasiyor("X", kalan),
                BildirimMetni.OnayBekliyor("X", kalan, dk % 2 == 0), BildirimMetni.IstekDusecek(2, kalan),
                BildirimMetni.IstekDusecek(1, kalan),
            };

            foreach (var m in metinler)
            {
                var metin = m.Baslik + " " + m.Govde;
                Assert.DoesNotContain(yasak, y => metin.Contains(y, StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotMatch(@"\d{1,2}[:.]\d{2}", metin);
                Assert.DoesNotContain("-", metin); // negatif süre
            }
        }
    }
}
