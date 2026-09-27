using PeerLearn.Application.Features.Communication.Bildirimler;
using Xunit;

namespace PeerLearn.UnitTests.Bildirimler;

/// <summary>
/// Expo token biçimi ve maskesi. Token o telefona bildirim göndermenin tek anahtarı:
/// günlüğe ya da LastError'a tam hâliyle düşmesi geri alınamaz (yedeklere gider).
/// </summary>
public class PushTokenKuraliTests
{
    private const string Ic = "abcdefghijklmnopqrstuv"; // 22 karakter, gerçek token boyu
    private const string Token = "ExponentPushToken[" + Ic + "]";

    [Theory]
    [InlineData("ExponentPushToken[abcdefghijklmnopqrstuv]")]
    [InlineData("ExpoPushToken[abcdefghijklmnopqrstuv]")]   // eski biçim
    [InlineData("ExponentPushToken[a_b-c9]")]
    [InlineData("ExponentPushToken[abc123OLU]")]            // Log sağlayıcısının test kancaları kayıttan geçmeli
    [InlineData("ExponentPushToken[abc123YABANCI]")]
    [InlineData("ExponentPushToken[abc123YAVAS]")]
    public void Gecerli_bicimler(string token) => Assert.True(PushTokenKurali.Gecerli(token));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ExponentPushToken[]")]
    [InlineData("ExponentPushToken[abc def]")]
    [InlineData("ExponentPushToken[abc]]")]
    [InlineData(" ExponentPushToken[abc]")]
    [InlineData("exponentpushtoken[abc]")]
    [InlineData("fcm:ham-firebase-tokeni")]
    [InlineData("ExponentPushToken[abc\"]")]
    public void Gecersiz_bicimler(string? token) => Assert.False(PushTokenKurali.Gecerli(token));

    [Fact]
    public void Uzunluk_siniri_kolonla_ayni()
    {
        var tam = "ExponentPushToken[" + new string('a', PushTokenKurali.EnFazlaUzunluk - 19) + "]";
        Assert.Equal(PushTokenKurali.EnFazlaUzunluk, tam.Length);
        Assert.True(PushTokenKurali.Gecerli(tam));
        Assert.False(PushTokenKurali.Gecerli(tam.Insert(18, "a")));
    }

    [Fact]
    public void Maske_yalnizca_son_alti_karakteri_gosterir()
    {
        Assert.Equal("ExponentPushToken[…qrstuv]", PushTokenKurali.Maskele(Token));
        Assert.DoesNotContain("abcdefghijklmnop", PushTokenKurali.Maskele(Token));
    }

    [Theory]
    [InlineData("ExponentPushToken[abcdef]", "ExponentPushToken[…]")]   // kısa: son altı karakter tamamını ele verirdi
    [InlineData("ExponentPushToken[abcdefghijkl]", "ExponentPushToken[…]")]
    [InlineData("biçimsiz-uzun-bir-değer-123456", "…123456")]
    [InlineData("kisa", "…")]
    [InlineData(null, "(yok)")]
    [InlineData("", "(yok)")]
    public void Maske_kisa_ve_bicimsiz_degerler(string? token, string beklenen)
        => Assert.Equal(beklenen, PushTokenKurali.Maskele(token));

    [Fact]
    public void Metnin_icindeki_her_token_maskelenir()
    {
        var metin = $"\"{Token}\" is not a registered push notification recipient; also ExpoPushToken[zzzzzzzzzzzzzzzzzz123456]";
        var maskeli = PushTokenKurali.MetniMaskele(metin)!;

        Assert.DoesNotContain(Ic, maskeli);
        Assert.DoesNotContain("zzzzzzzzzzzz", maskeli);
        Assert.Contains("ExponentPushToken[…qrstuv]", maskeli);
        Assert.Contains("ExpoPushToken[…123456]", maskeli);
        Assert.Contains("is not a registered push notification recipient", maskeli);
    }

    [Fact]
    public void Metin_maskesi_gevsek_bicimsiz_token_da_maskelenir()
    {
        // Katı desen boşluklu iç kısmı eşleştirmezdi ve token olduğu gibi günlüğe düşerdi.
        var maskeli = PushTokenKurali.MetniMaskele("hata: ExponentPushToken[abc def ghi jkl mno] reddedildi")!;
        Assert.DoesNotContain("abc def ghi", maskeli);
        Assert.EndsWith("reddedildi", maskeli);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("token içermeyen metin")]
    public void Tokensiz_metin_degismez(string? metin)
        => Assert.Equal(metin, PushTokenKurali.MetniMaskele(metin));
}
