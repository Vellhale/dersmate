using PeerLearn.Application.Common;
using Xunit;

namespace PeerLearn.UnitTests;

public class AramaGirdisiTests
{
    [Fact]
    public void Dogrula_NUL_baytini_reddeder()
    {
        var ex = Assert.Throws<AppException>(() => AramaGirdisi.Dogrula("ab\0cd"));
        Assert.Equal(ErrorCodes.ValidationFailed, ex.Code);
        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\t")]
    public void Dogrula_kontrol_karakterlerini_reddeder(string kontrol)
        => Assert.Throws<AppException>(() => AramaGirdisi.Dogrula("x" + kontrol + "y"));

    [Fact]
    public void Dogrula_normal_metni_bos_ve_null_gecirir()
        => AramaGirdisi.Dogrula("matematik", null, "Bogazici Universitesi", "");

    [Fact]
    public void Dogrula_alanlardan_birinde_kontrol_karakteri_varsa_reddeder()
        => Assert.Throws<AppException>(() => AramaGirdisi.Dogrula("temiz", "kirli\0"));
}
