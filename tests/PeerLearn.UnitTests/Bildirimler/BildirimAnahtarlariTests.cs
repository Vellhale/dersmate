using System.Globalization;
using PeerLearn.Application.Features.Communication.Bildirimler;
using Xunit;

namespace PeerLearn.UnitTests.Bildirimler;

/// <summary>
/// Tekilleştirme anahtarları (DedupeKey). UNIQUE(RecipientUserId, DedupeKey) mükerrere karşı
/// son hat; o hattın çalışması anahtarın her yerde AYNI metin olmasına bağlı.
/// </summary>
/// <remarks>
/// ⚠️ Biçim testleri bilerek BİREBİR metin karşılaştırıyor: biçimi değiştirmek, dağıtım
/// sırasında eski biçimle yazılmış satırları "başka olay" yapar ve aynı hatırlatma ikinci
/// kez gider. Bu testlerden biri kırılıyorsa önce göç planı gerekir, test güncellemesi değil.
/// </remarks>
public class BildirimAnahtarlariTests
{
    private static readonly Guid Id = Guid.Parse("11111111-2222-3333-4444-555555555555");

    /// <summary>10:00:00 UTC + 123456,7 µs: son tick hanesi (7) mikrosaniyeye kesilince düşmeli.</summary>
    private static readonly DateTime Damga = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567);

    [Fact]
    public void Olay_anahtarlarinin_bicimi_sabit()
    {
        var mikro = (Damga.Ticks / 10).ToString(CultureInfo.InvariantCulture);

        Assert.Equal("mesaj:11111111-2222-3333-4444-555555555555", BildirimAnahtarlari.Mesaj(Id));
        Assert.Equal("istek:11111111-2222-3333-4444-555555555555", BildirimAnahtarlari.Istek(Id));
        Assert.Equal("istekKabul:11111111-2222-3333-4444-555555555555", BildirimAnahtarlari.IstekKabul(Id));
        Assert.Equal("rezervasyon:11111111-2222-3333-4444-555555555555", BildirimAnahtarlari.Rezervasyon(Id));
        Assert.Equal("iptal:11111111-2222-3333-4444-555555555555", BildirimAnahtarlari.Iptal(Id));
        Assert.Equal("yaklasan:11111111-2222-3333-4444-555555555555:60", BildirimAnahtarlari.Yaklasan(Id, 60));
        Assert.Equal($"onay:11111111-2222-3333-4444-555555555555:{mikro}", BildirimAnahtarlari.Onay(Id, Damga));
        Assert.Equal($"otoOnay:11111111-2222-3333-4444-555555555555:{mikro}:24", BildirimAnahtarlari.OtoOnay(Id, Damga, 24));
        Assert.Equal("test:mesajlar:11111111222233334444555555555555", BildirimAnahtarlari.Test(BildirimKanallari.Mesajlar, Id));
    }

    [Fact]
    public void Ayni_olay_her_cagrida_ayni_anahtar()
    {
        Assert.Equal(BildirimAnahtarlari.Onay(Id, Damga), BildirimAnahtarlari.Onay(Id, Damga));
        Assert.Equal(BildirimAnahtarlari.Yaklasan(Id, 10), BildirimAnahtarlari.Yaklasan(Id, 10));
    }

    [Fact]
    public void Farkli_olaylar_ayni_kayitta_farkli_anahtar()
    {
        var anahtarlar = new[]
        {
            BildirimAnahtarlari.Mesaj(Id), BildirimAnahtarlari.Istek(Id), BildirimAnahtarlari.IstekKabul(Id),
            BildirimAnahtarlari.Rezervasyon(Id), BildirimAnahtarlari.Iptal(Id),
            BildirimAnahtarlari.Yaklasan(Id, 60), BildirimAnahtarlari.Yaklasan(Id, 10),
            BildirimAnahtarlari.Onay(Id, Damga), BildirimAnahtarlari.OtoOnay(Id, Damga, 24), BildirimAnahtarlari.OtoOnay(Id, Damga, 2),
        };
        Assert.Equal(anahtarlar.Length, anahtarlar.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Damga_mikrosaniyeye_kesilir_veritabanindan_okunan_deger_ayni_anahtari_verir()
    {
        // PostgreSQL mikrosaniye tutar, Npgsql son tick hanesini keser. Bellekteki değerle
        // DB'den dönen değer aynı anahtarı üretmeli; üretmeseydi aynı onay iki kez giderdi.
        var dbDegeri = Damga.AddTicks(-(Damga.Ticks % 10));
        Assert.NotEqual(Damga, dbDegeri);
        Assert.Equal(BildirimAnahtarlari.Onay(Id, Damga), BildirimAnahtarlari.Onay(Id, dbDegeri));
        Assert.Equal(BildirimAnahtarlari.OtoOnay(Id, Damga, 2), BildirimAnahtarlari.OtoOnay(Id, dbDegeri, 2));
    }

    [Fact]
    public void Bir_mikrosaniyelik_fark_yeni_anahtardir()
        => Assert.NotEqual(BildirimAnahtarlari.Damga(Damga), BildirimAnahtarlari.Damga(Damga.AddTicks(10)));

    [Fact]
    public void Damga_Kind_isaretinden_bagimsiz()
    {
        var belirsiz = DateTime.SpecifyKind(Damga, DateTimeKind.Unspecified);
        Assert.Equal(BildirimAnahtarlari.Damga(Damga), BildirimAnahtarlari.Damga(belirsiz));
    }

    [Theory]
    [InlineData("th-TH")] // Budist takvimi: yyyy 2569 olurdu
    [InlineData("ar-SA")]
    [InlineData("tr-TR")]
    public void Anahtarlar_kulturden_bagimsiz(string kultur)
    {
        var onceki = CultureInfo.CurrentCulture;
        var beklenenOzet = BildirimAnahtarlari.IstekDusecek(new DateTime(2026, 10, 5, 7, 0, 0, DateTimeKind.Utc));
        var beklenenOnay = BildirimAnahtarlari.OtoOnay(Id, Damga, 24);
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(kultur);
            Assert.Equal(beklenenOzet, BildirimAnahtarlari.IstekDusecek(new DateTime(2026, 10, 5, 7, 0, 0, DateTimeKind.Utc)));
            Assert.Equal(beklenenOnay, BildirimAnahtarlari.OtoOnay(Id, Damga, 24));
            Assert.Equal("istekDusecek:20261005", beklenenOzet);
        }
        finally
        {
            CultureInfo.CurrentCulture = onceki;
        }
    }

    [Fact]
    public void Ozet_anahtari_TR_takvim_gunune_gore()
    {
        // 21:30 UTC = ertesi gün 00:30 TR.
        Assert.Equal("istekDusecek:20261006", BildirimAnahtarlari.IstekDusecek(new DateTime(2026, 10, 5, 21, 30, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void En_uzun_anahtar_sutun_sinirinin_altinda()
    {
        // DedupeKey varchar(160).
        Assert.True(BildirimAnahtarlari.OtoOnay(Id, DateTime.MaxValue, 24).Length <= 160);
        Assert.True(BildirimAnahtarlari.Test(BildirimKanallari.DersPlani, Id).Length <= 160);
    }

    [Theory]
    [InlineData(BildirimKanallari.Mesajlar)]
    [InlineData(BildirimKanallari.Istekler)]
    [InlineData(BildirimKanallari.DersOnayi)]
    [InlineData(BildirimKanallari.DersPlani)]
    public void Test_anahtarindaki_kanal_geri_okunur(string kanal)
        => Assert.Equal(kanal, BildirimAnahtarlari.TestKanali(BildirimAnahtarlari.Test(kanal, Guid.NewGuid())));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("test:")]
    [InlineData("test:mesajlar")]            // ayraç yok
    [InlineData("test:bilinmez:abc")]        // tanınmayan kanal
    [InlineData("test:Mesajlar:abc")]        // büyük/küçük harf duyarlı
    [InlineData("mesaj:mesajlar:abc")]       // test anahtarı değil
    public void Test_kanali_okunamayan_anahtarda_null(string? anahtar)
        => Assert.Null(BildirimAnahtarlari.TestKanali(anahtar));
}
