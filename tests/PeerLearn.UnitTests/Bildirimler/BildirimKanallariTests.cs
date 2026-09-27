using PeerLearn.Application.Features.Communication.Bildirimler;
using PeerLearn.Domain.Communication;
using Xunit;

namespace PeerLearn.UnitTests.Bildirimler;

/// <summary>
/// Tür → kanal / tercih / veri türü eşlemesi ve tercih süzgeci.
/// </summary>
/// <remarks>
/// ⛔ Kanal kimlikleri ve data.tur değerleri MOBİLLE BİREBİR AYNI olmak zorunda (dersmate
/// Mobil → src/lib/bildirimler.js). Android tanımadığı kanal kimliğiyle gelen bildirimi
/// SESSİZCE düşürür; mobil tanımadığı data.tur ile gelen bildirimde yanlış ekranı tazeler.
/// Bu yüzden değerler burada elle yazılı: değiştiren kişi testi de değiştirmek zorunda
/// kalsın ve mobil tarafı hatırlasın.
/// </remarks>
public class BildirimKanallariTests
{
    [Fact]
    public void Kanal_kimlikleri_mobille_ayni()
    {
        Assert.Equal("mesajlar", BildirimKanallari.Mesajlar);
        Assert.Equal("istekler", BildirimKanallari.Istekler);
        Assert.Equal("ders-onayi", BildirimKanallari.DersOnayi);
        Assert.Equal("ders-plani", BildirimKanallari.DersPlani);
        Assert.Equal(new[] { "mesajlar", "istekler", "ders-onayi", "ders-plani" }, BildirimKanallari.Hepsi);
    }

    [Theory]
    [InlineData(NotificationType.NewMessage, "mesajlar", "mesaj")]
    [InlineData(NotificationType.MatchRequest, "istekler", "istek")]
    [InlineData(NotificationType.MatchAccepted, "istekler", "istekKabul")]
    [InlineData(NotificationType.MatchExpiringDigest, "istekler", "istekDusecek")]
    [InlineData(NotificationType.ApprovalPending, "ders-onayi", "onay")]
    [InlineData(NotificationType.AutoApproveSoon, "ders-onayi", "otoOnay")]
    [InlineData(NotificationType.LessonBooked, "ders-plani", "dersPlan")]
    [InlineData(NotificationType.LessonCancelled, "ders-plani", "dersIptal")]
    [InlineData(NotificationType.LessonSoon, "ders-plani", "dersYaklasiyor")]
    public void Tur_kanal_ve_veri_turu_eslemesi(NotificationType tur, string kanal, string veriTuru)
    {
        Assert.Equal(kanal, BildirimKanallari.Kanal(tur));
        Assert.Equal(veriTuru, BildirimKanallari.VeriTuru(tur));
    }

    [Fact]
    public void Her_turun_eslemesi_var()
    {
        // Yeni bir tür eşlemesiz eklenirse dağıtıcı o satırı Failed yazar; burada yakalansın.
        foreach (var tur in Enum.GetValues<NotificationType>())
        {
            Assert.False(string.IsNullOrEmpty(BildirimKanallari.VeriTuru(tur)));
            var kanal = tur == NotificationType.Test
                ? BildirimKanallari.Kanal(tur, BildirimKanallari.Mesajlar)
                : BildirimKanallari.Kanal(tur);
            Assert.True(BildirimKanallari.Bilinen(kanal), $"{tur} → {kanal}");
        }
    }

