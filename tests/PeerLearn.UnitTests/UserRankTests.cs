using PeerLearn.Domain.Community;
using Xunit;

namespace PeerLearn.UnitTests;

/// <summary>
/// Unvan hesabı. Saf fonksiyon olduğu için burada test edilir; e2e tarafı yalnızca
/// sunucunun bu değeri DOĞRU ALANLARDA döndürdüğünü sınar.
/// </summary>
public class UserRankTests
{
    [Theory]
    [InlineData(0, "Çırak")]
    [InlineData(499, "Çırak")]
    [InlineData(500, "Öğretici")]
    [InlineData(999, "Öğretici")]
    [InlineData(1_000, "Uzman")]
    [InlineData(2_499, "Uzman")]
    [InlineData(2_500, "Usta")]
    [InlineData(4_999, "Usta")]
    [InlineData(5_000, "Mentor")]
    [InlineData(9_999, "Mentor")]
    [InlineData(10_000, "Üstat")]
    [InlineData(999_999, "Üstat")]
    public void Esikler_dogru_unvani_verir(int puan, string beklenen)
        => Assert.Equal(beklenen, UserRankCalculator.Hesapla(puan).Title);

    /// <summary>
    /// SINIR KURALI: alt sınır DAHİL, üst sınır HARİÇ.
    ///
    /// İstekteki "0–500 / 500–1.000" yazımı 500'ü iki unvana birden veriyordu. Kural
    /// tek bir yerde seçilmezse aynı kullanıcı profilde bir unvanla, listede başka bir
    /// unvanla görünürdü. Bu test o seçimi kilitler.
    /// </summary>
    [Fact]
    public void Sinir_degeri_ust_kademeye_aittir()
    {
        Assert.Equal("Çırak", UserRankCalculator.Hesapla(499).Title);
        Assert.Equal("Öğretici", UserRankCalculator.Hesapla(500).Title);
    }

    /// <summary>
    /// Bozuk veri kimseyi unvanından etmemeli: negatif puan 0 sayılır, istisna fırlatmaz.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Negatif_puan_en_alt_kademeye_duser(int puan)
    {
        var r = UserRankCalculator.Hesapla(puan);
        Assert.Equal(UserRank.Cirak, r.Rank);
        Assert.Equal(0, r.MinCredits);
    }

    /// <summary>
    /// İlerleme çubuğu bu alandan çiziliyor; en üst unvanda "sonraki" diye bir şey yok
    /// ve null dönmezse arayüz olmayan bir hedefe doğru çubuk çizerdi.
    /// </summary>
    [Fact]
    public void En_ust_unvanda_sonraki_esik_yoktur()
    {
        Assert.Null(UserRankCalculator.Hesapla(10_000).NextRankAt);
        Assert.Null(UserRankCalculator.Hesapla(50_000).NextRankAt);
    }

    [Theory]
    [InlineData(0, 500)]
    [InlineData(499, 500)]
    [InlineData(500, 1_000)]
    [InlineData(2_500, 5_000)]
    [InlineData(5_000, 10_000)]
    public void Sonraki_esik_bir_ust_kademenin_alt_siniridir(int puan, int beklenen)
        => Assert.Equal(beklenen, UserRankCalculator.Hesapla(puan).NextRankAt);

    /// <summary>
    /// Unvan MONOTONdur: puan arttıkça kademe asla gerilemez. Eşik tablosu elle
    /// düzenlenirken sıralaması bozulursa (ör. bir satır yanlış yere taşınırsa) bu test
    /// kırılır — tablo sıralı olduğu varsayımına dayanan tek koruma budur.
    /// </summary>
    [Fact]
    public void Puan_arttikca_unvan_gerilemez()
    {
        var oncekiKademe = -1;

        for (var puan = 0; puan <= 11_000; puan += 25)
        {
            var kademe = (int)UserRankCalculator.Hesapla(puan).Rank;
            Assert.True(kademe >= oncekiKademe,
                $"{puan} puanda unvan geriledi: {oncekiKademe} -> {kademe}");
            oncekiKademe = kademe;
        }
    }

    /// <summary>Her kademenin bir emojisi olmalı — arayüz boş bir simge basmamalı.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    [InlineData(1_000)]
    [InlineData(2_500)]
    [InlineData(5_000)]
    [InlineData(10_000)]
    public void Her_kademenin_emojisi_ve_adi_vardir(int puan)
    {
        var r = UserRankCalculator.Hesapla(puan);
        Assert.False(string.IsNullOrWhiteSpace(r.Emoji));
        Assert.False(string.IsNullOrWhiteSpace(r.Title));
    }
}
