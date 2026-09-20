using System.Text;
using ImageMagick;
using PeerLearn.Infrastructure.Services;
using Xunit;

namespace PeerLearn.UnitTests;

/// <summary>
/// MagickGorselTemizleyici birim testleri — DB/API gerekmez, saf sınıf çağrılır.
///
/// KANIT STANDARDI MUTASYON (CLAUDE.md):
///   • image.Strip() kaldırılırsa   → EXIF/GPS/sentinel testleri KIRILIR
///   • image.AutoOrient() kaldırılırsa → yönelim (boyut takası) testi KIRILIR
/// </summary>
public class GorselTemizleyiciTests
{
    private const string Sentinel = "DERSMATE-KONUM-SENTINEL";

    private static readonly MagickGorselTemizleyici Temizleyici = new();

    /// <summary>EXIF (GPS konumu + sentinel) gömülü, verilen boyutta bir JPEG üretir.</summary>
    private static byte[] SentinelliJpeg(uint width, uint height)
    {
        using var image = new MagickImage(MagickColors.SteelBlue, width, height)
        {
            Format = MagickFormat.Jpeg
        };

        var exif = new ExifProfile();
        // Konum sızıntısı: gerçek GPS koordinatları (İstanbul ~41N, 29E).
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, new[] { new Rational(41.0), new Rational(1.0), new Rational(2.0) });
        exif.SetValue(ExifTag.GPSLongitudeRef, "E");
        exif.SetValue(ExifTag.GPSLongitude, new[] { new Rational(29.0), new Rational(0.0), new Rational(1.0) });
        // ASCII olarak dosyada aranabilir bir imza: temizlik sonrası kaybolmalı.
        exif.SetValue(ExifTag.ImageDescription, Sentinel);
        image.SetProfile(exif);

        return image.ToByteArray();
    }

    /// <summary>EXIF yönelim tag'i (Orientation) gömülü, verilen boyutta bir JPEG üretir.</summary>
    private static byte[] YonelimliJpeg(uint width, uint height, ushort orientation)
    {
        using var image = new MagickImage(MagickColors.SteelBlue, width, height)
        {
            Format = MagickFormat.Jpeg
        };

        // İKİSİ birden: Magick yazarken image.Orientation'ı EXIF'e SENKRONLUYOR; yalnız EXIF
        // tag'i verilirse Undefined olan image.Orientation onu eziyor. Yalnız image.Orientation
        // verilirse de (EXIF profili yokken) dosyaya hiç yazılmıyor. İkisi de RightTop(6) olmalı.
        image.Orientation = (OrientationType)orientation;
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Orientation, orientation);
        image.SetProfile(exif);

        return image.ToByteArray();
    }

    private static string Latin1(byte[] b) => Encoding.Latin1.GetString(b);

    [Fact]
    public void Temiz_ciktida_exif_gps_ve_sentinel_kalmaz_format_ve_boyut_korunur()
    {
        var kirli = SentinelliJpeg(10, 6);

        // Kurulum gerçekten kirli mi? (aksi halde test yanlış nedenle geçer)
        using (var girdi = new MagickImage(kirli))
        {
            Assert.NotNull(girdi.GetExifProfile());
        }
        Assert.Contains(Sentinel, Latin1(kirli));

        var ok = Temizleyici.TryTemizle(kirli, "image/jpeg", out var temiz);

        Assert.True(ok);

        using var cikti = new MagickImage(temiz);
        // (b) YAPISAL: hiçbir EXIF profili kalmadı — GPS de bunun içindeydi.
        Assert.Null(cikti.GetExifProfile());
        // (c) İÇERİK: sentinel ve EXIF APP1 imzası ("Exif") ham baytlarda yok.
        Assert.DoesNotContain(Sentinel, Latin1(temiz));
        Assert.DoesNotContain("Exif", Latin1(temiz));
        // (d) Aynı format, aynı boyut (180° olmayan bir dönüş yok).
        Assert.Equal(MagickFormat.Jpeg, cikti.Format);
        Assert.Equal(10u, cikti.Width);
        Assert.Equal(6u, cikti.Height);
    }

    [Fact]
    public void AutoOrient_yonelimi_piksele_isler_boyutlar_takas_olur()
    {
        // 4x8 dikey görsel, EXIF yönelimi RightTop (6): gösterim için 90° döndürülmeli.
        var kirli = YonelimliJpeg(4, 8, orientation: 6);

        // Kurulum doğrulaması: kirli görsel gerçekten RightTop ve 4x8 mi?
        using (var girdi = new MagickImage(kirli))
        {
            Assert.Equal(OrientationType.RightTop, girdi.Orientation);
            Assert.Equal(4u, girdi.Width);
            Assert.Equal(8u, girdi.Height);
        }

        var ok = Temizleyici.TryTemizle(kirli, "image/jpeg", out var temiz);
        Assert.True(ok);

        using var cikti = new MagickImage(temiz);
        // AutoOrient pikselleri fiziksel döndürdüğü için boyutlar TAKAS olur: 4x8 -> 8x4.
        // AutoOrient() kaldırılırsa (yalnız Strip kalırsa) boyutlar 4x8 kalır ve bu KIRILIR.
        Assert.Equal(8u, cikti.Width);
        Assert.Equal(4u, cikti.Height);
        // Strip yönelim tag'ini de sildiği için çıktı yönelimi tanımsız/TopLeft olmalı.
        Assert.True(cikti.Orientation is OrientationType.Undefined or OrientationType.TopLeft);
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 })] // görsel değil
    [InlineData(new byte[0])]                                  // boş
    public void Cozulemeyen_baytlar_false_doner(byte[] bozuk)
    {
        var ok = Temizleyici.TryTemizle(bozuk, "image/jpeg", out var temiz);

        Assert.False(ok);
        Assert.Empty(temiz);
    }
}
