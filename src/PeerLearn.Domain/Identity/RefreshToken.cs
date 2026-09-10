using System.Security.Cryptography;
using System.Text;
using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Identity;

/// <summary>
/// Yenileme token'ı — erişim token'ı öldüğünde kullanıcıyı parola sormadan içeride
/// tutan, DÖNÜŞÜMLÜ (her kullanımda değişen) ve İPTAL EDİLEBİLİR taşıyıcı.
/// </summary>
/// <remarks>
/// ─── NEDEN ŞİMDİ, VE NEDEN "DURUMSUZ" TERCİHİ TERK EDİLİYOR ──────────────────
/// Bu yokluk bilinçli bir tasarım kararı DEĞİLDİ; üç yerde "bilinen sınır" olarak
/// yazılıydı ve ikisi çözümü adıyla öneriyordu (<c>AccountStatusMiddleware</c> sınıf
/// yorumu, <c>ParolaSifirlama</c> §"her yerden çıkış", <c>docs/SUNUCUYA-KURULUM.md</c>
/// bilinen sınırlar). Purpose token'lardaki "durumsuz kalalım" gerekçesi ise güvenlik
/// değil kolaylıktı ("DB tablosu gerektirmez") — ve proje aynı tercihi daha önce İKİ KEZ
/// terk etti: parola sıfırlama token'ı parola damgasına bağlandı, e-posta doğrulama
/// 2026-09-02'de durumsuz bağlantıdan User satırındaki kolonlara taşındı.
///
/// ─── TOKEN BİR SIR, KOD DEĞİL — HASH'LEME BURADA CİDDİ ───────────────────────
/// <see cref="EmailVerificationRules"/>'ta hash "zayıf ama bedelsiz bir katman" diye
/// anlatılıyor, çünkü altı hanelik bir uzayda gökkuşağı tablosu anında üretilir. Burada
/// durum tersine: <see cref="TokenBytes"/> baytlık kriptografik rastgelelik, tahmin
/// edilemez. Veritabanını okuyabilen biri (yedek, günlük, destek ekranı) hash'lerden
/// token üretemez — bu yüzden hash gerçek bir savunma.
///
/// ⚠️ HASH TUZLANMIYOR ve bu bilinçli bir FARK. E-posta kodunda hash
/// <c>SHA-256(userId + ":" + kod)</c> idi, çünkü altı hane kullanıcıya özgü değil.
/// Burada arama TERS yönde çalışıyor: elimizde yalnızca token var, hangi kullanıcıya ait
/// olduğunu HENÜZ BİLMİYORUZ — satırı bulmanın tek yolu hash üzerinden sorgulamak.
/// Tuzlansaydı sorgu kurulamazdı. Tokenin kendisi 256 bit olduğu için tuza gerek de yok.
///
/// ─── DÖNÜŞÜM (ROTATION) VE HIRSIZLIK TESPİTİ ────────────────────────────────
/// Her yenilemede eski token iptal edilip yenisi veriliyor. Zaten iptal edilmiş bir
/// token yeniden sunulursa bu iki şeyden biri demektir: ya token çalındı ve iki taraf
/// birden kullanıyor, ya da istemci aynı token'la iki kez denedi. İkisini ayırt etmenin
/// güvenli yolu yok, bu yüzden davranış tektir: o kullanıcının TÜM zinciri iptal edilir.
/// Yanlış pozitifin bedeli "yeniden giriş yap", yanlış negatifin bedeli "hırsız içeride
/// kalır" — asimetri açık.
///
/// ─── CİHAZ BAĞI: KAYDEDİLİYOR, ZORUNLU TUTULMUYOR ───────────────────────────
/// <see cref="DeviceHwidHash"/> token üretilirken yazılıyor ama doğrulamada
/// KARŞILAŞTIRILMIYOR. Gerekçe: web'de HWID tarayıcı parmak izinden türüyor
/// (<c>frontend/src/lib/hwid.js</c>) ve bir tarayıcı/işletim sistemi güncellemesi onu
/// değiştirebilir. Zorunlu tutulsaydı böyle bir güncelleme TÜM web kullanıcılarını aynı
/// anda dışarı atardı ve bunu önceden sınamanın yolu yok. Alan şimdilik iz amaçlı:
/// şüpheli kullanımı görmeyi sağlıyor. Mobilde cihaz kimlikleri sabit olduğu için
/// zorunluluk ORADA açılmalı — açarken bu paragrafı güncelle.
/// </remarks>
public static class RefreshTokenRules
{
    /// <summary>
    /// Ham token'ın bayt uzunluğu. 32 bayt = 256 bit; base64url'de 43 karakter.
    /// </summary>
    public const int TokenBytes = 32;

