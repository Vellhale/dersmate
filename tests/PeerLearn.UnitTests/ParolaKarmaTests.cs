using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using PeerLearn.Infrastructure.Services;
using Xunit;

namespace PeerLearn.UnitTests;

/// <summary>
/// Parola karma iş faktörü (PBKDF2-HMAC-SHA256 ≥600k) ve doğrulamada yükseltme
/// (rehash-on-verify) davranışını mühürler.
/// </summary>
public class ParolaKarmaTests
{
    private readonly PasswordHasherService _hasher = new();

    [Fact]
    public void Dogru_parola_dogrulanir_ve_yeni_karma_gerekmez()
    {
        var karma = _hasher.Hash("KöpekBalığı-42!");

        Assert.True(_hasher.Verify(karma, "KöpekBalığı-42!", out var yeni));
        // Güncel faktörle üretildi: yükseltmeye gerek yok.
        Assert.Null(yeni);
    }

    [Fact]
    public void Yanlis_parola_reddedilir()
    {
        var karma = _hasher.Hash("dogru-parola");

        Assert.False(_hasher.Verify(karma, "yanlis-parola", out var yeni));
        Assert.Null(yeni);
    }

    [Fact]
    public void Eski_dusuk_faktorlu_karma_dogrulamada_yukseltilir()
    {
        // Framework'ün eski varsayılanıyla (100k) üretilmiş bir karmayı temsil ediyor.
        // PasswordHasher<T> jenerik parametreyi kullanmıyor, tip önemsiz.
        var eskiHasher = new PasswordHasher<object>(Options.Create(
            new PasswordHasherOptions { IterationCount = 100_000 }));
        var eskiKarma = eskiHasher.HashPassword(null!, "parola-123");

        // Parola doğru ama karma zayıf faktörlü → yükseltilmiş karma dönmeli.
        Assert.True(_hasher.Verify(eskiKarma, "parola-123", out var yeni));
        Assert.NotNull(yeni);
        Assert.NotEqual(eskiKarma, yeni);

        // Yükseltilmiş karma güncel faktörde: onu doğrulamak yeni bir karma gerektirmemeli.
        Assert.True(_hasher.Verify(yeni!, "parola-123", out var tekrar));
        Assert.Null(tekrar);
    }

    [Fact]
    public void Yukseltmeden_sonra_yanlis_parola_hala_reddedilir()
    {
        var eskiHasher = new PasswordHasher<object>(Options.Create(
            new PasswordHasherOptions { IterationCount = 100_000 }));
        var eskiKarma = eskiHasher.HashPassword(null!, "parola-123");

        Assert.True(_hasher.Verify(eskiKarma, "parola-123", out var yeni));
        // Yükseltme parolayı değiştirmez: yeni karma yalnızca doğru parolayı doğrular.
        Assert.False(_hasher.Verify(yeni!, "parola-123-yanlis", out _));
    }

    [Fact]
    public void Iterasyon_sayisi_owasp_esiginin_altina_dusmez()
    {
        // OWASP 2023+ önerisi PBKDF2-HMAC-SHA256 için ≥600.000.
        Assert.True(PasswordHasherService.IterasyonSayisi >= 600_000);
    }
}
