using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Scheduling;

/// <summary>
/// Ders kanıtı (Proof of Session): sistem saati + katılımcı listesi görünen ekran görüntüsü.
/// Sha256Hash, aynı görselin farklı derslerde tekrar kullanılmasını (sahte kanıt) tespit etmek içindir.
/// Reddedilen kanıttan sonra yeni kanıt yüklenebilir; bu yüzden SessionId unique değildir.
/// </summary>
public class SessionProof : BaseEntity
{
    public Guid SessionId { get; set; }
    public Guid UploadedByUserId { get; set; }

    /// <summary>Nesne depolamadaki anahtar (dosyanın kendisi DB'de tutulmaz).</summary>
    public string StorageKey { get; set; } = null!;

    public string ContentType { get; set; } = null!;
    public long FileSizeBytes { get; set; }
    public string Sha256Hash { get; set; } = null!;

    public ProofStatus Status { get; set; } = ProofStatus.Pending;

    /// <summary>
    /// Aynı SHA-256 hash'i daha önce BAŞKA bir derste görüldüyse true — sahte kanıt sinyali.
    /// Yükleme anında hesaplanıp KALICI yazılır; yalnızca admin görür (hilekâra ifşa edilmez).
    /// </summary>
    public bool IsDuplicateHash { get; set; }

    /// <summary>
    /// Görselin depodan silindiği an; null ise dosya hâlâ duruyor.
    /// </summary>
    /// <remarks>
    /// SATIR SİLİNMEZ, YALNIZCA DOSYA SİLİNİR — ayrım kasıtlı.
    ///
    /// Sahte kanıt tespiti <see cref="Sha256Hash"/> geçmişine dayanıyor: bir görselin daha
    /// önce başka bir derste kullanıldığını ancak eski hash'ler durursa bilebiliriz. Satırı
    /// silmek, hilekâra "yeterince bekle, aynı ekran görüntüsünü yeniden kullanabilirsin"
    /// demek olurdu. Hash 64 karakter, görsel megabaytlar: pahalı olanı atıp ucuz olanı
    /// sonsuza dek saklamak doğru takas.
    ///
    /// Damga aynı zamanda 404 ile "hiç yüklenmemiş"i ayırır: arayüz "kanıt yok" yerine
    /// "kanıt saklama süresi doldu" diyebilir.
    /// </remarks>
    public DateTime? ContentDeletedAtUtc { get; set; }
}
