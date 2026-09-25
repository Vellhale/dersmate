using PeerLearn.Application.Features.Communication.Bildirimler;
using PeerLearn.Domain.Communication;
using Xunit;

namespace PeerLearn.UnitTests.Bildirimler;

/// <summary>
/// Gece sessizliği (22:00–09:00 TR, sabit UTC+3) ve olay türüne göre kimin kaydığı.
/// </summary>
/// <remarks>
/// Kaymanın KARARI olay noktasında (BildirimKuyrugu fabrikaları) ve hatırlatmalarda
/// (HatirlatmaPenceresi) veriliyor; bu yüzden sınırların yanında fabrikaların ürettiği
/// DueAt da burada sınanıyor. "Kaymaz" iddiaları da en az "kayar" kadar önemli: gece
/// dersi olan kişinin hatırlatması sabaha kaysaydı ders kaçardı.
/// </remarks>
public class SessizSaatTests
{
    /// <summary>TR duvar saati → UTC (sabit +3, yaz saati yok).</summary>
    private static DateTime Tr(int gun, int saat, int dakika = 0, int saniye = 0)
        => new DateTime(2026, 10, gun, saat, dakika, saniye, DateTimeKind.Utc).AddHours(-3);

    private static readonly Guid Ders = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly Guid Ogrenci = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");
    private static readonly Guid Egitmen = Guid.Parse("16fd2706-8baf-433b-82eb-8c7fada847da");

    // ─── Sınırlar ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(21, 59, false)]
    [InlineData(22, 0, true)]   // başlangıç dahil
    [InlineData(23, 59, true)]
    [InlineData(0, 0, true)]
    [InlineData(8, 59, true)]
    [InlineData(9, 0, false)]   // bitiş hariç: 09:00'da gönderim serbest
    [InlineData(14, 0, false)]
    public void Sinirlar_TR_saatiyle_olculur(int saat, int dakika, bool sessiz)
        => Assert.Equal(sessiz, SessizSaat.SessizMi(Tr(1, saat, dakika)));

    [Fact]
    public void Sinir_UTC_degil_TR_saatine_gore()
    {
        // 19:00 UTC = 22:00 TR. UTC'ye bakan bir hata burayı "gündüz" sayardı.
        var an = new DateTime(2026, 10, 1, 19, 0, 0, DateTimeKind.Utc);
        Assert.True(SessizSaat.SessizMi(an));
        Assert.False(SessizSaat.SessizMi(an.AddTicks(-1)));
    }

    [Fact]
    public void Kaydir_gece_yarisindan_once_ertesi_sabaha()
        => Assert.Equal(Tr(2, 9), SessizSaat.Kaydir(Tr(1, 23, 10)));

    [Fact]
    public void Kaydir_gece_yarisindan_sonra_ayni_gunun_sabahina()
        => Assert.Equal(Tr(1, 9), SessizSaat.Kaydir(Tr(1, 3)));

    [Theory]
    [InlineData(9, 0)]
    [InlineData(14, 0)]
    [InlineData(21, 59)]
    public void Kaydir_gunduz_ani_degistirmez(int saat, int dakika)
        => Assert.Equal(Tr(1, saat, dakika), SessizSaat.Kaydir(Tr(1, saat, dakika)));

    [Fact]
    public void Kaydirilan_an_Utc_isaretli()
    {
        // Npgsql timestamptz'ye yalnızca Kind=Utc yazar; başka Kind fırlatırdı.
        Assert.Equal(DateTimeKind.Utc, SessizSaat.Kaydir(Tr(1, 23)).Kind);
        Assert.Equal(DateTimeKind.Utc, SessizSaat.Kaydir(Tr(1, 14)).Kind);

        var belirsiz = DateTime.SpecifyKind(Tr(1, 14), DateTimeKind.Unspecified);
        var sonuc = SessizSaat.Kaydir(belirsiz);
        Assert.Equal(DateTimeKind.Utc, sonuc.Kind);
        Assert.Equal(belirsiz.Ticks, sonuc.Ticks); // değer dönüştürülmedi, yalnızca işaretlendi
    }

