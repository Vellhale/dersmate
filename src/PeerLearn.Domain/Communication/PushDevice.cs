using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Communication;

/// <summary>
/// Bir kullanıcının bir cihazdaki Expo push token'ı. (Kullanıcı, cihaz) başına TEK satır.
/// </summary>
/// <remarks>
/// ─── NEDEN identity.UserDevices'A EKLENMEDİ ─────────────────────────────────
/// UserDevices moderasyon kaydıdır: banlı hesabın satırı HWID banı için bilerek
/// korunuyor (DeleteAccount). Push token'ının böyle bir değeri yok, tersine: hesap
/// kapanınca, çıkış yapılınca, ban gelince HEMEN silinmesi gerekiyor. İki farklı ömür
/// tek satırda yaşayamazdı.
///
/// ─── SİLME HER AKIŞTA ELLE ──────────────────────────────────────────────────
/// Users'a FK Cascade yazıldı ama ona GÜVENİLMİYOR: hesap silme bu üründe Users satırını
/// silmiyor, anonimleştiriyor. Silmeyi yapan yerler: çıkış (tek cihaz), her yerden çıkış
/// ve onu kullanan akışlar (TumOturumlariDusurAsync), hesap silme, kalıcı ban,
/// DeviceNotRegistered (bilette ya da makbuzda; satır o kanıttan sonra yeniden kaydedildiyse
/// SİLİNMEZ, bkz. OluCihaz), çevrimdışı çıkıştan sonra "forget" ucu ve günlük temizlik
/// (CleanupNotifications: oturum bağı kopmuş, 5 günden uzun süredir kaydını yenilememiş
/// cihaz — uygulamayı çıkış yapmadan silen kullanıcının satırı başka hiçbir yoldan gitmiyordu).
///
/// ─── GÖNDERİM ŞARTI: OTURUM BAĞI ────────────────────────────────────────────
/// Satırın var olması tek başına gönderim izni DEĞİL. Bu (UserId, HwidHash) için EN YENİ
/// yenileme token'ı aktif değilse cihaz "bağlı" sayılmaz (OturumBagi). Böylece silme
/// adımlarından biri kaçsa bile oturumu kapanmış cihaza bildirim gitmez.
/// </remarks>
public class PushDevice : BaseEntity
{
    public Guid UserId { get; set; }

    /// <summary>
    /// <c>ExponentPushToken[...]</c>. Tekil: aynı telefonda hesap değişince satır yeni
    /// kullanıcıya TAŞINIR (eski kullanıcı o telefona bildirim almaya devam etmesin).
    /// </summary>
    /// <remarks>⚠️ Tam hâliyle ASLA loglanmaz; yalnızca son 6 karakteri.</remarks>
    public string Token { get; set; } = null!;

    public PushPlatform Platform { get; set; }

    /// <summary>
    /// <c>HwidKurali.Normalize</c>'dan geçmiş cihaz özeti. RefreshTokens.DeviceHwidHash
    /// ile AYNI biçim ve uzunluk — oturum bağı bu iki değerin eşitliğine dayanıyor.
    /// </summary>
    public string HwidHash { get; set; } = null!;

    /// <summary>
    /// Android'de kullanıcının telefon ayarlarından kapattığı kanal kimlikleri. Dağıtıcı,
    /// alıcının tüm cihazlarında kanal kapalıysa satırı Skipped(KanalKapali) yapar.
    /// iOS'ta kanal yok, dizi hep boş.
    /// </summary>
    public string[] KapaliKanallar { get; set; } = [];

    public DateTime LastSeenAtUtc { get; set; }
}
