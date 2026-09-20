using System.Text.RegularExpressions;
using PeerLearn.Application.Features.Community;
using Xunit;

namespace PeerLearn.UnitTests;

public class AvatarErisimTests
{
    private static readonly Guid Kullanici = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Avatar_yoksa_null_doner(string? anahtar)
    {
        Assert.Null(AvatarErisim.YolFor(Kullanici, anahtar));
    }

    [Fact]
    public void Yol_erisim_ucunu_isaret_eder_ve_sürumludur()
    {
        var yol = AvatarErisim.YolFor(Kullanici, "2026/09/abc.jpg");

        Assert.NotNull(yol);
        Assert.StartsWith($"/api/users/{Kullanici}/avatar?v=", yol);
    }

    [Fact]
    public void Ham_depo_anahtari_sizmaz()
    {
        var anahtar = "2026/09/3f2a9c7e-1b4d-4a2e-9c11-0d5e6f7a8b90.webp";

        var yol = AvatarErisim.YolFor(Kullanici, anahtar)!;

        // Ne yol biçimi ne uzantı ne de anahtarın herhangi bir parçası yanıta girmemeli.
        Assert.DoesNotContain(anahtar, yol);
        Assert.DoesNotContain("2026/09", yol);
        Assert.DoesNotContain(".webp", yol);
    }

    [Fact]
    public void Surum_damgasi_12_kucuk_hex_karakter()
    {
        var yol = AvatarErisim.YolFor(Kullanici, "2026/09/abc.jpg")!;
        var damga = yol.Split("v=")[1];

        Assert.Matches(new Regex("^[0-9a-f]{12}$"), damga);
    }

    [Fact]
    public void Ayni_anahtar_ayni_damga_farkli_anahtar_farkli_damga()
    {
        var a = AvatarErisim.YolFor(Kullanici, "2026/09/abc.jpg");
        var aTekrar = AvatarErisim.YolFor(Kullanici, "2026/09/abc.jpg");
        var b = AvatarErisim.YolFor(Kullanici, "2026/09/xyz.jpg");

        Assert.Equal(a, aTekrar);
        Assert.NotEqual(a, b);
    }
}