    [Fact]
    public void OncekiAksam_ayni_gun_ya_da_onceki_gun_21_30()
    {
        Assert.Equal(Tr(1, 21, 30), SessizSaat.OncekiAksam(Tr(1, 23)));
        Assert.Equal(Tr(1, 21, 30), SessizSaat.OncekiAksam(Tr(1, 21, 30))); // eşit an dahil
        Assert.Equal(Tr(1, 21, 30), SessizSaat.OncekiAksam(Tr(2, 4)));
    }

    // ─── İstek ve kabul: sabaha kayar ────────────────────────────────────────

    [Fact]
    public void Gelen_istek_gece_09_00_a_kayar_omru_dusme_ani()
    {
        var now = Tr(1, 23, 30);
        var satir = BildirimKuyrugu.YeniIstek(Guid.NewGuid(), Ogrenci, Egitmen, now, istekOlusturmaUtc: now);

        Assert.Equal(Tr(2, 9), satir.DueAtUtc);
        Assert.Equal(now.AddDays(14), satir.ExpiresAtUtc);
    }

    [Fact]
    public void Istek_kabulu_gece_09_00_a_kayar()
    {
        var satir = BildirimKuyrugu.IstekKabul(Guid.NewGuid(), Guid.NewGuid(), Ogrenci, Egitmen, Tr(1, 22, 30));
        Assert.Equal(Tr(2, 9), satir.DueAtUtc);
        Assert.Null(satir.ExpiresAtUtc);
    }

    [Fact]
    public void Istek_gunduz_kaymaz()
    {
        var now = Tr(1, 15);
        Assert.Equal(now, BildirimKuyrugu.YeniIstek(Guid.NewGuid(), Ogrenci, Egitmen, now, now).DueAtUtc);
        Assert.Equal(now, BildirimKuyrugu.IstekKabul(Guid.NewGuid(), Guid.NewGuid(), Ogrenci, Egitmen, now).DueAtUtc);
    }

    // ─── Onay bekliyor: sabaha kayar, ama otomatik onay − 2 sa'ya yetişmiyorsa hemen ──

    [Fact]
    public void Onay_bekliyor_gece_sabaha_kayar()
    {
        var damga = Tr(1, 23);
        var satir = BildirimKuyrugu.OnayBekliyor(Ders, Ogrenci, Egitmen, damga, otomatikOnaySaati: 48);

        Assert.Equal(Tr(2, 9), satir.DueAtUtc);
        Assert.Equal(damga.AddHours(48), satir.ExpiresAtUtc);
        Assert.Equal(damga, satir.OlayDamgasiUtc);
    }

    [Fact]
    public void Onay_bekliyor_sabah_otomatik_onaya_iki_saatten_az_birakacaksa_hemen_gider()
    {
        // D = damga + 10 sa = 09:00; sabah 09:00 > D − 2 sa (07:00): kaydırmak öğrencinin
        // itiraz hakkını sessizce yerdi.
        var damga = Tr(1, 23);
        Assert.Equal(damga, BildirimKuyrugu.OnayBekliyor(Ders, Ogrenci, Egitmen, damga, 10).DueAtUtc);
    }

    [Fact]
    public void Onay_bekliyor_sinirda_kayar_bir_dakika_otesinde_kaymaz()
    {
        var damga = Tr(1, 23);
        // sonAn − pay == kaydırılmış an (09:00) → kayar (<=)
        Assert.Equal(Tr(2, 9), SessizSaat.Kaydir(damga, Tr(2, 11), TimeSpan.FromHours(2)));
        // sonAn − pay = 08:59 < 09:00 → hemen
        Assert.Equal(damga, SessizSaat.Kaydir(damga, Tr(2, 10, 59), TimeSpan.FromHours(2)));
    }

    // ─── Otomatik onay hatırlatmaları: R24 sabaha, R2 önceki akşama ──────────

