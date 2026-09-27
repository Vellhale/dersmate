using System.Diagnostics;
using PeerLearn.Infrastructure.Services;
using Xunit;

namespace PeerLearn.UnitTests.Bildirimler;

/// <summary>
/// "Kuyrukta iş var" sinyali. Handler'lar bunu COMMIT'TEN SONRA çağırıyor: fırlatırsa
/// commit olmuş mesaj istemciye hata döner, istemci yeniden gönderir ve mesaj iki kez
/// yazılır. Sayaç biriktirirse dağıtıcı bin kez boşuna tarar.
/// </summary>
public class BildirimSinyaliTests
{
    private static readonly TimeSpan Kisa = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task Bin_art_arda_Uyandir_firlatmaz_ve_tek_uyanis_birakir()
    {
        var s = new BildirimSinyali();
        var ex = Record.Exception(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                s.Uyandir();
            }
        });

        Assert.Null(ex);
        Assert.True(await s.BekleAsync(Kisa, default));
        Assert.False(await s.BekleAsync(Kisa, default));
    }

    [Fact]
    public async Task Eszamanli_Uyandir_firlatmaz_tek_uyanis()
    {
        var s = new BildirimSinyali();
        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
            {
                s.Uyandir();
            }
        })));

        Assert.True(await s.BekleAsync(Kisa, default));
        Assert.False(await s.BekleAsync(Kisa, default));
    }

    [Fact]
    public async Task Bekleyen_dagitici_sinyalle_hemen_uyanir()
    {
        var s = new BildirimSinyali();
        var sure = Stopwatch.StartNew();
        var bekleyen = s.BekleAsync(TimeSpan.FromSeconds(10), default);

        await Task.Delay(100);
        s.Uyandir();

        Assert.True(await bekleyen);
        Assert.True(sure.Elapsed < TimeSpan.FromSeconds(5), $"{sure.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Sinyal_yoksa_sure_dolunca_false()
    {
        var s = new BildirimSinyali();
        Assert.False(await s.BekleAsync(Kisa, default));
    }

    [Fact]
    public async Task Kapanis_iptali_OperationCanceledException()
    {
        var s = new BildirimSinyali();
        using var iptal = new CancellationTokenSource(Kisa);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s.BekleAsync(TimeSpan.FromSeconds(10), iptal.Token));
    }

    [Fact]
    public async Task Sure_dolan_bekleme_sonraki_sinyali_kaybetmez()
    {
        var s = new BildirimSinyali();
        Assert.False(await s.BekleAsync(Kisa, default));
        s.Uyandir();
        Assert.True(await s.BekleAsync(Kisa, default));
    }
}
