using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Features.Communication.Bildirimler;
using PeerLearn.Application.Options;
using PeerLearn.Domain.Communication;
using PeerLearn.Infrastructure.Services;
using Xunit;

namespace PeerLearn.UnitTests.Bildirimler;

/// <summary>
/// Push:Provider = "Log": geliştirmenin ve e2e paketinin (tools/e2e-bildirim.ps1) göndericisi.
/// </summary>
/// <remarks>
/// Test kancaları (…OLU], …YABANCI], …YAVAS]) e2e'nin Expo'nun hata yollarını gerçek bir
/// Expo hesabı olmadan sınamasının TEK yolu. Kanca sessizce bozulursa e2e'deki makbuz,
/// yabancı deneyim ve "gönderim sürerken cihaz silme" senaryoları yanlış nedenle geçer ya
/// da hiç koşmaz; bu yüzden kancaların sözleşmesi burada ayrıca kilitleniyor.
/// </remarks>
public class LoggingPushGondericiTests
{
    private const string Deneyim = "@ardaerenguler/dersmate";

    private readonly ToplayanGunluk _gunluk = new();

    private LoggingPushGonderici Kur(string deneyim = Deneyim)
        => new(Options.Create(new PushOptions { DeneyimKimligi = deneyim }), _gunluk.Al<LoggingPushGonderici>());

    private static string Tok(string ek = "") => $"ExponentPushToken[{Guid.NewGuid():N}{ek}]";

    private static PushMesaji Mesaj(string token)
    {
        var taslak = new BildirimTaslagi(NotificationType.NewMessage, BildirimKanallari.Mesajlar,
            new BildirimIcerigi("Yeni mesaj", "Gizli Ad sana yeni bir mesaj gönderdi."), "/sohbet/x", "0011223344556677", "s-aa", 60, null);
        return BildirimYuku.Kur(taslak, token, PushPlatform.Android);
    }

    [Fact]
    public void Kanca_eki_kayit_ucunun_bicim_kuralindan_gecer()
    {
        // Ek köşeli parantezin İÇİNDE: e2e bu token'larla gerçek PUT /push/devices yapıyor.
        Assert.True(PushTokenKurali.Gecerli(Tok("OLU")));
        Assert.True(PushTokenKurali.Gecerli(Tok("YABANCI")));
        Assert.True(PushTokenKurali.Gecerli(Tok("YAVAS")));
        Assert.EndsWith(LoggingPushGonderici.OluEki, Tok("OLU"));
        Assert.EndsWith(LoggingPushGonderici.YabanciEki, Tok("YABANCI"));
        Assert.EndsWith(LoggingPushGonderici.YavasEki, Tok("YAVAS"));
    }

    [Fact]
    public async Task Siradan_token_ok_bileti_ve_ok_makbuzu()
    {
        var g = Kur();
        var sonuc = await g.GonderAsync([Mesaj(Tok()), Mesaj(Tok())], default);

        Assert.Null(sonuc.IstekHatasi);
        Assert.Equal(2, sonuc.Biletler.Count);
        Assert.All(sonuc.Biletler, b => Assert.True(b.Basarili));
        Assert.Equal(2, sonuc.Biletler.Select(b => b.BiletId).Distinct().Count());

        var makbuz = await g.MakbuzlariAlAsync(sonuc.Biletler.Select(b => b.BiletId!).ToList(), default);
        Assert.All(makbuz.Values, m => Assert.True(m.Basarili));
    }

    [Fact]
    public async Task OLU_bilet_ok_makbuz_DeviceNotRegistered()
    {
        var g = Kur();
        var bilet = Assert.Single((await g.GonderAsync([Mesaj(Tok("OLU"))], default)).Biletler);
        Assert.True(bilet.Basarili);

        var makbuz = await g.MakbuzlariAlAsync([bilet.BiletId!], default);
        Assert.Equal(PushHataKurali.CihazKayitliDegil, makbuz[bilet.BiletId!].HataKodu);
        Assert.Equal(BiletKarari.CihazOlu, PushHataKurali.Bilet(new PushBileti(false, null, makbuz[bilet.BiletId!].HataKodu, null)));
    }