    [Fact]
    public void R24_sessize_duserse_sabaha_kayar_ve_otomatik_onaydan_en_az_uc_saat_once_kalir()
    {
        // D = 04:00 (3. gün) → R24 = 04:00 (2. gün, sessiz) → 09:00 (2. gün).
        var damga = Tr(1, 4);
        var h = HatirlatmaPenceresi.OtoOnayAnlari(damga, 48);

        var r24 = Assert.Single(h, x => x.Ofset == 24);
        Assert.Equal(Tr(2, 9), r24.AnUtc);
        Assert.True(r24.AnUtc < damga.AddHours(48) - HatirlatmaPenceresi.SabahEnGecKalan);
    }

    [Fact]
    public void R24_kaymasi_gunun_her_dakikasinda_D_eksi_3_saat_sinirini_asmaz()
    {
        var gun = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var dk = 0; dk < 24 * 60; dk++)
        {
            var damga = gun.AddMinutes(dk);
            foreach (var h in HatirlatmaPenceresi.OtoOnayAnlari(damga, 48).Where(x => x.Ofset == 24))
            {
                Assert.True(h.AnUtc < damga.AddHours(48) - TimeSpan.FromHours(3), $"damga {damga:O}");
                Assert.False(SessizSaat.SessizMi(h.AnUtc), $"damga {damga:O}: R24 sessiz saatte kaldı");
            }
        }
    }

    [Fact]
    public void R2_sessize_duserse_onceki_21_30_a_cekilir()
    {
        // D = 01:00 (3. gün) → R2 = 23:00 (2. gün, sessiz) → 21:30 (2. gün).
        var damga = Tr(1, 1);
        var r2 = Assert.Single(HatirlatmaPenceresi.OtoOnayAnlari(damga, 48), x => x.Ofset == 2);
        Assert.Equal(Tr(2, 21, 30), r2.AnUtc);
    }

    [Fact]
    public void R2_cekildigi_aksam_damgadan_once_kaliyorsa_atlanir()
    {
        // H = 3: damga 23:30 → D = 02:30 → R2 = 00:30 (sessiz) → 21:30, yani damgadan ÖNCE.
        // Olaydan önceye düşen hatırlatma anlamsız: atla. (R24 zaten H ≤ 24 olduğu için yok.)
        Assert.Empty(HatirlatmaPenceresi.OtoOnayAnlari(Tr(1, 23, 30), 3));
    }

    // ─── Ders planı ve iptal: 12 saat kuralı ─────────────────────────────────

    [Fact]
    public void Rezervasyon_on_iki_saatten_yakin_derste_gece_de_hemen_gider()
    {
        var now = Tr(1, 23);
        var satir = BildirimKuyrugu.DersPlanlandi(Ders, Egitmen, Ogrenci, now, now.AddHours(12).AddTicks(-1));
        Assert.Equal(now, satir.DueAtUtc);
    }

    [Fact]
    public void Rezervasyon_on_iki_saat_ve_otesindeki_derste_gece_sabaha_kayar()
    {
        var now = Tr(1, 23);
        var satir = BildirimKuyrugu.DersPlanlandi(Ders, Egitmen, Ogrenci, now, now.AddHours(12));
        Assert.Equal(Tr(2, 9), satir.DueAtUtc);
        Assert.Equal(now.AddHours(12), satir.ExpiresAtUtc);
    }

    [Fact]
    public void Iptal_de_ayni_on_iki_saat_kuralini_izler()
    {
        var now = Tr(1, 2);
        Assert.Equal(now, BildirimKuyrugu.DersIptal(Ders, Ogrenci, Egitmen, now, now.AddHours(5)).DueAtUtc);
        Assert.Equal(Tr(1, 9), BildirimKuyrugu.DersIptal(Ders, Ogrenci, Egitmen, now, now.AddDays(2)).DueAtUtc);
    }

    // ─── Kaymayanlar ─────────────────────────────────────────────────────────

    [Fact]
    public void Yeni_mesaj_gece_kaymaz_yalnizca_gecikme_eklenir()
    {
        var now = Tr(1, 2);
        var satir = BildirimKuyrugu.YeniMesaj(Guid.NewGuid(), Guid.NewGuid(), Ogrenci, Egitmen, now, TimeSpan.FromSeconds(10));
        Assert.Equal(now.AddSeconds(10), satir.DueAtUtc);
    }

    [Fact]
    public void Yaklasan_ders_hatirlatmasi_gece_kaymaz()
    {
        // 03:00'te başlayan ders: 02:00 ve 02:50 hatırlatmaları sessiz aralıkta ama KAYMAZ.
        var baslangic = Tr(1, 3);
        var anlar = HatirlatmaPenceresi.DersAnlari(baslangic, rezervasyonUtc: Tr(1, 3).AddDays(-3));

        Assert.Equal(new[] { Tr(1, 2), Tr(1, 2, 50) }, anlar.Select(a => a.AnUtc));
        Assert.All(anlar, a => Assert.True(SessizSaat.SessizMi(a.AnUtc)));
    }

    [Fact]
    public void Test_bildirimi_gece_kaymaz()
    {
        var now = Tr(1, 2);
        Assert.Equal(now, BildirimKuyrugu.Test(Ogrenci, BildirimKanallari.Mesajlar, now).DueAtUtc);
    }

    [Fact]
    public void Kayan_her_tur_ve_ayni_kaydirmayi_kullanir()
    {
        // Sessiz aralığın her dakikasında kayan türler 09:00'a düşer, kaymayanlar yerinde kalır.
        var gun = Tr(1, 22);
        for (var dk = 0; dk < 11 * 60; dk++)
        {
            var now = gun.AddMinutes(dk);
            var sabah = SessizSaat.SonrakiSabah(now);
            Assert.Equal(sabah, BildirimKuyrugu.YeniIstek(Guid.NewGuid(), Ogrenci, Egitmen, now, now).DueAtUtc);
            Assert.Equal(sabah, BildirimKuyrugu.IstekKabul(Guid.NewGuid(), Guid.NewGuid(), Ogrenci, Egitmen, now).DueAtUtc);
            Assert.Equal(now + TimeSpan.FromSeconds(10),
                BildirimKuyrugu.YeniMesaj(Guid.NewGuid(), Guid.NewGuid(), Ogrenci, Egitmen, now, TimeSpan.FromSeconds(10)).DueAtUtc);
            Assert.Equal(NotificationStatus.Pending, BildirimKuyrugu.YeniIstek(Guid.NewGuid(), Ogrenci, Egitmen, now, now).Status);
        }
    }

    // ─── Gönderim anında: tek kural (BildirimKuyrugu.SessizSaatVadesi) ──────────
    //
    // Dağıtıcı, sessiz saate GECİKEREK giren satıra (sunucu kapalıydı, Expo düştü) ve yeniden
    // deneme vadesine aynı fonksiyonu uyguluyor. Bu testler kuralın kendisini ve fabrikalarla
    // AYNI kaldığını kilitliyor; dağıtıcıdaki kullanımı e2e-bildirim 3. bölüm sınıyor.

    [Theory]
    [InlineData(NotificationType.MatchRequest)]
    [InlineData(NotificationType.MatchAccepted)]
    public void Istek_ve_kabul_gonderim_aninda_da_sabaha_kayar(NotificationType tur)
    {
        Assert.Equal(Tr(2, 9), BildirimKuyrugu.SessizSaatVadesi(tur, Tr(1, 22, 0, 30)));
        Assert.Equal(Tr(1, 21, 59), BildirimKuyrugu.SessizSaatVadesi(tur, Tr(1, 21, 59)));
    }

    [Fact]
    public void Yeniden_deneme_vadesi_22_00_i_gecerse_sabaha_kayar()
    {
        // Bulgudaki senaryo: 21:57'de kabul, Expo 503; bekleme 21:58:30 → 22:00:30.
        var yenidenDeneme = Tr(1, 21, 58, 30) + TimeSpan.FromMinutes(2);
        Assert.Equal(Tr(2, 9), BildirimKuyrugu.SessizSaatVadesi(NotificationType.MatchAccepted, yenidenDeneme));
    }

    [Fact]
    public void Onay_bekliyor_gonderim_aninda_da_otomatik_onay_payini_korur()
    {
        var an = Tr(1, 23);
        // Otomatik onay 48 sa sonra: sabaha kayar.
        Assert.Equal(Tr(2, 9), BildirimKuyrugu.SessizSaatVadesi(NotificationType.ApprovalPending, an, an.AddHours(48)));
        // Otomatik onay 10:00'da: 09:00 > 10:00 − 2 sa → hemen (itiraz hakkı gece uyandırmaya değer).
        Assert.Equal(an, BildirimKuyrugu.SessizSaatVadesi(NotificationType.ApprovalPending, an, Tr(2, 10)));
    }

    [Fact]
    public void Ders_plani_ve_iptal_gonderim_aninda_da_on_iki_saat_kuralini_izler()
    {
        var an = Tr(1, 22, 5);
        foreach (var tur in new[] { NotificationType.LessonBooked, NotificationType.LessonCancelled })
        {
            Assert.Equal(an, BildirimKuyrugu.SessizSaatVadesi(tur, an, an.AddHours(3)));
            Assert.Equal(Tr(2, 9), BildirimKuyrugu.SessizSaatVadesi(tur, an, an.AddDays(2)));
        }
    }

    [Theory]
    [InlineData(NotificationType.NewMessage)]
    [InlineData(NotificationType.LessonSoon)]
    [InlineData(NotificationType.AutoApproveSoon)]
    [InlineData(NotificationType.MatchExpiringDigest)]
    [InlineData(NotificationType.Test)]
    public void Kaymayan_turler_gonderim_aninda_da_kaymaz(NotificationType tur)
    {
        var an = Tr(1, 2);
        Assert.Equal(an, BildirimKuyrugu.SessizSaatVadesi(tur, an, an.AddHours(30)));
        Assert.Equal(DateTimeKind.Utc, BildirimKuyrugu.SessizSaatVadesi(tur, DateTime.SpecifyKind(an, DateTimeKind.Unspecified)).Kind);
    }

    [Fact]
    public void Fabrikalar_ve_gonderim_ani_ayni_kurali_uygular()
    {
        // Sessiz aralığın ve iki yanının her 7 dakikasında fabrika vadesi = kuralın cevabı.
        // Biri değişip öteki değişmezse gece yazılan satır ile geciken satır farklı saatte giderdi.
        var bas = Tr(1, 20);
        for (var dk = 0; dk < 15 * 60; dk += 7)
        {
            var now = bas.AddMinutes(dk);
            var onay = now.AddHours(dk % 3 == 0 ? 10 : 48);
            var ders = now.AddHours(dk % 2 == 0 ? 5 : 30);

            Assert.Equal(BildirimKuyrugu.SessizSaatVadesi(NotificationType.MatchRequest, now),
                BildirimKuyrugu.YeniIstek(Guid.NewGuid(), Ogrenci, Egitmen, now, now).DueAtUtc);
            Assert.Equal(BildirimKuyrugu.SessizSaatVadesi(NotificationType.MatchAccepted, now),
                BildirimKuyrugu.IstekKabul(Guid.NewGuid(), Guid.NewGuid(), Ogrenci, Egitmen, now).DueAtUtc);
            Assert.Equal(BildirimKuyrugu.SessizSaatVadesi(NotificationType.ApprovalPending, now, onay),
                BildirimKuyrugu.OnayBekliyor(Ders, Ogrenci, Egitmen, now, (int)(onay - now).TotalHours).DueAtUtc);
            Assert.Equal(BildirimKuyrugu.SessizSaatVadesi(NotificationType.LessonBooked, now, ders),
                BildirimKuyrugu.DersPlanlandi(Ders, Egitmen, Ogrenci, now, ders).DueAtUtc);
            Assert.Equal(BildirimKuyrugu.SessizSaatVadesi(NotificationType.LessonCancelled, now, ders),
                BildirimKuyrugu.DersIptal(Ders, Ogrenci, Egitmen, now, ders).DueAtUtc);
        }
    }
}
