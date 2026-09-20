using PeerLearn.Domain.Community;
using Xunit;

namespace PeerLearn.UnitTests;

public class CommunityOduluTavaniTests
{
    /// <summary>
    /// Tavan, tek ödülün katı olmalı: pencere sayımı da (fark basımları) daima
    /// CreditsPerReward'ın katı olduğu için eşik tam bir ödül sınırına denk gelsin.
    /// Yapısal iddia (değer değil kural): oran değişse bile katsayı korunur.
    /// </summary>
    [Fact]
    public void Tavan_bes_odule_karsilik_gelir()
    {
        Assert.Equal(5 * CommunityRewardRules.CreditsPerReward, CommunityRewardRules.MaxRewardCreditsPerDay);
    }

    [Theory]
    [InlineData(0, false)]                 // hiç basılmamış: serbest
    [InlineData(400, false)]               // tavanın altında: serbest
    public void Tavan_altinda_basim_serbest(int penceredeBasilan, bool beklenen)
    {
        Assert.Equal(beklenen, CommunityRewardRules.GunlukTavanAsildi(penceredeBasilan));
    }

    [Fact]
    public void Tavan_esiginde_ve_ustunde_atlanir()
    {
        // Sınır DAHİL: tam tavan kadar basılmışsa bu tur atlanmalı (>= karşılaştırması).
        Assert.True(CommunityRewardRules.GunlukTavanAsildi(CommunityRewardRules.MaxRewardCreditsPerDay));
        Assert.True(CommunityRewardRules.GunlukTavanAsildi(CommunityRewardRules.MaxRewardCreditsPerDay + 100));
    }

    [Fact]
    public void Tavan_bir_puan_altinda_hala_serbest()
    {
        // Eşik testinin ikizi: sınırın hemen altı geçmeli, hemen üstü/eşiti geçmemeli.
        Assert.False(CommunityRewardRules.GunlukTavanAsildi(CommunityRewardRules.MaxRewardCreditsPerDay - 1));
    }
}
