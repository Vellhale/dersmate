using PeerLearn.Application.Features.Communication.Bildirimler;
using PeerLearn.Application.Matchmaking;
using Xunit;

namespace PeerLearn.UnitTests.Bildirimler;

/// <summary>
/// Hatırlatma anları ve hatırlatma işinin sorgu aralıkları.
/// </summary>
/// <remarks>
/// Sorgu aralıkları yalnızca ÖN SÜZGEÇ; kesin kararı bellekteki fonksiyonlar veriyor.
/// Aralık bir gün daraltılırsa hatırlatma sessizce kaybolur (hata yok, satır yok). Bu
/// yüzden "aralık hiçbir hatırlatmayı dışarıda bırakmıyor" kaba kuvvetle sınanıyor:
/// her dakika için kararın "girer" dediği her kayıt aralığın içinde olmalı.
/// </remarks>
public class HatirlatmaPenceresiTests
{
    private static DateTime U(int gun, int saat, int dakika = 0)
        => new(2026, 10, gun, saat, dakika, 0, DateTimeKind.Utc);

    private static readonly DateTime Baslangic = U(5, 15);
    private static readonly DateTime EskiRezervasyon = U(1, 10);

    // ─── Ders yaklaşıyor ─────────────────────────────────────────────────────

    [Fact]
    public void Ders_anlari_60_ve_10_dakika_toleranslariyla()
    {
        var anlar = HatirlatmaPenceresi.DersAnlari(Baslangic, EskiRezervasyon);

        Assert.Collection(anlar,
            h => Assert.Equal(new Hatirlatma(60, Baslangic.AddMinutes(-60), Baslangic.AddMinutes(-45)), h),
            h => Assert.Equal(new Hatirlatma(10, Baslangic.AddMinutes(-10), Baslangic.AddMinutes(-5)), h));
        Assert.All(anlar, h => Assert.Equal(DateTimeKind.Utc, h.AnUtc.Kind));
    }

    [Fact]
    public void Hatirlatma_anindan_sonra_yapilan_rezervasyonda_o_ofset_atlanir()
    {
        // 30 dakika önce rezerve edilen derste "1 saat kaldı" yanlış olurdu.
        var anlar = HatirlatmaPenceresi.DersAnlari(Baslangic, Baslangic.AddMinutes(-30));
        Assert.Equal(10, Assert.Single(anlar).Ofset);

        Assert.Empty(HatirlatmaPenceresi.DersAnlari(Baslangic, Baslangic.AddMinutes(-5)));
    }

    [Fact]
    public void Rezervasyon_tam_hatirlatma_aninda_ise_ofset_korunur_bir_tick_sonra_atlanir()
    {
        Assert.Equal(2, HatirlatmaPenceresi.DersAnlari(Baslangic, Baslangic.AddMinutes(-60)).Count);
        Assert.Equal(10, Assert.Single(HatirlatmaPenceresi.DersAnlari(Baslangic, Baslangic.AddMinutes(-60).AddTicks(1))).Ofset);
    }

    [Theory]
    [InlineData(0)]   // tam başlangıç anı
    [InlineData(1)]
    [InlineData(600)]
    public void Baslangici_gecmis_derse_hatirlatma_yazilmaz(int dakikaSonra)
        => Assert.Empty(HatirlatmaPenceresi.KuyruklanacakDersHatirlatmalari(
            Baslangic.AddMinutes(dakikaSonra), Baslangic, EskiRezervasyon));

    [Fact]
    public void Ileriye_donuk_kuyruklama_iki_saat_icindeki_ani_yazar()
    {
        // now 12:00, ileri 2 sa: 60'lık an 14:00 ≤ 14:00 → girer; 10'luk an 14:50 → henüz değil.
        var k = HatirlatmaPenceresi.KuyruklanacakDersHatirlatmalari(U(5, 12), Baslangic, EskiRezervasyon);
        Assert.Equal(60, Assert.Single(k).Ofset);
    }

