using PeerLearn.Application.Features.Identity;
using Xunit;

namespace PeerLearn.UnitTests;

public class ParolaSifirlamaTests
{
    private static readonly DateTime Now = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BeklemeIcinde_hic_istek_yoksa_false()
        => Assert.False(ParolaSifirlama.BeklemeIcinde(null, Now));

    [Fact]
    public void BeklemeIcinde_pencere_icinde_true()
    {
        var son = Now.AddSeconds(-(ParolaSifirlama.BeklemeSaniye - 1));
        Assert.True(ParolaSifirlama.BeklemeIcinde(son, Now));
    }

    [Fact]
    public void BeklemeIcinde_pencere_tam_dolunca_false()
    {
        var son = Now.AddSeconds(-ParolaSifirlama.BeklemeSaniye);
        Assert.False(ParolaSifirlama.BeklemeIcinde(son, Now));
    }
}
