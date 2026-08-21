using PeerLearn.Domain.Community;
using Xunit;

namespace PeerLearn.UnitTests;

/// <summary>
/// Seviye hesabı. Saf fonksiyon olduğu için burada test edilir; e2e tarafı yalnızca
/// sunucunun bu değeri DOĞRU ALANLARDA döndürdüğünü sınar.
/// </summary>
/// <remarks>
/// 2026-08-21'de unvan adları (Çırak / Öğretici / … / Üstat) yerine 1–10 seviye geldi.
/// Eşikler: 0, 200, 500, 1.000, 2.000, 3.500, 6.000, 10.000, 16.000, 25.000.
/// </remarks>
public class UserLevelTests
{
    [Theory]
    [InlineData(0, "1. Seviye")]
    [InlineData(199, "1. Seviye")]
    [InlineData(200, "2. Seviye")]
    [InlineData(499, "2. Seviye")]
    [InlineData(500, "3. Seviye")]
    [InlineData(999, "3. Seviye")]
    [InlineData(1_000, "4. Seviye")]
    [InlineData(1_999, "4. Seviye")]
    [InlineData(2_000, "5. Seviye")]
    [InlineData(3_499, "5. Seviye")]
    [InlineData(3_500, "6. Seviye")]
    [InlineData(5_999, "6. Seviye")]
    [InlineData(6_000, "7. Seviye")]
    [InlineData(9_999, "7. Seviye")]
    [InlineData(10_000, "8. Seviye")]
    [InlineData(15_999, "8. Seviye")]
    [InlineData(16_000, "9. Seviye")]
    [InlineData(24_999, "9. Seviye")]
    [InlineData(25_000, "10. Seviye")]
    [InlineData(999_999, "10. Seviye")]
    public void Esikler_dogru_seviyeyi_verir(int puan, string beklenen)
        => Assert.Equal(beklenen, UserLevelCalculator.Hesapla(puan).Title);

    /// <summary>
    /// SINIR KURALI: alt sınır DAHİL, üst sınır HARİÇ. Kural tek bir yerde seçilmezse
    /// aynı kullanıcı profilde bir seviyede, listede başka bir seviyede görünürdü.
    /// </summary>
    [Fact]
    public void Sinir_degeri_ust_kademeye_aittir()
    {
        Assert.Equal(1, UserLevelCalculator.Hesapla(199).Level);
        Assert.Equal(2, UserLevelCalculator.Hesapla(200).Level);
        Assert.Equal(9, UserLevelCalculator.Hesapla(24_999).Level);
        Assert.Equal(10, UserLevelCalculator.Hesapla(25_000).Level);
    }

    /// <summary>
    /// YÜKSELMEK GİTTİKÇE ZORLAŞIR — ürün kararı, testle kilitleniyor.
    /// Kademeler arası fark her adımda BÜYÜMELİ. Eşik tablosuna bir gün "kolay" bir satır
    /// eklenirse (ör. 25.000'den sonra 25.500) bu test kırılır.
    /// </summary>
    [Fact]
    public void Kademeler_arasi_fark_her_adimda_buyur()
    {
        var esikler = new List<int>();
        for (var seviye = 1; seviye <= UserLevelCalculator.MaxLevel; seviye++)
        {
            // Her seviyenin alt sınırını, o seviyeye ait bir puandan geri okuyoruz.
            var oncekiSonraki = seviye == 1
                ? 0
                : UserLevelCalculator.Hesapla(esikler[^1]).NextLevelAt!.Value;
            esikler.Add(oncekiSonraki);
        }

        var oncekiFark = 0;
        for (var i = 1; i < esikler.Count; i++)
        {
            var fark = esikler[i] - esikler[i - 1];
            Assert.True(fark > oncekiFark,
                $"{i + 1}. seviyeye çıkış {i}. seviyeden kolay ya da eşit: {oncekiFark} -> {fark}");
            oncekiFark = fark;
        }
    }

    /// <summary>Seviye sayısı üründe söz verildiği gibi 10 olmalı.</summary>
    [Fact]
    public void En_ust_seviye_ondur()
    {
        Assert.Equal(10, UserLevelCalculator.MaxLevel);
        Assert.Equal(10, UserLevelCalculator.Hesapla(int.MaxValue).Level);
    }

    /// <summary>
    /// Bozuk veri kimseyi seviyesinden etmemeli: negatif puan 0 sayılır, istisna fırlatmaz.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Negatif_puan_en_alt_kademeye_duser(int puan)
    {
        var r = UserLevelCalculator.Hesapla(puan);
        Assert.Equal(1, r.Level);
        Assert.Equal(0, r.MinCredits);
    }

    /// <summary>
    /// İlerleme çubuğu bu alandan çiziliyor; en üst seviyede "sonraki" diye bir şey yok
    /// ve null dönmezse arayüz olmayan bir hedefe doğru çubuk çizerdi.
    /// </summary>
    [Fact]
    public void En_ust_seviyede_sonraki_esik_yoktur()
    {
        Assert.Null(UserLevelCalculator.Hesapla(25_000).NextLevelAt);
        Assert.Null(UserLevelCalculator.Hesapla(500_000).NextLevelAt);
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(199, 200)]
    [InlineData(200, 500)]
    [InlineData(500, 1_000)]
    [InlineData(2_000, 3_500)]
    [InlineData(16_000, 25_000)]
    public void Sonraki_esik_bir_ust_kademenin_alt_siniridir(int puan, int beklenen)
        => Assert.Equal(beklenen, UserLevelCalculator.Hesapla(puan).NextLevelAt);

    /// <summary>
    /// Seviye MONOTONdur: puan arttıkça kademe asla gerilemez. Eşik tablosu elle
    /// düzenlenirken sıralaması bozulursa (ör. bir satır yanlış yere taşınırsa) bu test
    /// kırılır — tablo sıralı olduğu varsayımına dayanan tek koruma budur.
    /// </summary>
    [Fact]
    public void Puan_arttikca_seviye_gerilemez()
    {
        var oncekiSeviye = 0;

        for (var puan = 0; puan <= 26_000; puan += 25)
        {
            var seviye = UserLevelCalculator.Hesapla(puan).Level;
            Assert.True(seviye >= oncekiSeviye,
                $"{puan} puanda seviye geriledi: {oncekiSeviye} -> {seviye}");
            oncekiSeviye = seviye;
        }
    }

    /// <summary>Her kademenin bir emojisi ve adı olmalı — arayüz boş bir simge basmamalı.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    [InlineData(500)]
    [InlineData(2_000)]
    [InlineData(6_000)]
    [InlineData(16_000)]
    [InlineData(25_000)]
    public void Her_kademenin_emojisi_ve_adi_vardir(int puan)
    {
        var r = UserLevelCalculator.Hesapla(puan);
        Assert.False(string.IsNullOrWhiteSpace(r.Emoji));
        Assert.False(string.IsNullOrWhiteSpace(r.Title));
    }
}
