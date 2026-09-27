using System.Globalization;
using System.Text.Json;
using PeerLearn.Application.Features.Community;
using PeerLearn.Domain.Community;
using Xunit;

namespace PeerLearn.UnitTests;

/// <summary>
/// Topluluk akışındaki ilk yorum önizlemesi. Hangi yorumun seçildiği (Visible, engel,
/// perdeli gönderi) gerçek veritabanında sınanıyor: tools/e2e-topluluk.ps1. Burada metin
/// kuralı ve sözleşmenin biçimi var.
/// </summary>
public class ForumOnizlemeTests
{
    [Fact]
    public void Kisa_metin_aynen_doner()
    {
        Assert.Equal("Bence önce paragraf çöz.", ForumOnizleme.Metin("Bence önce paragraf çöz."));
    }

    [Fact]
    public void Satir_sonlari_ve_art_arda_bosluk_tek_bosluga_iner()
    {
        // Kart iki satır gösteriyor: gövdedeki boş satırlar önizlemeyi boşa harcamamalı.
        Assert.Equal("İlk satır ikinci satır son",
            ForumOnizleme.Metin("  İlk satır\r\n\r\n  ikinci\tsatır\n\nson  "));
    }

    [Fact]
    public void Sinirdaki_metin_kesilmez()
    {
        var tam = new string('a', ForumOnizleme.EnFazlaGrafem);

        Assert.Equal(tam, ForumOnizleme.Metin(tam));
    }

    [Fact]
    public void Uzun_metin_sinirda_uc_noktayla_kesilir()
    {
        var sonuc = ForumOnizleme.Metin(new string('a', 1000));

        Assert.Equal(ForumOnizleme.EnFazlaGrafem, new StringInfo(sonuc).LengthInTextElements);
        Assert.EndsWith("…", sonuc);
    }

    [Fact]
    public void Emoji_ortadan_bolunmez()
    {
        // 👨‍👩‍👧 tek grafem ama beş UTF-16 birimi (vekil çiftleri + ZWJ). Birim sayısıyla
        // kesilseydi yarım emoji (bozuk vekil) istemciye giderdi.
        const string aile = "👨‍👩‍👧";
        var sonuc = ForumOnizleme.Metin(string.Concat(Enumerable.Repeat(aile, 300)));

        var grafemler = new StringInfo(sonuc);
        Assert.Equal(ForumOnizleme.EnFazlaGrafem, grafemler.LengthInTextElements);
        for (var i = 0; i < grafemler.LengthInTextElements - 1; i++)
        {
            Assert.Equal(aile, grafemler.SubstringByTextElements(i, 1));
        }
        Assert.EndsWith("…", sonuc);
    }

    [Fact]
    public void FirstComment_sona_ve_varsayilan_null_ile_eklendi()
    {
        // YAPISAL: alan konumsal kurucunun SONUNDA ve isteğe bağlı. Ortaya eklenseydi
        // mevcut çağrı noktaları derlenmeye devam edip alanları kaydırabilirdi.
        var parametreler = typeof(ForumPostDto).GetConstructors().Single().GetParameters();
        var son = parametreler[^1];

        Assert.Equal("FirstComment", son.Name);
        Assert.Equal(typeof(ForumCommentPreviewDto), son.ParameterType);
        Assert.True(son.HasDefaultValue);
        Assert.Null(son.DefaultValue);
    }

    [Fact]
    public void Json_sozlesmesi_istemcilerin_okudugu_adlarla()
    {
        // Denetleyiciler ASP.NET varsayılanıyla (Web: camelCase) yazıyor.
        var secenek = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var yazar = new ForumAuthorDto(Guid.Empty, "Ayşe", 3, false);
        var tarih = new DateTime(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc);

        var onizlemeli = new ForumPostDto(Guid.Empty, ForumTag.Question, "Başlık", "Gövde", yazar,
            tarih, 1, 0, 2, 0, false, 0,
            new ForumCommentPreviewDto(Guid.Empty, "İlk yorum", yazar, tarih));
        using var belge = JsonDocument.Parse(JsonSerializer.Serialize(onizlemeli, secenek));
        var ilk = belge.RootElement.GetProperty("firstComment");

        Assert.Equal("İlk yorum", ilk.GetProperty("body").GetString());
        Assert.True(ilk.TryGetProperty("commentId", out _));
        Assert.True(ilk.TryGetProperty("createdAtUtc", out _));
        Assert.Equal("Ayşe", ilk.GetProperty("author").GetProperty("displayName").GetString());

        // Önizlemesiz gönderide alan VAR ve null (yok sayılmıyor): istemci tek koşula bakar.
        var onizlemesiz = new ForumPostDto(Guid.Empty, ForumTag.Question, "Başlık", "Gövde", yazar,
            tarih, 1, 0, 0, 0, false, 0);
        using var bos = JsonDocument.Parse(JsonSerializer.Serialize(onizlemesiz, secenek));

        Assert.Equal(JsonValueKind.Null, bos.RootElement.GetProperty("firstComment").ValueKind);
    }
}