    [Fact]
    public async Task OLU_kancasi_durumsuz_baska_ornek_de_ayni_makbuzu_verir()
    {
        // Süreç yeniden başlasa da çalışmalı: karar bilet kimliğinin önekinden okunuyor.
        var bilet = Assert.Single((await Kur().GonderAsync([Mesaj(Tok("OLU"))], default)).Biletler).BiletId!;
        var makbuz = await Kur().MakbuzlariAlAsync([bilet], default);
        Assert.False(makbuz[bilet].Basarili);
    }

    [Fact]
    public async Task Bu_saglayicinin_vermedigi_bilet_hazir_degil_sayilir()
    {
        var makbuz = await Kur().MakbuzlariAlAsync(["gercek-expo-bileti"], default);
        Assert.Empty(makbuz);
    }

    [Fact]
    public async Task YABANCI_istek_duzeyinde_cok_deneyim_hatasi_ve_harita()
    {
        var yabanci = Tok("YABANCI");
        var bizim = Tok();
        var sonuc = await Kur().GonderAsync([Mesaj(bizim), Mesaj(yabanci)], default);

        var hata = Assert.IsType<PushIstekHatasi>(sonuc.IstekHatasi);
        Assert.Empty(sonuc.Biletler);
        Assert.Equal(400, hata.HttpDurumu);
        Assert.Equal(PushHataKurali.CokDeneyim, hata.Kod);
        Assert.Equal(new[] { yabanci }, hata.DeneyimTokenlari![LoggingPushGonderici.YabanciDeneyim]);
        Assert.Equal(new[] { bizim }, hata.DeneyimTokenlari[Deneyim]);
        Assert.Equal(IstekKarari.YabanciDeneyim, PushHataKurali.Istek(hata));
    }

    [Fact]
    public async Task YABANCI_tek_basina_da_hata_verir_harita_yalnizca_yabanciyi_tasir()
    {
        // Gerçek Expo iki proje yokken vermez; kanca verir ki sonuç partinin bileşimine bağlı
        // olmasın. Dağıtıcı bizim deneyimi haritada görmeyince HİÇBİR cihazı silmez.
        var hata = (await Kur().GonderAsync([Mesaj(Tok("YABANCI"))], default)).IstekHatasi!;
        Assert.Equal(new[] { LoggingPushGonderici.YabanciDeneyim }, hata.DeneyimTokenlari!.Keys);
    }

    [Fact]
    public async Task YAVAS_yaniti_uc_saniye_geciktirir_ve_iptale_uyar()
    {
        var g = Kur();
        var sure = Stopwatch.StartNew();
        await g.GonderAsync([Mesaj(Tok("YAVAS"))], default);
        Assert.True(sure.Elapsed >= LoggingPushGonderici.YavasGecikme - TimeSpan.FromMilliseconds(50), $"{sure.ElapsedMilliseconds} ms");

        using var iptal = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => g.GonderAsync([Mesaj(Tok("YAVAS"))], iptal.Token));
    }

    [Fact]
    public async Task Gercek_gondericiyle_ayni_sinir_yuz_birinci_mesaj_istisna()
        => await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            Kur().GonderAsync(Enumerable.Range(0, PushSinirlari.EnFazlaMesaj + 1).Select(_ => Mesaj(Tok())).ToList(), default));

    [Fact]
    public async Task Gunluge_baslik_govde_ve_tam_token_yazilmaz()
    {
        var tokenlar = new[] { Tok(), Tok("OLU"), Tok() };
        await Kur().GonderAsync(tokenlar.Select(Mesaj).ToList(), default);
        await Kur().GonderAsync([Mesaj(Tok("YABANCI"))], default);

        Assert.NotEmpty(_gunluk.Satirlar);
        Assert.All(_gunluk.Satirlar, s =>
        {
            Assert.DoesNotContain("Gizli Ad", s.Metin);
            Assert.DoesNotContain("Yeni mesaj", s.Metin);
            foreach (var t in tokenlar)
            {
                Assert.DoesNotContain(t[(t.IndexOf('[') + 1)..^1], s.Metin);
            }
        });
        Assert.Contains(_gunluk.Satirlar, s => s.Metin.Contains("[PUSH-LOG]") && s.Metin.Contains("0011223344556677"));
        Assert.Equal(0, _gunluk.Say(LogLevel.Error) + _gunluk.Say(LogLevel.Critical));
    }
}
