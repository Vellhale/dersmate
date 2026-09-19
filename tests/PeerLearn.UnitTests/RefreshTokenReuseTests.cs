using PeerLearn.Application.Identity;
using PeerLearn.Domain.Identity;
using Xunit;

namespace PeerLearn.UnitTests;

/// <summary>
/// Yeniden-kullanım (hırsızlık) tespitinin, iptal edilmiş bir token yeniden sunulduğunda
/// zinciri YALNIZCA gerçek delilde düşürdüğünü kilitler:
/// <see cref="RefreshTokenService.GercekYenidenKullanim"/> = dönüşmüş (Rotated) token +
/// pencere dışı. Diğer iptal sebepleri (çıkış, parola değişimi, yaptırım, hesap silme)
/// ölü token'ın masum tekrarıdır; reddedilir ama zincir DÜŞÜRÜLMEZ — aksi hâlde sıfırlama
/// ya da çıkış sonrası açılan taze oturumlar da topluca düşerdi.
/// </summary>
public class RefreshTokenReuseTests
{
    private static readonly DateTime Now = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
    private const int Pencere = RefreshTokenService.DonusumTekrarPenceresiSaniye; // 30 sn

    // ---- Gerçek hırsızlık: Rotated + pencere DIŞI → zincir düşürülür ----

    [Fact]
    public void Rotated_pencere_disinda_gercek_yeniden_kullanimdir()
    {
        var iptalAni = Now.AddSeconds(-(Pencere + 10)); // pencere çoktan geçmiş
        Assert.True(RefreshTokenService.GercekYenidenKullanim(
            RefreshTokenRevokeReason.Rotated, iptalAni, Now));
    }

    [Fact]
    public void Rotated_tam_sinirda_gercek_yeniden_kullanimdir()
    {
        // iptalAni + pencere == now: sınır dahil (eski davranışla birebir).
        var iptalAni = Now.AddSeconds(-Pencere);
        Assert.True(RefreshTokenService.GercekYenidenKullanim(
            RefreshTokenRevokeReason.Rotated, iptalAni, Now));
    }

    // ---- İyi niyetli tekrar: Rotated + pencere İÇİ → düşürülmez ----

    [Fact]
    public void Rotated_pencere_icinde_dusurulmez()
    {
        var iptalAni = Now.AddSeconds(-(Pencere - 5)); // pencere hâlâ açık
        Assert.False(RefreshTokenService.GercekYenidenKullanim(
            RefreshTokenRevokeReason.Rotated, iptalAni, Now));
    }

    [Fact]
    public void Rotated_sinirin_bir_tick_gerisinde_dusurulmez()
    {
        // iptalAni + pencere, now'dan bir tick SONRA → hâlâ pencere içi.
        var iptalAni = Now.AddSeconds(-Pencere).AddTicks(1);
        Assert.False(RefreshTokenService.GercekYenidenKullanim(
            RefreshTokenRevokeReason.Rotated, iptalAni, Now));
    }

    // ---- Rotated DIŞI her sebep: pencere dışı bile olsa ASLA düşürülmez ----

    [Theory]
    [InlineData(RefreshTokenRevokeReason.SignedOut)]
    [InlineData(RefreshTokenRevokeReason.PasswordChanged)]
    [InlineData(RefreshTokenRevokeReason.AccountDeleted)]
    [InlineData(RefreshTokenRevokeReason.Sanctioned)]
    [InlineData(RefreshTokenRevokeReason.ReuseDetected)]
    public void Rotated_disi_sebepler_pencere_disinda_bile_dusurulmez(RefreshTokenRevokeReason sebep)
    {
        // Zaman "pencere dışı" olsa bile: sebep Rotated değilse hırsızlık sayılmaz.
        var iptalAni = Now.AddSeconds(-(Pencere + 100));
        Assert.False(RefreshTokenService.GercekYenidenKullanim(sebep, iptalAni, Now));
    }

    [Fact]
    public void Sebep_null_ise_dusurulmez()
    {
        // RevokedAtUtc dolu ama RevokeReason null (pratikte oluşmaz): güvenli varsayılan
        // "zinciri nükleme".
        var iptalAni = Now.AddSeconds(-(Pencere + 100));
        Assert.False(RefreshTokenService.GercekYenidenKullanim(null, iptalAni, Now));
    }
}