    [Fact]
    public void Toleransi_gecen_hatirlatma_bayattir_ve_yazilmaz()
    {
        // 60'lık anın son anı 14:15: tam o anda ve sonrasında yazılmaz, 10'luk yazılır.
        Assert.Equal(10, Assert.Single(HatirlatmaPenceresi.KuyruklanacakDersHatirlatmalari(U(5, 14, 15), Baslangic, EskiRezervasyon)).Ofset);
        Assert.Equal(2, HatirlatmaPenceresi.KuyruklanacakDersHatirlatmalari(U(5, 14, 14), Baslangic, EskiRezervasyon).Count);
    }

    [Fact]
    public void Ders_sorgu_araligi_now_arti_5_dk_ile_now_arti_3_sa()
    {
        var (alt, ust) = HatirlatmaPenceresi.DersSorguAraligi(U(5, 12));
        Assert.Equal(U(5, 12, 5), alt);
        Assert.Equal(U(5, 15), ust);
    }

    [Fact]
    public void Ders_sorgu_araligi_hicbir_hatirlatmayi_disarida_birakmaz()
    {
        var now = U(5, 12);
        var (alt, ust) = HatirlatmaPenceresi.DersSorguAraligi(now);

        for (var dk = -120; dk <= 600; dk++)
        {
            var baslangic = now.AddMinutes(dk);
            if (HatirlatmaPenceresi.KuyruklanacakDersHatirlatmalari(now, baslangic, EskiRezervasyon).Count > 0)
            {
                Assert.True(baslangic > alt && baslangic <= ust, $"başlangıç {baslangic:O} aralık dışında kaldı");
            }
        }
    }

    // ─── Otomatik onay yaklaşıyor ────────────────────────────────────────────

    [Fact]
    public void Kirk_sekiz_saatte_24_ve_2_saat_hatirlatmasi()
    {
        // damga 11:00 UTC = 14:00 TR; D = 3 gün sonra 14:00 TR, iki an da gündüz.
        var damga = U(1, 11);
        var h = HatirlatmaPenceresi.OtoOnayAnlari(damga, 48);

        Assert.Collection(h,
            x => Assert.Equal(new Hatirlatma(24, damga.AddHours(24), damga.AddHours(24).AddMinutes(30)), x),
            x => Assert.Equal(new Hatirlatma(2, damga.AddHours(46), damga.AddHours(46).AddMinutes(15)), x));
    }

    [Theory]
    [InlineData(24)]  // "24 saat kaldı" onay bekliyor bildiriminin kopyası olurdu
    [InlineData(12)]
    [InlineData(3)]
    public void Yirmi_dort_saatten_kisa_surede_R24_atlanir(int otomatikOnaySaati)
    {
        var h = HatirlatmaPenceresi.OtoOnayAnlari(U(1, 11), otomatikOnaySaati);
        Assert.DoesNotContain(h, x => x.Ofset == 24);
        Assert.Equal(2, Assert.Single(h).Ofset);
    }

    [Fact]
    public void Iki_saatte_hic_hatirlatma_yok()
        => Assert.Empty(HatirlatmaPenceresi.OtoOnayAnlari(U(1, 11), 2));

    [Fact]
    public void Varsayilan_H_ile_gunun_her_dakikasinda_iki_hatirlatma_arasi_en_az_alti_saat()
    {
        var gun = U(1, 0);
        for (var dk = 0; dk < 24 * 60; dk++)
        {
            var damga = gun.AddMinutes(dk);
            var h = HatirlatmaPenceresi.OtoOnayAnlari(damga, 48);

            Assert.Equal(2, h.Count);
            Assert.True(h[1].AnUtc - h[0].AnUtc >= HatirlatmaPenceresi.IkiHatirlatmaArasi, $"damga {damga:O}");
            Assert.All(h, x =>
            {
                Assert.True(x.AnUtc > damga, $"damga {damga:O}: hatırlatma olaydan önce");
                Assert.False(SessizSaat.SessizMi(x.AnUtc), $"damga {damga:O}: hatırlatma sessiz saatte");
                Assert.True(x.AnUtc < damga.AddHours(48), $"damga {damga:O}: hatırlatma otomatik onaydan sonra");
            });
        }
    }

