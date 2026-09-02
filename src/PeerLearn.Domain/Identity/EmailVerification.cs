using System.Security.Cryptography;
using System.Text;

namespace PeerLearn.Domain.Identity;

/// <summary>
/// E-posta doğrulama KODU — bağlantının yerini aldı (2026-09-02, ürün sahibi kararı).
/// </summary>
/// <remarks>
/// ─── NEDEN BAĞLANTI DEĞİL KOD ────────────────────────────────────────────────
/// İkisi de aynı şeyi kanıtlıyor: adrese erişim. Değişimin gerekçesi güvenlik değil
/// TESLİM EDİLEBİLİRLİK ve mobil kullanım:
///   • Türkiye'deki birçok sağlayıcı, tanımadığı alan adından gelen bağlantılı postayı
///     spam'e atıyor ya da bağlantıyı kesiyor; altı haneli bir sayı bu filtrelere
///     takılmıyor.
///   • Telefonda posta uygulamasından tarayıcıya geçip geri dönmek, formu terk etmek
///     demek. Kod, kullanıcıyı kayıt ekranından hiç çıkarmıyor.
///
/// ─── KOD BİR SIR DEĞİL, BİR KAPIDIR ──────────────────────────────────────────
/// Altı hane = 1.000.000 olasılık. Bu, çevrimdışı kaba kuvvete karşı HİÇBİR ŞEY ifade
/// etmez; korumayı sağlayan üç şey birlikte çalışıyor:
///   1. SÜRE — <see cref="ValidityMinutes"/> dakika sonra ölüyor
///   2. DENEME SAYISI — <see cref="MaxAttempts"/> yanlıştan sonra kod iptal
///   3. HIZ SINIRI — uç, IP başına dakikada 10 istekle sınırlı (RateLimit:AuthPerMinute)
///
/// Üçünden biri kalkarsa kod kırılabilir hâle gelir. Özellikle deneme sayacı:
/// olmasaydı 1.000.000 denemeyi hız sınırı bile yeterince yavaşlatamazdı (10/dk ile
/// ~70 gün, ama saldırgan binlerce IP kullanır).
///
/// KOD VERİTABANINDA HASH'LENİYOR. Altı hanelik bir değeri hash'lemek kaba kuvveti
/// engellemiyor (10^6 tablosu anında üretilir) — amaç farklı: veritabanını okuyabilen
/// biri (yedek dosyası, log, destek ekranı) kodları DOĞRUDAN kullanamasın. Zayıf ama
/// bedelsiz bir katman; güçlü sanılmamalı.
/// </remarks>
public static class EmailVerificationRules
{
    /// <summary>Kod uzunluğu (hane).</summary>
    public const int CodeLength = 6;

    /// <summary>Kodun geçerlilik süresi.</summary>
    public const int ValidityMinutes = 15;

    /// <summary>
    /// Bu kadar yanlış denemeden sonra kod iptal olur; kullanıcı yenisini istemeli.
    /// </summary>
    public const int MaxAttempts = 5;

    /// <summary>
    /// İki kod isteği arasında beklenmesi gereken süre.
    /// </summary>
    /// <remarks>
    /// Hız sınırı IP başına; bu bekleme HESAP başına. İkisi farklı saldırıyı kapatıyor:
    /// hız sınırı bir IP'nin uca yüklenmesini, bekleme ise bir ADRESE posta yağdırılmasını
    /// (mail bombing) engelliyor — saldırgan IP değiştirerek hız sınırını aşabilir ama
    /// hedef hesap yine dakikada bir posta alır.
    /// </remarks>
    public const int ResendCooldownSeconds = 60;

    /// <summary>
    /// Kriptografik rastgele, <see cref="CodeLength"/> haneli kod.
    /// </summary>
    /// <remarks>
    /// ⚠️ Random ya da Guid KULLANILMAZ: ikisi de tahmin edilebilir. Doğrulama kodu
    /// tahmin edilebilirse e-posta sahipliği hiç kanıtlanmamış olur — bu mekanizmanın
    /// var oluş sebebi ortadan kalkar.
    ///
    /// Modulo sapması: RandomNumberGenerator.GetInt32 üst sınırı dışlayarak ÜNİFORM
    /// dağıtım veriyor, elle "% 1000000" yapılsaydı küçük değerler biraz daha sık
    /// çıkardı. Altı hanede sapma önemsiz görünür ama düzeltmesi de bedava.
    ///
    /// Baştaki sıfırlar korunuyor (D6): "004271" beş haneye düşerse kullanıcı
    /// e-postadaki kodu birebir yazdığında eşleşme tutmaz.
    /// </remarks>
    public static string GenerateCode()
        => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString($"D{CodeLength}");

    /// <summary>
    /// Kodun veritabanında saklanan biçimi: SHA-256(userId + ":" + kod), hex.
    /// </summary>
    /// <param name="userId">
    /// Tuz yerine geçiyor. Olmasaydı aynı kodu alan iki kullanıcının hash'i AYNI olurdu
    /// ve veritabanını gören biri "şu iki hesabın kodu aynı" bilgisinden yararlanabilirdi.
    /// </param>
    public static string HashCode(Guid userId, string code)
    {
        var bytes = Encoding.UTF8.GetBytes($"{userId:D}:{code}");
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    /// <summary>
    /// Sabit zamanlı karşılaştırma.
    /// </summary>
    /// <remarks>
    /// Zamanlama saldırısı bu senaryoda gerçekçi değil (deneme sayacı 5'te kesiyor) ama
    /// karşılaştırmayı doğru yapmanın maliyeti sıfır ve "== ile karşılaştırdık, sonra
    /// başka bir yerde ısırdı" bu projede tekrarlanmak istenmeyen bir hikâye.
    /// </remarks>
    public static bool HashesMatch(string? saklanan, string hesaplanan)
    {
        if (string.IsNullOrEmpty(saklanan) || saklanan.Length != hesaplanan.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(saklanan), Encoding.ASCII.GetBytes(hesaplanan));
    }
}
