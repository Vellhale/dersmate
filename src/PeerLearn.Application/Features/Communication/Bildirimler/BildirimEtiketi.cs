using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Options;

namespace PeerLearn.Application.Features.Communication.Bildirimler;

/// <summary>
/// Bildirimlerdeki opak etiketler: Android tag, iOS collapseId/threadId ve data.alici.
/// Hepsi HMAC-SHA256 kısaltması; kayıt ya da hesap kimliği (GUID) taşımaz.
/// </summary>
/// <remarks>
/// ─── NEDEN GUID DEĞİL ───────────────────────────────────────────────────────
/// Etiketler sağlayıcı başlıklarına (apns-collapse-id, FCM tag) ve cihazın bildirim
/// veritabanına iniyor. Ham sohbet/ders/kullanıcı kimliği orada kalıcı bir tanımlayıcı
/// olurdu. Kısaltılmış HMAC aynı işi görür (aynı girdi → aynı etiket, cihaz eskisinin
/// yerine koyar) ama tersine çevrilemez ve hesaplar arası bağlanamaz.
///
/// ─── ANAHTAR ────────────────────────────────────────────────────────────────
/// Mevcut Jwt:Key'den HKDF-SHA256 ile türetiliyor (info "dersmate-push-etiket-v1").
/// Yeni bir üretim sırrı gerekmez; HKDF sayesinde etiket anahtarı JWT imza anahtarından
/// kriptografik olarak ayrı: etiketlerden JWT anahtarına dair bir şey öğrenilemez.
///
/// ⚠️ Jwt:Key DEĞİŞİRSE tüm etiketler değişir. Sonuç: cihazdaki eski bildirimler yenisiyle
/// yer değiştirmez (birer kez çift görünür) ve mobilin tuttuğu "alici" etiketi uyuşmaz —
/// uygulama bir sonraki açılışta GET /push/preferences ile yenisini alana kadar gelen
/// bildirimleri "başka hesabın" sayıp yok sayar. Anahtar rotasyonunda bu beklenen bir geçiş.
///
/// ⚠️ info dizgesini değiştirmek de aynı etkiyi yapar; sürüm eki ("-v1") bunun için.
/// </remarks>
public sealed class BildirimEtiketi
{
    private const string Bilgi = "dersmate-push-etiket-v1";

    /// <summary>Etiketin hex uzunluğu: 16 hex = 64 bit. Çakışma olasılığı ihmal edilebilir.</summary>
    public const int HexUzunluk = 16;

    private readonly byte[] _anahtar;

    /// <summary>DI yolu: Jwt:Key'den.</summary>
    public BildirimEtiketi(IOptions<JwtOptions> jwt) : this(jwt.Value.Key)
    {
    }

    /// <summary>Test yolu: anahtarı doğrudan.</summary>
    public BildirimEtiketi(string jwtAnahtari)
    {
        if (string.IsNullOrEmpty(jwtAnahtari))
        {
            throw new InvalidOperationException(
                "Bildirim etiketi türetilemiyor: Jwt:Key boş. Etiketler sabit bir anahtarla üretilseydi tahmin edilebilir olurdu.");
        }

        _anahtar = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(jwtAnahtari),
            outputLength: 32,
            salt: [],
            info: Encoding.UTF8.GetBytes(Bilgi));
    }

    /// <summary>
    /// Alıcı etiketi (data.alici ve kayıt ucunun "alici" yanıtı). Mobil, telefondaki
    /// oturumun etiketiyle uyuşmayan bildirimi yok sayar: aynı telefonda hesap değişince
    /// önceki hesabın geç gelen bildirimi yanlış kişiye yönlendirmez.
    /// </summary>
    public string Alici(Guid kullaniciId) => Hmac("alici", kullaniciId.ToString("D"));

    /// <summary>Sohbet başına: "s-…". Yeni mesaj bildirimi aynı sohbetin eskisinin yerine geçer.</summary>
    public string Sohbet(Guid sohbetId) => "s-" + Hmac("sohbet", sohbetId.ToString("D"));

    /// <summary>
    /// İstek çifti başına: "i-…". Aynı kişiden gelen konu istekleri cihazda TEK bildirim
    /// olarak kalır (taciz yüzeyini daraltır). Sıra önemli: gönderen → alan.
    /// </summary>
    public string IstekCifti(Guid gonderenId, Guid alanId)
        => "i-" + Hmac("istek", $"{gonderenId:D}:{alanId:D}");

    /// <summary>Alıcı başına günlük özet: "id-…". Önceki günün özetinin yerine geçer.</summary>
    public string IstekOzeti(Guid aliciId) => "id-" + Hmac("istekOzeti", aliciId.ToString("D"));

    /// <summary>
    /// Ders başına: "d-…". Rezervasyon, yaklaşan ders, onay ve İPTAL aynı etiketi taşır:
    /// iptal bildirimi kilit ekranındaki "1 saat sonra başlıyor"un yerine geçer.
    /// </summary>
    public string Ders(Guid dersId) => "d-" + Hmac("ders", dersId.ToString("D"));

    /// <summary>Test bildirimi: "t-…".</summary>
    public string Test(Guid satirId) => "t-" + Hmac("test", satirId.ToString("D"));

    /// <summary>
    /// HMAC-SHA256(anahtar, tür + "|" + değer), ilk <see cref="HexUzunluk"/> hex, küçük harf.
    /// Tür girdinin parçası: aynı GUID farklı türlerde farklı etiket verir (alan ayrımı).
    /// </summary>
    private string Hmac(string tur, string deger)
    {
        var ozet = HMACSHA256.HashData(_anahtar, Encoding.UTF8.GetBytes(tur + "|" + deger));
        return Convert.ToHexString(ozet, 0, HexUzunluk / 2).ToLowerInvariant();
    }
}
