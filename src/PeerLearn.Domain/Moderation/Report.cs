using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Moderation;

/// <summary>
/// TEK YÖNLÜ şikayet. Şikayet edilen kişi bunu hiçbir yerde görmez ve yanıt veremez.
/// </summary>
/// <remarks>
/// NEDEN İTİRAZIN (Dispute) YERİNE GEÇTİ.
///
/// İtiraz iki taraflıydı: öğrenci itiraz açar, eğitmen savunma yazar, hakem karar verir ve
/// karar boyunca dersin puanı donardı. Bu yapı, aralarında ders ilişkisi olan iki kişiyi
/// karşı karşıya getiriyordu — "beni şikayet etmişsin" konuşması platformun kendisinde
/// başlıyordu. Amaç kötü davranışı yönetime bildirmekti; sonuç iki kullanıcı arasında
/// gerginlik üretmekti.
///
/// Şikayet bunu üç noktada değiştiriyor:
///   1. GİZLİ — şikayet edilene hiçbir bildirim gitmez, profilinde/derslerinde görünmez.
///   2. SAVUNMA YOK — yanıt ucu yoktur; kararı yalnızca yönetim verir.
///   3. DERSİ ETKİLEMEZ — puan basımı normal akışında sürer. Bilinçli bir takas: şikayet
///      tek başına puanı dondursaydı, sebebini bilmeyen ve itiraz edemeyen eğitmen
///      cezalandırılmış olurdu. Yaptırım kişiye uygulanır (uyarı/askı/ban), derse değil.
///
/// ESKİ İTİRAZLAR SİLİNMEDİ: <see cref="Dispute"/> tablosu ve geçmiş kararlar denetim izi
/// olarak duruyor, yalnızca yeni itiraz AÇILAMIYOR.
/// </remarks>
public class Report : BaseEntity
{
    /// <summary>Şikayeti açan. Yalnızca yönetim görür.</summary>
    public Guid ReporterUserId { get; set; }

    /// <summary>Şikayet edilen kişi. Bu alanın kullanıcıya dönen HİÇBİR DTO'da karşılığı yoktur.</summary>
    public Guid ReportedUserId { get; set; }

    /// <summary>
    /// İlgili ders. Zorunlu DEĞİL: bir kullanıcı ders dışı bir davranış için de
    /// (sohbette taciz gibi) şikayet edilebilmeli.
    /// </summary>
    public Guid? SessionId { get; set; }

    /*
      FORUM İÇERİĞİ — 2026-08-27'de eklendi.

      Forum şikayetleri için AYRI bir tablo açılmadı: üç şikayet türü (ders, sohbet,
      forum) aynı kuyruğa, aynı yaptırım zincirine ve aynı denetim izine düşmeli.
      Ayrı tablo, moderatöre bakması gereken ikinci bir yer açardı ve "bu kişi
      hakkında kaç şikayet var" sorusunun cevabı iki tabloya bölünürdü.

      SessionId gibi bunlar da DÜZ Guid — foreign key YOK. Modüller birbirinin
      tablosuna doğrudan gitmez (CLAUDE.md); moderation modülü community'nin
      satırına FK ile bağlanamaz. Bağın gevşek olması ayrıca şunu sağlıyor: içerik
      bir gün gerçekten silinse bile şikayet kaydı (ve verilen yaptırımın gerekçesi)
      denetim izinde ayakta kalır.

      ÜÇÜ DE NULL OLABİLİR ve en fazla biri dolu olur.
    */
    public Guid? CommunityPostId { get; set; }

    public Guid? CommunityCommentId { get; set; }

    public ReportReason Reason { get; set; }

    public string Description { get; set; } = null!;

    public ReportStatus Status { get; set; } = ReportStatus.Open;

    public Guid? ReviewedByAdminId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }

    /// <summary>Yönetimin kapatma notu. Şikayet edene de gösterilmez — iç kayıttır.</summary>
    public string? AdminNote { get; set; }
}

public enum ReportReason
{
    /// <summary>Ders yapılmadı ama yapılmış gibi onaya gönderildi.</summary>
    SessionNotHeld = 0,

    /// <summary>Kanıt olarak sahte/alakasız görsel yüklendi.</summary>
    FakeProof = 1,

    /// <summary>Ders süresi anlaşılandan belirgin kısa sürdü.</summary>
    DurationMismatch = 2,

    /// <summary>Hakaret, taciz, uygunsuz davranış.</summary>
    Abuse = 3,

    Other = 4,

    /*
      ─── FORUMDAN GELEN SEBEPLER (2026-08-27) ──────────────────────────────────

      Dördü de topluluk şikayet formunun seçenekleri. EKLENMELERİNİN SEBEBİ ŞU:
      onlarsız spam, telif, kişisel bilgi ve trolleme şikayetlerinin hepsi `Other`
      olarak düşüyordu — yani kuyruktaki sebep sütunu, forum şikayetleri için hiçbir
      şey söylemiyordu. Moderasyon kuyruğunun işi sıraya sokmak; tek bir "Diğer"
      yığını sıralanamaz.

      Sebep listesi FORMA GÖRE DEĞİŞİYOR, enum'a göre değil: ders şikayeti formunda
      "Spam" seçeneği yok, forumda "Ders yapılmadı" yok. Enum ortak, alt küme
      arayüzde seçiliyor (Chat.jsx'te kurulan kalıp).

      ⚠️ EKLEMEK GÜVENLİ, ÇIKARMAK DEĞİL: enum veritabanında METİN olarak saklanıyor
      (HasConversion<string>, 30 karakter sınırı). Bir üyeyi verisinden önce silmek,
      o satırları okunamaz hâle getirir (CLAUDE.md).
    */

    /// <summary>Satış, reklam, yönlendirme bağlantısı, tekrar eden gönderi.</summary>
    Spam = 5,

    /// <summary>İzinsiz kitap, PDF, deneme ya da video paylaşımı.</summary>
    Copyright = 6,

    /// <summary>Telefon, adres, sosyal hesap — kendisinin ya da başkasının.</summary>
    PersonalInfo = 7,

    /// <summary>Konu dışı içerik ya da tartışmayı bilerek bozan davranış.</summary>
    OffTopic = 8
}

public enum ReportStatus
{
    Open = 0,

    /// <summary>Yönetim inceledi ve bir yaptırım uyguladı.</summary>
    ActionTaken = 1,

    /// <summary>Yönetim inceledi, işlem gerektirmedi.</summary>
    Dismissed = 2
}
