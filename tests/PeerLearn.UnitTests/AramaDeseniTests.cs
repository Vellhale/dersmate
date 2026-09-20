using PeerLearn.Application.Common;
using Xunit;

namespace PeerLearn.UnitTests;

public class AramaDeseniTests
{
    [Fact]
    public void Duz_metin_degismeden_desene_sarilir()
    {
        Assert.Equal("%matematik%", AramaDeseni.Iceren("matematik"));
    }

    [Theory]
    [InlineData("%", "\\%")]
    [InlineData("_", "\\_")]
    [InlineData("\\", "\\\\")]
    [InlineData("100%_indirim", "100\\%\\_indirim")]
    public void Joker_karakterler_kacislanir(string ham, string beklenen)
    {
        Assert.Equal(beklenen, AramaDeseni.Kacisla(ham));
    }

    [Fact]
    public void Ters_bolu_once_kacislanir_eklenen_kacislar_bozulmaz()
    {
        // "\%" → önce \ ikilenir ("\\%"), sonra % kaçışlanır ("\\\%").
        // Yanlış sırada olsaydı % için eklenen \ tekrar ikilenir ve desen bozulurdu.
        Assert.Equal("\\\\\\%", AramaDeseni.Kacisla("\\%"));
    }

    [Fact]
    public void Iceren_kacislanmis_terimi_yuzde_ile_sarar()
    {
        Assert.Equal("%50\\%%", AramaDeseni.Iceren("50%"));
    }

    [Fact]
    public void Kacis_karakteri_tek_ters_bolu()
    {
        Assert.Equal("\\", AramaDeseni.KacisKarakteri);
    }
}
