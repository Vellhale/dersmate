using PeerLearn.Domain.Community;
using Xunit;

namespace PeerLearn.UnitTests;

/// <summary>
/// Seviye hesabı (1..10, krediden türer). Saf fonksiyon olduğu için burada sınanır;
/// e2e tarafı yalnızca sunucunun bu değeri DOĞRU ALANLARDA döndürdüğünü kontrol eder.
/// </summary>
public class UserLevelTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 2)]
    [InlineData(199, 2)]
    [InlineData(200, 3)]
    [InlineData(349, 3)]
    [InlineData(350, 4)]
    [InlineData(599, 4)]
    [InlineData(600, 5)]
    [InlineData(999, 5)]
    [InlineData(1_000, 6)]
    [InlineData(1_749, 6)]
    [InlineData(1_750, 7)]
    [InlineData(2_999, 7)]
    [InlineData(3_000, 8)]
    [InlineData(5_499, 8)]
    [InlineData(5_500, 9)]
    [InlineData(9_999, 9)]
    [InlineData(10_000, 10)]
    [InlineData(999_999, 10)]
    public void Esikler_dogru_seviyeyi_verir(int puan, int beklenen)
        => Assert.Equal(beklenen, UserLevelRules.Hesapla(puan).Level);

    /// <summary>
    /// SINIR KURALI: alt sınır DAHİL, üst sınır HARİÇ. 100 kredi 2. seviyedir.
    ///
    /// Kural tek bir yerde seçilmezse aynı kullanıcı profilde bir seviye, başlıktaki
    /// rozette başka bir seviye görünür — ve hangisinin doğru olduğu anlaşılmaz.
    /// </summary>
    [Fact]
    public void Sinir_degeri_ust_basamaga_aittir()
    {
        Assert.Equal(1, UserLevelRules.Hesapla(99).Level);
        Assert.Equal(2, UserLevelRules.Hesapla(100).Level);
    }

    /// <summary>Bozuk veri kimseyi seviyesinden etmemeli: negatif puan 1. seviyedir, istisna değil.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Negatif_puan_en_alt_basamaga_duser(int puan)
    {
        var s = UserLevelRules.Hesapla(puan);
        Assert.Equal(1, s.Level);
        Assert.Equal(0, s.MinCredits);
    }

    /// <summary>
    /// İlerleme satırı bu alandan çiziliyor; en üst seviyede "sonraki" diye bir şey yok
    /// ve null dönmezse arayüz olmayan bir hedefe doğru ilerleme gösterirdi.
    /// </summary>
    [Fact]
    public void En_ust_seviyede_sonraki_esik_yoktur()
    {
        Assert.Null(UserLevelRules.Hesapla(10_000).NextLevelAt);
        Assert.Null(UserLevelRules.Hesapla(50_000).NextLevelAt);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(99, 100)]
    [InlineData(100, 200)]
    [InlineData(600, 1_000)]
    [InlineData(5_500, 10_000)]
    public void Sonraki_esik_bir_ust_basamagin_alt_siniridir(int puan, int beklenen)
        => Assert.Equal(beklenen, UserLevelRules.Hesapla(puan).NextLevelAt);

    /// <summary>
    /// Seviye MONOTONdur: puan arttıkça asla gerilemez. Eşik tablosu elle düzenlenirken
    /// sıralaması bozulursa (ör. bir satır yanlış yere taşınırsa) bu test kırılır —
    /// tablonun sıralı olduğu varsayımına dayanan tek koruma budur.
    /// </summary>
    [Fact]
    public void Puan_arttikca_seviye_gerilemez()
    {
        var onceki = 0;

        for (var puan = 0; puan <= 11_000; puan += 25)
        {
            var seviye = UserLevelRules.Hesapla(puan).Level;
            Assert.True(seviye >= onceki, $"{puan} puanda seviye geriledi: {onceki} -> {seviye}");
            onceki = seviye;
        }
    }

    /// <summary>
    /// Seviye HER ZAMAN 1..MaxLevel aralığında. Eşik tablosuna bir satır eklenip
    /// <see cref="UserLevelRules.MaxLevel"/> güncellenmezse arayüz "11 / 10" gibi
    /// anlamsız bir bağlam gösterirdi; sabit ile tablo bu testle birbirine bağlı.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(12_345)]
    [InlineData(int.MaxValue)]
    public void Seviye_daima_gecerli_araliktadir(int puan)
    {
        var seviye = UserLevelRules.Hesapla(puan).Level;
        Assert.InRange(seviye, 1, UserLevelRules.MaxLevel);
    }

    /// <summary>
    /// En üst seviye GERÇEKTEN ulaşılabilir olmalı. MaxLevel'i tabloyu büyütmeden
    /// artırmak, kimsenin varamayacağı bir basamak yaratırdı ve rozet sonsuza kadar
    /// "9 / 10" derdi.
    /// </summary>
    [Fact]
    public void En_ust_seviyeye_ulasilabiliyor()
        => Assert.Equal(UserLevelRules.MaxLevel, UserLevelRules.Hesapla(int.MaxValue).Level);

    /// <summary>
    /// <see cref="UserLevelRules.MinCreditsFor"/> ile <see cref="UserLevelRules.Hesapla"/>
    /// aynı tabloyu okumalı: eşikten hesaplanan seviye, o eşiğin seviyesi olmalı.
    /// İkisi ayrışırsa tohumlama betikleri "9. seviye kullanıcı" üretip 8 gösterirdi.
    /// </summary>
    [Fact]
    public void MinCreditsFor_ve_Hesapla_ayni_tabloyu_okur()
    {
        for (var seviye = 1; seviye <= UserLevelRules.MaxLevel; seviye++)
        {
            var esik = UserLevelRules.MinCreditsFor(seviye);
            Assert.Equal(seviye, UserLevelRules.Hesapla(esik).Level);
            Assert.Equal(esik, UserLevelRules.Hesapla(esik).MinCredits);

            // Eşiğin bir altı MUTLAKA bir alt basamak olmalı (1. seviye hariç: altı yok).
            if (seviye > 1) Assert.Equal(seviye - 1, UserLevelRules.Hesapla(esik - 1).Level);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(-3)]
    public void MinCreditsFor_gecersiz_seviyede_hata_verir(int seviye)
        => Assert.Throws<ArgumentOutOfRangeException>(() => UserLevelRules.MinCreditsFor(seviye));
}
