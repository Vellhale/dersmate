using PeerLearn.Application.Common;
using PeerLearn.Application.Features.Scheduling;
using PeerLearn.Domain.Scheduling;
using Xunit;

namespace PeerLearn.UnitTests;

/// <summary>
/// Derslerim geçmiş süzgeci (<c>GET /api/sessions?pastStatus=</c>). Sorgunun kendisi
/// (süzgecin SQL'e girdiği, toplamın süzüldüğü) gerçek veritabanında sınanıyor:
/// tools/e2e-scale.ps1, A bölümü. Burada dizgeden enum'a geçiş kapısı ve kümeler var.
/// </summary>
public class DersGecmisiSuzgeciTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Bos_deger_suzgec_yok_demek(string? deger)
    {
        // Parametresiz istek bugünkü davranışı korumalı: boş değer 400 DEĞİL, süzgeçsiz.
        Assert.Null(DersGecmisi.SuzgeciCoz(deger));
    }

    [Theory]
    [InlineData("Completed", SessionStatus.Completed)]
    [InlineData("completed", SessionStatus.Completed)]
    [InlineData(" COMPLETED ", SessionStatus.Completed)]
    [InlineData("Cancelled", SessionStatus.Cancelled)]
    [InlineData("expired", SessionStatus.Expired)]
    public void Gecmis_durum_adlari_kabul_edilir(string deger, SessionStatus beklenen)
    {
        Assert.Equal(beklenen, DersGecmisi.SuzgeciCoz(deger));
    }

    [Theory]
    // Aktif durumlar: geçmişte asla bulunmazlar, sessiz boş liste yerine 400.
    [InlineData("Booked")]
    [InlineData("AwaitingApproval")]
    [InlineData("Disputed")]
    // Tanımsız sayı ve TANIMLI sayı: kapı yalnızca ad kabul ediyor ("2" = Completed da red).
    [InlineData("99")]
    [InlineData("2")]
    [InlineData("-1")]
    // Enum.TryParse'ın kabul edeceği virgüllü birleşim ve düz çöp.
    [InlineData("Completed,Cancelled")]
    [InlineData("foo")]
    [InlineData("Tamamlandı")]
    public void Gecersiz_deger_VALIDATION_FAILED_400(string deger)
    {
        var ex = Assert.Throws<AppException>(() => DersGecmisi.SuzgeciCoz(deger));

        Assert.Equal(ErrorCodes.ValidationFailed, ex.Code);
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("Geçmiş süzgeci yalnızca Completed, Cancelled ya da Expired olabilir.", ex.Message);
    }

    [Theory]
    [InlineData(SessionStatus.Booked)]
    [InlineData(SessionStatus.AwaitingApproval)]
    [InlineData(SessionStatus.Disputed)]
    [InlineData((SessionStatus)99)]
    public void Sorguya_enum_olarak_gelen_aktif_ya_da_tanimsiz_durum_reddedilir(SessionStatus durum)
    {
        // İkinci savunma hattı: sorgu nesnesi denetleyici dışından da kurulabilir.
        var ex = Assert.Throws<AppException>(() => DersGecmisi.SuzgeciDogrula(durum));

        Assert.Equal(ErrorCodes.ValidationFailed, ex.Code);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public void Gecmis_durum_ve_bos_suzgec_dogrulamadan_gecer()
    {
        DersGecmisi.SuzgeciDogrula(null);
        DersGecmisi.SuzgeciDogrula(SessionStatus.Completed);
        DersGecmisi.SuzgeciDogrula(SessionStatus.Cancelled);
        DersGecmisi.SuzgeciDogrula(SessionStatus.Expired);
    }

    [Fact]
    public void Aktif_ve_gecmis_kumeleri_ayrik_ve_birlikte_tum_durumlar()
    {
        // Süzülebilen küme = geçmişin TAMAMI. Kesişim olsaydı bir ders iki listede birden
        // görünür, eksik olsaydı bir durum hiçbir listede görünmezdi.
        Assert.Empty(DersGecmisi.AktifDurumlar.Intersect(DersGecmisi.GecmisDurumlar));
        Assert.Equal(
            Enum.GetValues<SessionStatus>().OrderBy(d => d),
            DersGecmisi.AktifDurumlar.Concat(DersGecmisi.GecmisDurumlar).OrderBy(d => d));
        Assert.Equal(
            new[] { SessionStatus.Completed, SessionStatus.Cancelled, SessionStatus.Expired },
            DersGecmisi.GecmisDurumlar);
    }
}