    /// <summary>
    /// Yenileme token'ının ömrü. Kullanıcı bu süre boyunca hiç dokunmazsa yeniden giriş
    /// yapar. 60 gün, mobil uygulamada "beni hatırla" beklentisini karşılayacak kadar
    /// uzun, terk edilmiş bir cihazın süresiz açık kalmayacağı kadar kısa.
    /// </summary>
    public const int ValidityDays = 60;

    /// <summary>SHA-256 hex = tam 64 karakter; kolon sınırı da tam bu.</summary>
    public const int HashLength = 64;

    /// <summary>
    /// Kriptografik rastgele token. Base64url (dolgusuz) — URL ve JSON güvenli,
    /// kaçış gerektirmiyor.
    /// </summary>
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenBytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>
    /// Saklanan/aranan biçim: SHA-256(token), küçük harf hex.
    /// TUZ YOK — gerekçesi sınıf açıklamasında (arama token'dan kullanıcıya doğru).
    /// </summary>
    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

/// <summary>
/// Tek bir yenileme token'ının kaydı. Ham token BURADA YOK — yalnızca
/// <see cref="TokenHash"/>.
/// </summary>
public class RefreshToken : BaseEntity
{
    public Guid UserId { get; set; }

    /// <summary>SHA-256(token) hex. Aramanın tek girişi; tekil.</summary>
    public string TokenHash { get; set; } = null!;

    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// İptal anı. NULL = aktif. Süresi dolmuş ama iptal edilmemiş token da NULL taşır —
    /// "aktif" sorgusu bu yüzden HEM <c>RevokedAtUtc IS NULL</c> HEM süre kontrolü ister.
    /// </summary>
    /// <remarks>
    /// ⚠️ Kısmi index filtresi <c>"RevokedAtUtc" IS NULL</c> ile yazıldı. Sorguda
    /// <c>RevokedAtUtc == null</c> yerine başka bir ifade (ör. bir durum enum'u) kullanmak
    /// index'i SESSİZCE devre dışı bırakır: sonuç doğru döner, tablo taranır.
    /// Süre koşulu index filtresine KONULAMAZ — <c>now()</c> immutable değil, PostgreSQL
    /// kısmi index'te kabul etmez.
    /// </remarks>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>Neden iptal edildi — denetim ve teşhis için.</summary>
    /// <remarks>
    /// Enum METİN olarak saklanıyor (<c>HasConversion&lt;string&gt;</c>, 20 karakter).
    /// Yeni üye eklemek göç gerektirmez; bir üyeyi VERİSİNDEN ÖNCE silmek, o satırlar
    /// okunurken uzak bir yerde patlar (CLAUDE.md, dokunulmaz bölüm).
    /// </remarks>
    public RefreshTokenRevokeReason? RevokeReason { get; set; }

    /// <summary>
    /// Dönüşümde bu token'ın yerine geçen satır. Zinciri geriye doğru izlemeyi ve
    /// yeniden kullanım tespitinde tüm aileyi iptal etmeyi sağlar.
    /// </summary>
    public Guid? ReplacedByTokenId { get; set; }

    /// <summary>
    /// Token üretilirken görülen cihaz parmak izi. KARŞILAŞTIRILMIYOR — gerekçe
    /// <see cref="RefreshTokenRules"/> açıklamasında.
    /// </summary>
    public string? DeviceHwidHash { get; set; }

    public DateTime? LastUsedAtUtc { get; set; }

    public bool AktifMi(DateTime now) => RevokedAtUtc is null && ExpiresAtUtc > now;
}

/// <summary>Yenileme token'ının neden iptal edildiği.</summary>
public enum RefreshTokenRevokeReason
{
    /// <summary>Normal dönüşüm: kullanıldı, yerine yenisi verildi.</summary>
    Rotated = 0,

    /// <summary>Kullanıcı çıkış yaptı.</summary>
    SignedOut = 1,

    /// <summary>Parola değişti — tüm oturumlar düşürüldü.</summary>
    PasswordChanged = 2,

    /// <summary>Hesap silindi.</summary>
    AccountDeleted = 3,

    /// <summary>Yaptırım (ban/askı) uygulandı.</summary>
    Sanctioned = 4,

    /// <summary>
    /// İptal edilmiş bir token yeniden sunuldu — hırsızlık şüphesiyle tüm zincir iptal.
    /// </summary>
    ReuseDetected = 5,
}
