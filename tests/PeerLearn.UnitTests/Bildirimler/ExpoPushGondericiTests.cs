using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PeerLearn.Application;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Features.Communication.Bildirimler;
using PeerLearn.Application.Options;
using PeerLearn.Domain.Communication;
using PeerLearn.Infrastructure;
using PeerLearn.Infrastructure.Services;
using Xunit;

namespace PeerLearn.UnitTests.Bildirimler;

/// <summary>
/// Gerçek Expo göndericisi, sahte HTTP ucuyla. Ağa çıkılmıyor.
/// </summary>
/// <remarks>
/// ⚠️ "100'lük parçalama" tasarımda göndericinin işiydi; kodda BİLİNÇLİ OLARAK değil
/// (ExpoPushGonderici.GonderAsync yorumu): parçalardan biri istek düzeyinde düşerse tek bir
/// sonuca sığmaz ve kabul edilmiş parçanın biletleri kaybolurdu. Parçalamayı dağıtıcı
/// yapıyor; burada sınanan sözleşme "101 mesaj → istisna, istek hiç gitmez".
/// </remarks>
public class ExpoPushGondericiTests
{
    private const string Ic = "abcdefghijklmnopqrstuv";
    private const string Token = "ExponentPushToken[" + Ic + "]";

    private readonly ToplayanGunluk _gunluk = new();
    private readonly SahteHttpUcu _uc = new();

    private (ExpoPushGonderici Gonderici, HttpClient Http) Kur(string? erisimTokeni = "robot-siri", int zamanAsimiSaniye = 1)
    {
        var http = new HttpClient(_uc)
        {
            BaseAddress = new Uri(ExpoPushGonderici.TemelAdres),
            Timeout = TimeSpan.FromSeconds(zamanAsimiSaniye),
        };
        var ayar = Options.Create(new PushOptions { Provider = "Expo", AccessToken = erisimTokeni ?? string.Empty, ZamanAsimiSaniye = zamanAsimiSaniye });
        return (new ExpoPushGonderici(http, ayar, _gunluk.Al<ExpoPushGonderici>()), http);
    }

    private static List<PushMesaji> Mesajlar()
    {
        var taslak = new BildirimTaslagi(NotificationType.NewMessage, BildirimKanallari.Mesajlar,
            new BildirimIcerigi("Yeni mesaj", "Şükrü sana yeni bir mesaj gönderdi."), "/sohbet/x", "0011223344556677", "s-aa", 100, null);
        return
        [
            BildirimYuku.Kur(taslak, Token, PushPlatform.Android),
            BildirimYuku.Kur(taslak, "ExponentPushToken[ikinci-cihaz]", PushPlatform.Ios),
        ];
    }

    private static HttpResponseMessage Json(HttpStatusCode durum, string govde)
        => new(durum) { Content = new StringContent(govde, System.Text.Encoding.UTF8, "application/json") };

    private void OkYanitla()
        => _uc.Yanit = (_, _) => Task.FromResult(Json(HttpStatusCode.OK, """{"data":[{"status":"ok","id":"t1"},{"status":"ok","id":"t2"}]}"""));

    [Fact]
    public async Task Bearer_istek_basina_eklenir_paylasilan_basliga_dokunulmaz()
    {
        var (g, http) = Kur();
        OkYanitla();

        await Task.WhenAll(g.GonderAsync(Mesajlar(), default), g.GonderAsync(Mesajlar(), default));

        Assert.Equal(2, _uc.Istekler.Count);
        Assert.All(_uc.Istekler, i => Assert.Equal("Bearer robot-siri", i.Yetki));
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Erisim_tokeni_yoksa_baslik_hic_eklenmez(string? erisimTokeni)
    {
        var (g, _) = Kur(erisimTokeni);
        OkYanitla();

        await g.GonderAsync(Mesajlar(), default);

        Assert.Null(Assert.Single(_uc.Istekler).Yetki);
    }

    [Fact]
    public async Task Govde_camelCase_dizi_ve_biletler_sirayla_doner()
    {
        var (g, _) = Kur();
        OkYanitla();

        var sonuc = await g.GonderAsync(Mesajlar(), default);

        Assert.Null(sonuc.IstekHatasi);
        Assert.Equal(new[] { "t1", "t2" }, sonuc.Biletler.Select(b => b.BiletId));

        var istek = Assert.Single(_uc.Istekler);
        Assert.Equal(ExpoPushGonderici.GonderimYolu, istek.Yol);
        using var belge = JsonDocument.Parse(istek.Govde);
        Assert.Equal(2, belge.RootElement.GetArrayLength());
        Assert.Equal(Token, belge.RootElement[0].GetProperty("to").GetString());
        Assert.False(belge.RootElement[0].TryGetProperty("collapseId", out _));
        Assert.Equal("s-aa", belge.RootElement[1].GetProperty("collapseId").GetString());
        Assert.False(belge.RootElement[1].TryGetProperty("badge", out _)); // null alan yazılmadı
        Assert.Contains("Şükrü", istek.Govde);
    }

    [Fact]
    public async Task Yuz_birinci_mesaj_istisna_istek_hic_gitmez()
    {
        var (g, _) = Kur();
        var cok = Enumerable.Repeat(Mesajlar()[0], PushSinirlari.EnFazlaMesaj + 1).ToList();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => g.GonderAsync(cok, default));
        Assert.Empty(_uc.Istekler);
    }

