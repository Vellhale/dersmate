using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Communication;

/// <summary>
/// Bildirim defteri: hem olay kuyruğu (outbox) hem gönderilen hatırlatmaların kaydı.
/// </summary>
/// <remarks>
/// ─── NEDEN DEFTER, NEDEN İSTEKTE GÖNDERMİYORUZ ──────────────────────────────
/// Olay (mesaj, istek, ders) kendi kaydıyla AYNI SaveChanges'te bir satır olarak yazılır;
/// asıl gönderimi arka plandaki iş yapar. İki kazanç: istek Expo'yu beklemez, ve sunucu
/// yeniden başlasa da bildirim kaybolmaz. Handler'da Expo çağırmak ikisini de kaybettirirdi.
///
/// ─── İÇERİK KOPYALANMAZ ─────────────────────────────────────────────────────
/// Satırda yalnızca kimlikler ve zamanlar var. Metin gönderim ANINDA güncel veriden
/// kurulur: mesaj okunmuşsa gitmez, ders iptal edildiyse "yaklaşıyor" gitmez, okunmamış
/// sayısı o anki sayıdır. Mesaj içeriği ise hiçbir yerde, hiçbir koşulda yok.
///
/// ─── FK YOK (SweepFailure deseni) ───────────────────────────────────────────
/// Alıcı/aktör/kayıt farklı şemalara işaret ediyor ve hesap silme Users satırını silmiyor.
/// Yetimleri hesap silme (alıcı satırları) ve yaş temizliği (30 gün) kaldırır.
/// </remarks>
public class Notification : BaseEntity
{
    public NotificationType Type { get; set; }

    /// <summary>
    /// Olay başına tekil anahtar (<c>BildirimAnahtarlari</c>). UNIQUE(RecipientUserId,
    /// DedupeKey) mükerrere karşı SON hattır ve hatırlatmaların sahiplenmesidir: iki sunucu
    /// kopyası ya da yeniden başlatma aynı hatırlatmayı ikinci kez yazamaz.
    /// </summary>
    public string DedupeKey { get; set; } = null!;

    public Guid RecipientUserId { get; set; }

    /// <summary>Olayı doğuran kişi (gönderen, iptal eden…). Özet ve test satırlarında yok.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>
    /// Olayın konusu: messageId, matchId, sessionId. Özette alıcının kendisi; testte
    /// satırın kendi rastgele kimliği.
    /// </summary>
    public Guid RecordId { get; set; }

    /// <summary>Mesaj ve kabul satırlarında sohbet; mesaj birleştirme bu alana göre yapılır.</summary>
    public Guid? ConversationId { get; set; }

    /// <summary>
    /// Onay türlerinde (ApprovalPending, AutoApproveSoon) LessonSession.CompletionRequestedAtUtc
    /// değerinin KOPYASI. Dağıtıcı SQL'de <c>s."CompletionRequestedAtUtc" = n."OlayDamgasiUtc"</c>
    /// arar; tutmazsa damga yenilenmiş (itiraz reddi) ya da ders çoktan kapanmıştır.
    /// </summary>
    /// <remarks>
    /// ⚠️ Handler bu alanı, entity'ye atadığı AYNI bellek değerinden doldurmalı (ikisi de
    /// `now`). İki sütun aynı mikrosaniye kesimine uğradığı için SQL eşitliği tutar.
    /// Anahtar metninden ayrıştırmak YASAK: metin biçimi değişirse eşitlik sessizce bozulur.
    /// </remarks>
    public DateTime? OlayDamgasiUtc { get; set; }

    /// <summary>
    /// En erken gönderim anı. Mesajda +10 sn gecikme, sessiz saat kaydırması, hatırlatmanın
    /// kendi anı ve hata sonrası yeniden deneme beklemesi de buraya yazılır.
    /// </summary>
    public DateTime DueAtUtc { get; set; }

    /// <summary>Bu andan sonra gönderilmez (Skipped(Bayat)). Geç gelen "1 saat kaldı" yanlış bilgidir.</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;

    public NotificationOutcome? Outcome { get; set; }

    /// <summary>Geçici hatalardaki deneme sayısı (üstel bekleme bundan hesaplanır).</summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Satırı sahiplenen dağıtım turunun kimliği (fencing). Sonuç yazımı
    /// <c>"LeaseOwner" = @tur</c> koşuluyla yapılır: kirası dolup başka tura geçmiş satırın
    /// sonucunu eski tur ezemez.
    /// </summary>
    public Guid? LeaseOwner { get; set; }

    public DateTime? LeaseUntilUtc { get; set; }

    /// <summary>Sent/Skipped/Failed'a geçiş anı. Yaş temizliği (30 gün) buna bakar.</summary>
    public DateTime? ProcessedAtUtc { get; set; }

    /// <summary>
    /// Son hatanın kısa hâli. ⚠️ YALNIZCA maskelenmiş metin: Expo'nun hata metni token'ı
    /// içeriyor, tam token buraya asla yazılmaz.
    /// </summary>
    public string? LastError { get; set; }
}
