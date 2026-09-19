using ImageMagick;
using PeerLearn.Application.Abstractions;

namespace PeerLearn.Infrastructure.Services;

/// <summary>
/// Görsel metadata temizliğinin Magick.NET tabanlı uygulaması.
///
/// AKIŞ: çöz → AutoOrient (EXIF yönelimini piksele işle) → Strip (tüm metadata'yı sil) →
/// aynı formatta yeniden kodla. SIRA KRİTİK: önce AutoOrient SONRA Strip. Ters sırada ya da
/// yalnızca Strip yapılsaydı telefon fotoğrafının yönelim tag'i silinir ama pikseller
/// döndürülmez, görsel yan/ters görünürdü.
///
/// Strip() EXIF/GPS/XMP/IPTC/yorum VE gömülü thumbnail'i hep birlikte kaldırır — thumbnail
/// önemli, çünkü kırpılmamış orijinal görselin gömülü bir kopyasını taşıyabiliyordu.
/// ICC renk profili de silinir; avatar (istemcide küçültülmüş) ve ekran görüntüsü kanıdı
/// için ihmal edilebilir, gizlilik tam-strip lehine tercih edildi.
///
/// Stateless ve thread-safe → Singleton kayıtlı (IProofStorage ile aynı ömür).
/// </summary>
public sealed class MagickGorselTemizleyici : IGorselTemizleyici
{
    static MagickGorselTemizleyici()
    {
        // Decompression-bomb / piksel-taşması savunması: minik dosya + devasa boyut
        // saldırısını sınırlar. Aşan girdi MagickException'a düşer, TryTemizle false döner.
        ResourceLimits.Width = 20000;
        ResourceLimits.Height = 20000;
    }

    public bool TryTemizle(byte[] icerik, string contentType, out byte[] temiz)
    {
        // contentType imza tutarlılığı/ileride format zorlamak için sözleşmede duruyor;
        // Magick formatı içerikten çıkardığı için burada şart değil.
        temiz = Array.Empty<byte>();

        // Boş/null baytlarda Magick MagickException DEĞİL ArgumentException fırlatır; onu
        // yakalamak yerine burada erken-red ediyoruz ki catch yalnızca gerçek çözümleme
        // hatalarını kapsasın ve beklenmedik bir istisna çağırana sızmasın.
        if (icerik is null || icerik.Length == 0)
        {
            return false;
        }

        try
        {
            using var image = new MagickImage(icerik); // format içerikten çıkarılır
            image.AutoOrient();                          // önce yönelimi piksele işle
            image.Strip();                               // sonra tüm metadata'yı sil
            temiz = image.ToByteArray();                 // aynı formatta yeniden kodla
            return true;
        }
        catch (MagickException)
        {
            return false;
        }
    }
}