    [Fact]
    public async Task Tam_yuz_mesaj_tek_istekte_gider()
    {
        var (g, _) = Kur();
        _uc.Yanit = (_, _) => Task.FromResult(Json(HttpStatusCode.OK,
            "{\"data\":[" + string.Join(",", Enumerable.Range(0, 100).Select(i => $"{{\"status\":\"ok\",\"id\":\"b{i}\"}}")) + "]}"));

        var sonuc = await g.GonderAsync(Enumerable.Repeat(Mesajlar()[0], PushSinirlari.EnFazlaMesaj).ToList(), default);

        Assert.Single(_uc.Istekler);
        Assert.Equal(100, sonuc.Biletler.Count);
        Assert.Equal("b99", sonuc.Biletler[99].BiletId);
    }

    [Fact]
    public async Task Bos_liste_istek_gondermez()
    {
        var (g, _) = Kur();
        var sonuc = await g.GonderAsync([], default);
        Assert.Empty(sonuc.Biletler);
        Assert.Null(sonuc.IstekHatasi);
        Assert.Empty(_uc.Istekler);
    }

    [Fact]
    public async Task Zaman_asimi_firlatmaz_ZamanAsimi_doner()
    {
        var (g, _) = Kur(zamanAsimiSaniye: 1);
        _uc.Yanit = async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        var sonuc = await g.GonderAsync(Mesajlar(), default);

        Assert.Equal(new PushIstekHatasi(null, "ZamanAsimi", "1 sn içinde yanıt yok", null), sonuc.IstekHatasi);
        Assert.Equal(IstekKarari.Gecici, PushHataKurali.Istek(sonuc.IstekHatasi!));
    }

    [Fact]
    public async Task Ag_hatasi_firlatmaz_maskeli_AgHatasi_doner()
    {
        var (g, _) = Kur();
        _uc.Yanit = (_, _) => throw new HttpRequestException("bağlantı reddedildi " + Token);

        var sonuc = await g.GonderAsync(Mesajlar(), default);

        Assert.Equal("AgHatasi", sonuc.IstekHatasi!.Kod);
        Assert.DoesNotContain(Ic, sonuc.IstekHatasi.Mesaj);
    }