    [Fact]
    public void Test_turu_kullanicinin_sectigi_kanaldan_gider()
    {
        Assert.Equal("test", BildirimKanallari.VeriTuru(NotificationType.Test));
        foreach (var kanal in BildirimKanallari.Hepsi)
        {
            Assert.Equal(kanal, BildirimKanallari.Kanal(NotificationType.Test, kanal));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bilinmez")]
    [InlineData("Mesajlar")]
    public void Test_turu_bilinmeyen_kanalla_sessizce_yanlis_kanala_gitmez(string? kanal)
        => Assert.Throws<ArgumentOutOfRangeException>(() => BildirimKanallari.Kanal(NotificationType.Test, kanal));

    [Theory]
    [InlineData("mesaj", "mesajlar")]
    [InlineData("istek", "istekler")]
    [InlineData("onay", "ders-onayi")]
    [InlineData("ders", "ders-plani")]
    [InlineData("MESAJ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Test_ucunun_tur_alani(string? testTuru, string? kanal)
        => Assert.Equal(kanal, BildirimKanallari.TestKanali(testTuru));

    // ─── Tercih süzgeci ──────────────────────────────────────────────────────

    [Fact]
    public void Tercih_satiri_yoksa_hepsi_acik()
    {
        foreach (var tur in Enum.GetValues<NotificationType>())
        {
            Assert.True(BildirimKanallari.TercihAcik(null, tur), tur.ToString());
        }
    }

    [Fact]
    public void Yeni_tercih_satirinin_varsayilani_dordu_acik()
    {
        // DB varsayılanı da true: tek sütunluk upsert diğer üç sütunu vermez.
        var t = new NotificationPreference();
        Assert.True(t.Messages && t.Requests && t.LessonApproval && t.LessonPlan);
    }

    [Theory]
    [InlineData("mesajlar")]
    [InlineData("istekler")]
    [InlineData("ders-onayi")]
    [InlineData("ders-plani")]
    public void Kapatilan_kategori_yalnizca_kendi_kanalinin_turlerini_susturur(string kapali)
    {
        var tercih = new NotificationPreference
        {
            Messages = kapali != "mesajlar",
            Requests = kapali != "istekler",
            LessonApproval = kapali != "ders-onayi",
            LessonPlan = kapali != "ders-plani",
        };

        foreach (var tur in Enum.GetValues<NotificationType>().Where(t => t != NotificationType.Test))
        {
            var beklenen = BildirimKanallari.Kanal(tur) != kapali;
            Assert.Equal(beklenen, BildirimKanallari.TercihAcik(tercih, tur));
        }
    }

    [Fact]
    public void Test_bildirimi_tercih_suzgecinden_muaf()
    {
        // "Neden bildirim gelmiyor" diye bakan kullanıcının kapattığı kategori testi de susturmamalı.
        var hepsiKapali = new NotificationPreference { Messages = false, Requests = false, LessonApproval = false, LessonPlan = false };
        Assert.True(BildirimKanallari.TercihAcik(hepsiKapali, NotificationType.Test));
    }

    [Theory]
    [InlineData("mesajlar", "Messages")]
    [InlineData("istekler", "Requests")]
    [InlineData("ders-onayi", "LessonApproval")]
    [InlineData("ders-plani", "LessonPlan")]
    public void Tercih_sutunu_beyaz_listeden(string kategori, string sutun)
        => Assert.Equal(sutun, BildirimKanallari.TercihSutunu(kategori));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Mesajlar")]
    [InlineData("Messages")]
    [InlineData("\"Messages\" = false; --")]
    [InlineData("mesajlar ")]
    public void Tercih_sutunu_istek_metnini_asla_SQL_e_tasimaz(string? kategori)
        => Assert.Null(BildirimKanallari.TercihSutunu(kategori));

    [Fact]
    public void Tercih_sutunlari_varlik_ozellikleriyle_ayni_adda()
    {
        // Upsert kolon adını buradan METİN olarak alıyor; ad kayarsa SQL "kolon yok" ile düşer.
        foreach (var kategori in BildirimKanallari.Hepsi)
        {
            var sutun = BildirimKanallari.TercihSutunu(kategori)!;
            Assert.NotNull(typeof(NotificationPreference).GetProperty(sutun));
        }
    }

    // ─── Teslim önceliği ve iOS grubu ────────────────────────────────────────

    [Theory]
    [InlineData("mesajlar", "high", null)]
    [InlineData("istekler", "normal", "istekler")]
    [InlineData("ders-onayi", "high", "dersler")]
    [InlineData("ders-plani", "high", "dersler")]
    public void Oncelik_ve_ios_grubu(string kanal, string oncelik, string? grup)
    {
        Assert.Equal(oncelik, BildirimKanallari.Oncelik(kanal));
        Assert.Equal(grup, BildirimKanallari.IosGrubu(kanal));
    }
}