    [Fact]
    public void Damga_degisince_hatirlatma_anahtari_da_degisir()
    {
        // İtiraz reddi damgayı yeniler: eski damganın hatırlatmaları yeni tamamlamayı TEKİLLEŞTİRMEMELİ.
        var ders = Guid.NewGuid();
        var damga = U(1, 11);
        Assert.NotEqual(BildirimAnahtarlari.OtoOnay(ders, damga, 24), BildirimAnahtarlari.OtoOnay(ders, damga.AddSeconds(1), 24));
        Assert.NotEqual(BildirimAnahtarlari.OtoOnay(ders, damga, 24), BildirimAnahtarlari.OtoOnay(ders, damga, 2));
        Assert.NotEqual(BildirimAnahtarlari.Onay(ders, damga), BildirimAnahtarlari.Onay(ders, damga.AddTicks(10)));
    }

    [Theory]
    [InlineData(25)]
    [InlineData(26)]
    [InlineData(27)]
    [InlineData(30)]
    [InlineData(36)]
    [InlineData(48)]
    public void Oto_onay_sorgu_araligi_hicbir_hatirlatmayi_disarida_birakmaz(int otomatikOnaySaati)
    {
        var now = U(5, 12);
        var (alt, ust) = HatirlatmaPenceresi.OtoOnaySorguAraligi(now, otomatikOnaySaati);

        for (var dk = -72 * 60; dk <= 12 * 60; dk += 3)
        {
            var damga = now.AddMinutes(dk);
            if (HatirlatmaPenceresi.KuyruklanacakOtoOnayHatirlatmalari(now, damga, otomatikOnaySaati).Count > 0)
            {
                Assert.True(damga > alt && damga <= ust, $"H={otomatikOnaySaati}, damga {damga:O} aralık dışında");
            }
        }
    }

    // ─── Günlük istek özeti ──────────────────────────────────────────────────

    [Theory]
    [InlineData(4, 59, false)]
    [InlineData(5, 0, true)]    // yuva − 2 sa: yazım başlar
    [InlineData(7, 0, true)]    // yuva: 07:00 UTC = 10:00 TR
    [InlineData(7, 59, true)]
    [InlineData(8, 0, false)]   // yuva + 1 sa: bitti
    public void Ozet_yuvasinin_yazim_penceresi(int saat, int dakika, bool yuvaVar)
    {
        var yuva = HatirlatmaPenceresi.OzetYuvasi(U(5, saat, dakika));
        if (yuvaVar)
        {
            Assert.Equal(U(5, 7), yuva);
        }
        else
        {
            Assert.Null(yuva);
        }
    }

    [Fact]
    public void Ozet_satirinin_omru_yuvadan_bir_saat_sonra_biter()
        => Assert.Equal(U(5, 8), HatirlatmaPenceresi.OzetSonu(U(5, 7)));

    [Fact]
    public void Ozete_giren_istek_dusmesinden_6_ile_30_saat_once_haber_alir()
    {
        var yuva = U(5, 7);
        var (alt, ust) = HatirlatmaPenceresi.OzetAdayAraligi(yuva);

        Assert.Equal(yuva.AddHours(6), MatchRules.DusmeAni(alt));
        Assert.Equal(yuva.AddHours(30), MatchRules.DusmeAni(ust));
    }

    [Fact]
    public void Ozet_pencereleri_ardisik_gunlerde_bosluksuz_ve_ortusmesiz()
    {
        // Her istek TAM OLARAK bir özete girer: (alt, üst] aralıkları uç uca eklenir.
        var yuva = U(1, 7);
        for (var gun = 0; gun < 40; gun++)
        {
            var (_, ust) = HatirlatmaPenceresi.OzetAdayAraligi(yuva.AddDays(gun));
            var (sonrakiAlt, _) = HatirlatmaPenceresi.OzetAdayAraligi(yuva.AddDays(gun + 1));
            Assert.Equal(ust, sonrakiAlt);
        }
    }

    [Fact]
    public void Ozet_anahtari_yuvanin_TR_tarihini_tasir()
        => Assert.Equal("istekDusecek:20261005", BildirimAnahtarlari.IstekDusecek(U(5, 7)));
}