    [Fact]
    public async Task Cagiranin_iptali_firlatilir()
    {
        // Kapanış: dağıtıcı iptali görmeli, "geçici hata" sanıp satırı yeniden kuyruğa almamalı.
        var (g, _) = Kur(zamanAsimiSaniye: 10);
        _uc.Yanit = async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        using var iptal = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => g.GonderAsync(Mesajlar(), iptal.Token));
    }

    [Fact]
    public async Task Istek_duzeyi_hata_yaniti_sonuca_cevrilir()
    {
        var (g, _) = Kur();
        _uc.Yanit = (_, _) => Task.FromResult(Json(HttpStatusCode.Unauthorized, """{"errors":[{"code":"UNAUTHORIZED","message":"no"}]}"""));

        var sonuc = await g.GonderAsync(Mesajlar(), default);

        Assert.Equal(401, sonuc.IstekHatasi!.HttpDurumu);
        Assert.True(PushHataKurali.YetkiHatasi(sonuc.IstekHatasi));
    }

    // ─── Makbuzlar ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Makbuz_istegi_ids_govdesiyle_ve_Bearer_ile()
    {
        var (g, _) = Kur();
        _uc.Yanit = (_, _) => Task.FromResult(Json(HttpStatusCode.OK, """{"data":{"t1":{"status":"ok"}}}"""));

        var m = await g.MakbuzlariAlAsync(["t1", "t2"], default);

        var istek = Assert.Single(_uc.Istekler);
        Assert.Equal(ExpoPushGonderici.MakbuzYolu, istek.Yol);
        Assert.Equal("""{"ids":["t1","t2"]}""", istek.Govde);
        Assert.Equal("Bearer robot-siri", istek.Yetki);
        Assert.True(m["t1"].Basarili);
        Assert.False(m.ContainsKey("t2")); // hazır değil: sonra yeniden sorulur
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, LogLevel.Critical)]
    [InlineData(HttpStatusCode.Forbidden, LogLevel.Critical)]
    [InlineData(HttpStatusCode.InternalServerError, LogLevel.Warning)]
    public async Task Makbuz_hatasi_bos_sozluk_ve_seviyeli_gunluk(HttpStatusCode durum, LogLevel seviye)
    {
        var (g, _) = Kur();
        _uc.Yanit = (_, _) => Task.FromResult(Json(durum, $$"""{"errors":[{"code":"X","message":"{{Token}}"}]}"""));

        var m = await g.MakbuzlariAlAsync(["t1"], default);

        Assert.Empty(m);
        Assert.Equal(1, _gunluk.Say(seviye));
        Assert.All(_gunluk.Satirlar, s => Assert.DoesNotContain(Ic, s.Metin));
    }

    [Fact]
    public async Task Bininci_biletten_fazlasi_istisna()
    {
        var (g, _) = Kur();
        var biletler = Enumerable.Range(0, PushSinirlari.EnFazlaBilet + 1).Select(i => $"b{i}").ToList();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => g.MakbuzlariAlAsync(biletler, default));
        Assert.Empty(_uc.Istekler);
    }

    // ─── DI: typed client ayarları ───────────────────────────────────────────

    private static ServiceProvider Kapsayici(string saglayici, int zamanAsimi = 15)
    {
        var ayar = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=127.0.0.1;Database=kullanilmaz",
            ["ConnectionStrings:Redis"] = "",
            ["Jwt:Key"] = "birim-testi-anahtari-en-az-otuz-iki-karakter-uzun",
            ["Jwt:Issuer"] = "PeerLearn",
            ["Jwt:Audience"] = "PeerLearn",
            ["Push:Provider"] = saglayici,
            ["Push:AccessToken"] = "robot-siri",
            ["Push:DeneyimKimligi"] = "@ardaerenguler/dersmate",
            ["Push:ZamanAsimiSaniye"] = zamanAsimi.ToString(),
        }).Build();

        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton<IConfiguration>(ayar);
        s.AddApplication();
        s.AddInfrastructure(ayar);
        return s.BuildServiceProvider();
    }

    [Theory]
    [InlineData(15, 15)]
    [InlineData(60, 25)]   // kira payının (30 sn) altında kalmalı: yanıt kira bitmeden gelsin
    [InlineData(0, 1)]
    public void Typed_client_adresi_ve_zaman_asimi(int ayar, int beklenen)
    {
        using var sp = Kapsayici("Expo", ayar);
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ExpoPushGonderici));

        Assert.Equal(new Uri(ExpoPushGonderici.TemelAdres), http.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(beklenen), http.Timeout);
        Assert.True(http.Timeout < DispatchNotificationsHandler.KiraPayi);
    }

    [Fact]
    public void Typed_client_gzip_ve_deflate_acar_baglanti_sinirli()
    {
        using var sp = Kapsayici("Expo");
        HttpMessageHandler? h = sp.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(nameof(ExpoPushGonderici));
        while (h is DelegatingHandler d)
        {
            h = d.InnerHandler;
        }

        var asil = Assert.IsType<SocketsHttpHandler>(h);
        Assert.True(asil.AutomaticDecompression.HasFlag(DecompressionMethods.GZip));
        Assert.True(asil.AutomaticDecompression.HasFlag(DecompressionMethods.Deflate));
        Assert.Equal(6, asil.MaxConnectionsPerServer);
    }

    [Theory]
    [InlineData("Expo", typeof(ExpoPushGonderici))]
    [InlineData("expo", typeof(ExpoPushGonderici))]
    [InlineData("Log", typeof(LoggingPushGonderici))]
    [InlineData("", typeof(LoggingPushGonderici))]
    public void Saglayici_ayardan_secilir(string saglayici, Type beklenen)
    {
        using var sp = Kapsayici(saglayici);
        using var kapsam = sp.CreateScope();
        Assert.IsType(beklenen, kapsam.ServiceProvider.GetRequiredService<IPushGonderici>());
    }

    [Fact]
    public void Sinyal_tekil_handler_ve_is_ayni_ornegi_gorur()
    {
        using var sp = Kapsayici("Log");
        using var k1 = sp.CreateScope();
        using var k2 = sp.CreateScope();
        Assert.Same(k1.ServiceProvider.GetRequiredService<IBildirimSinyali>(), k2.ServiceProvider.GetRequiredService<IBildirimSinyali>());
        Assert.Same(sp.GetRequiredService<BildirimEtiketi>(), k1.ServiceProvider.GetRequiredService<BildirimEtiketi>());
    }
}
