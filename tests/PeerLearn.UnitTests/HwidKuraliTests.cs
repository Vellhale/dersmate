using System.Reflection;
using PeerLearn.Application.Features.Identity;
using PeerLearn.Application.Identity;
using Xunit;

namespace PeerLearn.UnitTests;

/// <summary>
/// Cihaz özetinin (HWID) tek biçimlendirme kuralı. Push'un oturum bağı
/// ("bu cihazın en yeni token'ı aktif mi") RefreshTokens.DeviceHwidHash ile
/// PushDevices.HwidHash'in EŞİTLİĞİNE dayanıyor; giriş, yenileme ve kayıt ucu aynı
/// fonksiyonu kullanmazsa eşitlik sessizce bozulur ve hiçbir cihaz bağlı sayılmaz.
/// </summary>
/// <remarks>
/// İki tür kanıt: (1) çıktı, ortak fonksiyona geçmeden önce Login.cs ve RefreshSession.cs'te
/// duran kopyalarla BİREBİR aynı (veritabanındaki HWID'ler ve banlar o çıktıyla yazıldı);
/// (2) yapısal: iki handler'da kendi Normalize kopyası YOK ve ikisi de HwidKurali.Normalize'ı
/// çağırıyor (IL taraması).
/// </remarks>
public class HwidKuraliTests
{
    /// <summary>
    /// Ortak fonksiyondan önce Login.cs:188 ve RefreshSession.cs:213'te duran kopya, birebir
    /// (dc88440). Karşılaştırma kâhini: ortak fonksiyon bundan ayrılırsa eski kayıtlar ve
    /// banlı cihazlar yeni girişlerle eşleşmez olur.
    /// </summary>
    private static string? EskiNormalize(string? hwid)
    {
        var trimmed = hwid?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed[..Math.Min(trimmed.Length, 128)];
    }

    public static TheoryData<string?> Girdiler() => new()
    {
        null,
        "",
        "   ",
        "\t\n",
        "abc",
        "  ABCdef0123  ",
        new string('A', 64),
        new string('b', 128),
        new string('c', 129),
        "  " + new string('D', 300) + "  ",
        "İSTANBUL-ıi",                  // invariant küçük harf: Türkçe kültüre göre DEĞİL
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
    };

    [Theory]
    [MemberData(nameof(Girdiler))]
    public void Cikti_eski_kopyalarla_birebir_ayni(string? girdi)
        => Assert.Equal(EskiNormalize(girdi), HwidKurali.Normalize(girdi));

    [Fact]
    public void Kirpar_kucultur_128_de_keser()
    {
        Assert.Equal("abc", HwidKurali.Normalize("  ABC "));
        Assert.Equal(HwidKurali.EnFazlaUzunluk, HwidKurali.Normalize(new string('x', 500))!.Length);
        Assert.Null(HwidKurali.Normalize("  "));
    }

    [Fact]
    public void Kultur_Turkce_olsa_da_sonuc_degismez()
    {
        var onceki = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            Assert.Equal(EskiNormalize("İSTANBUL"), HwidKurali.Normalize("İSTANBUL"));
            Assert.Equal(EskiNormalize("TITLE"), HwidKurali.Normalize("TITLE"));
            Assert.Equal("title", HwidKurali.Normalize("TITLE"));  // tr-TR'de "tıtle" olurdu
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = onceki;
        }
    }

    [Theory]
    [InlineData(typeof(LoginHandler))]
    [InlineData(typeof(RefreshSessionHandler))]
    public void Handlerda_kendi_Normalize_kopyasi_yok(Type handler)
    {
        var kopyalar = handler
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name.Contains("Normalize", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(kopyalar);
    }

    [Theory]
    [InlineData(typeof(LoginHandler))]
    [InlineData(typeof(RefreshSessionHandler))]
    public void Handler_ortak_fonksiyonu_cagiriyor(Type handler)
    {
        var hedef = typeof(HwidKurali).GetMethod(nameof(HwidKurali.Normalize))!;
        Assert.True(Cagiriyor(handler, hedef), $"{handler.Name} HwidKurali.Normalize'ı çağırmıyor");
    }

    /// <summary>
    /// Tipin ve iç içe tiplerinin (async durum makinesi Handle'ın gövdesini oraya taşıyor)
    /// IL'inde hedef metoda bir call/callvirt var mı. Token'ı çözülemeyen bayt dizileri atlanır.
    /// </summary>
    private static bool Cagiriyor(Type tip, MethodInfo hedef)
    {
        const BindingFlags Hepsi = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var tipler = new[] { tip }.Concat(tip.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public));

        foreach (var t in tipler)
        {
            foreach (var m in t.GetMethods(Hepsi).Cast<MethodBase>().Concat(t.GetConstructors(Hepsi)))
            {
                var il = m.GetMethodBody()?.GetILAsByteArray();
                if (il is null)
                {
                    continue;
                }

                for (var i = 0; i + 4 < il.Length; i++)
                {
                    if (il[i] is not (0x28 or 0x6F)) // call, callvirt
                    {
                        continue;
                    }

                    var token = BitConverter.ToInt32(il, i + 1);
                    try
                    {
                        if (m.Module.ResolveMethod(token, t.IsGenericType ? t.GetGenericArguments() : null,
                                m.IsGenericMethod ? m.GetGenericArguments() : null) == hedef)
                        {
                            return true;
                        }
                    }
                    catch (ArgumentException)
                    {
                        // IL baytı yanlış hizalandı: bu bir token değil.
                    }
                }
            }
        }

        return false;
    }
}
