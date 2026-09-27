using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Features.Communication.Bildirimler;
using PeerLearn.Application.Options;
using Xunit;

namespace PeerLearn.UnitTests.Bildirimler;

/// <summary>
/// Opak bildirim etiketleri (Android tag, iOS collapseId/threadId, data.alici).
/// </summary>
/// <remarks>
/// Etiket değişirse cihazdaki eski bildirim yenisiyle YER DEĞİŞTİRMEZ ve mobilin tuttuğu
/// alıcı etiketi uyuşmaz (bildirimler "başka hesabın" sanılıp yok sayılır). Bu yüzden
/// türetme bağımsız bir hesapla kilitleniyor: HKDF bilgisi ya da HMAC girdisinin biçimi
/// sessizce değişemez.
/// </remarks>
public class BildirimEtiketiTests
{
    private const string Anahtar = "birim-testi-anahtari-en-az-otuz-iki-karakter-uzun";
    private static readonly Guid Kimlik = Guid.Parse("9b2f5e0c-3d41-4a6b-8c7d-1e2f3a4b5c6d");
    private static readonly BildirimEtiketi Etiket = new(Anahtar);

    [Fact]
    public void Ayni_girdi_ayni_etiket()
    {
        Assert.Equal(Etiket.Alici(Kimlik), new BildirimEtiketi(Anahtar).Alici(Kimlik));
        Assert.Equal(Etiket.Ders(Kimlik), Etiket.Ders(Kimlik));
    }

    [Fact]
    public void Etiketler_16_hex_kucuk_harf_ve_onekli()
    {
        Assert.Matches("^[0-9a-f]{16}$", Etiket.Alici(Kimlik));
        Assert.Matches("^s-[0-9a-f]{16}$", Etiket.Sohbet(Kimlik));
        Assert.Matches("^i-[0-9a-f]{16}$", Etiket.IstekCifti(Kimlik, Guid.Empty));
        Assert.Matches("^id-[0-9a-f]{16}$", Etiket.IstekOzeti(Kimlik));
        Assert.Matches("^d-[0-9a-f]{16}$", Etiket.Ders(Kimlik));
        Assert.Matches("^t-[0-9a-f]{16}$", Etiket.Test(Kimlik));
    }

    [Fact]
    public void Ayni_kimlik_turler_arasinda_farkli_etiket()
    {
        // Alan ayrımı: öneki soyulmuş hâlleri de farklı olmalı (tür HMAC girdisinin parçası).
        var govdeler = new[]
        {
            Etiket.Alici(Kimlik), Etiket.Sohbet(Kimlik)[2..], Etiket.IstekOzeti(Kimlik)[3..],
            Etiket.Ders(Kimlik)[2..], Etiket.Test(Kimlik)[2..],
        };
        Assert.Equal(govdeler.Length, govdeler.Distinct().Count());
    }

    [Fact]
    public void Istek_ciftinde_yon_onemli()
        => Assert.NotEqual(Etiket.IstekCifti(Kimlik, Guid.Empty), Etiket.IstekCifti(Guid.Empty, Kimlik));

    [Fact]
    public void Etiket_kimligi_tasimaz()
    {
        var parcalar = Kimlik.ToString("N").Chunk(8).Select(c => new string(c));
        foreach (var e in new[] { Etiket.Alici(Kimlik), Etiket.Sohbet(Kimlik), Etiket.Ders(Kimlik) })
        {
            Assert.All(parcalar, p => Assert.DoesNotContain(p, e));
        }
    }

    [Fact]
    public void Etiket_anahtara_bagli()
    {
        var baska = new BildirimEtiketi("baska-bir-anahtar-en-az-otuz-iki-karakter-uzunluk");
        Assert.NotEqual(Etiket.Alici(Kimlik), baska.Alici(Kimlik));
        Assert.NotEqual(Etiket.Sohbet(Kimlik), baska.Sohbet(Kimlik));
    }

    [Fact]
    public void Anahtar_Jwt_Key_den_HKDF_ile_turer()
    {
        // Bağımsız hesap: HKDF-SHA256(Jwt:Key, salt yok, info "dersmate-push-etiket-v1", 32 bayt),
        // sonra HMAC-SHA256("alici|<guid D>"), ilk 8 bayt hex.
        var turetilmis = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(Anahtar), 32, [],
            Encoding.UTF8.GetBytes("dersmate-push-etiket-v1"));
        var ozet = HMACSHA256.HashData(turetilmis, Encoding.UTF8.GetBytes("alici|" + Kimlik.ToString("D")));
        var beklenen = Convert.ToHexString(ozet, 0, 8).ToLowerInvariant();

        Assert.Equal(beklenen, Etiket.Alici(Kimlik));

        // JWT imza anahtarıyla DOĞRUDAN HMAC yapılmıyor (HKDF ayrımı).
        var dogrudan = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Anahtar),
            Encoding.UTF8.GetBytes("alici|" + Kimlik.ToString("D"))), 0, 8).ToLowerInvariant();
        Assert.NotEqual(dogrudan, Etiket.Alici(Kimlik));
    }

    [Fact]
    public void DI_kurucusu_JwtOptions_Key_i_kullanir()
    {
        var di = new BildirimEtiketi(Options.Create(new JwtOptions { Key = Anahtar }));
        Assert.Equal(Etiket.Alici(Kimlik), di.Alici(Kimlik));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Bos_anahtarla_kurulmaz(string? anahtar)
        => Assert.Throws<InvalidOperationException>(() => new BildirimEtiketi(anahtar!));
}
