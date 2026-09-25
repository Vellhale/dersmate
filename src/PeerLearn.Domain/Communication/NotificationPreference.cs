using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Communication;

/// <summary>
/// Kullanıcı başına tek satır: dört bildirim kategorisi, aydınlatma damgası ve izin
/// sorusunun erteleme durumu. Satır YOKSA tembel varsayılan geçerli: dördü açık, damga yok.
/// </summary>
/// <remarks>
/// ─── NEDEN identity.UserPreferences'A EKLENMEDİ ──────────────────────────────
/// O satır xmin korumalı ve KVKK rıza CHECK'ini taşıyor. Buradaki yazımlar TEK SÜTUNLUK
/// SQL upsert (<c>INSERT … ON CONFLICT ("UserId") DO UPDATE SET "&lt;sütun&gt;" = …</c>):
/// aynı satırda dursalardı her anahtar dokunuşu rıza satırının xmin'ini oynatır ve
/// eşzamanlı bir rıza kaydını çakıştırırdı. Bu yüzden xmin de YOK — tam gövdeli yazım
/// yapılmadığı için korunacak bir "okuduğum sürüm" yok; farklı sütunlara giden eşzamanlı
/// istekler birbirini ezmez.
///
/// ─── NEDEN SUNUCUDA ─────────────────────────────────────────────────────────
/// Mobilde cihaza yazılan her yeni tercih, izin kategorisi ve IZIN_SURUMU artışı demek.
/// Ayrıca tercih cihaz değişince korunmalı ve kuyrukta bekleyen bildirimi de etkilemeli:
/// dağıtıcı tercihi GÖNDERİM ANINDA okur.
///
/// ⚠️ Dört anahtarın DB varsayılanı TRUE. Tek sütunluk upsert satırı ilk kez yaratırken
/// diğer üç sütunu vermez; varsayılan false olsaydı bir anahtarı açmak diğer üçünü
/// sessizce kapatırdı.
/// </remarks>
public class NotificationPreference : BaseEntity
{
    public Guid UserId { get; set; }

    /// <summary>Yeni mesaj.</summary>
    public bool Messages { get; set; } = true;

    /// <summary>Gelen istek, kabul ve günlük düşme özeti.</summary>
    public bool Requests { get; set; } = true;

    /// <summary>Onay bekliyor ve otomatik onay 24/2 saat.</summary>
    public bool LessonApproval { get; set; } = true;

    /// <summary>Yeni ders, iptal ve yaklaşan ders 60/10 dakika.</summary>
    public bool LessonPlan { get; set; } = true;

    /// <summary>
    /// Kullanıcı bildirim aydınlatma ekranını gördü ve "Aç/Devam" dedi. İlk an korunur
    /// (COALESCE). Bu damga BOŞKEN sunucu cihaz kaydını KABUL ETMEZ: KVKK güvencesi
    /// istemci koduna bağlı kalmasın.
    /// </summary>
    public DateTime? DisclosureShownAtUtc { get; set; }

    /// <summary>
    /// Aydınlatma sorusu kaç kez ertelendi. Mobil geri çekilmeyi bundan hesaplar
    /// (1 → 14 gün, 2 → 60 gün, 3+ → otomatik soru yok). Cihazda TUTULMAZ.
    /// </summary>
    public int PromptDeferCount { get; set; }

    public DateTime? PromptDeferredAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
