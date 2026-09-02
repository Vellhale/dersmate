using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Community;

/*
  ══════════════════════════════════════════════════════════════════════════════
  TOPLULUK (FORUM) VARLIKLARI.

  Arayüz 2026-08-25'te yazıldı ama sunucusu yoktu; sekme bu yüzden yayından
  çıkarılmıştı. Bu dosya o boşluğu kapatıyor.

  ─── SAYAÇLAR NEDEN DENORMALİZE ────────────────────────────────────────────────
  Gönderide UpvoteCount / DownvoteCount / CommentCount ayrı sütunlar olarak
  duruyor, her istekte COUNT(*) ile sayılmıyor. Sebep sıralama: akış "en çok oy"
  ve "tartışmalı" ölçütleriyle sıralanıyor ve ikisi de TÜM gönderilerin oy
  sayılarını bilmek zorunda. Her sayfa açılışında oy tablosunu iki kez toplamak,
  forum büyüdükçe akışın ana maliyeti olurdu.

  Bedeli: sayaç ile oy satırları ayrışabilir. Bunu önleyen üç şey var ve üçü de
  oy verme yolunda birlikte kullanılıyor — CLAUDE.md'nin ekonomi için tarif ettiği
  kalıbın aynısı (kilit + transaction + ConcurrencyRetry). Sayaçlar yalnızca
  CommunityVote satırıyla AYNI transaction'da değişiyor.

  ─── DURUM (Status) ────────────────────────────────────────────────────────────
  İçerik SİLİNMİYOR, durumu değişiyor. Sessiz silme moderasyonu görünmez ve
  tartışılamaz yapar; ayrıca şikayet kuyruğundaki kayıt, konusu ortadan kalkmış
  bir şikayete dönüşürdü.

  ─── ŞİKAYET ──────────────────────────────────────────────────────────────────
  Forum içeriği için AYRI bir şikayet tablosu YOK: moderation.Reports genişletildi
  (CommunityPostId / CommunityCommentId). Böylece ders şikayeti, sohbet şikayeti ve
  forum şikayeti aynı kuyruğa, aynı yaptırım zincirine ve aynı denetim izine
  düşüyor — moderatörün bakması gereken tek yer kalıyor.
  ══════════════════════════════════════════════════════════════════════════════
*/

/// <summary>
/// Gönderi etiketi. Arayüzdeki filtre şeridiyle BİREBİR aynı küme
/// (frontend/src/pages/Topluluk.jsx → ETIKETLER); ikisi ayrışırsa filtre sessizce
/// boş sonuç döndürür.
///
/// Enum'lar veritabanında METİN olarak saklanıyor (CLAUDE.md): bir üyeyi verisinden
/// önce silmek, o satırları okunamaz hâle getirir. Önce veriyi göç ettir.
/// </summary>
public enum ForumTag
{
    ExamStress = 0,
    Question = 1,
    Resource = 2,
    StudyPlan = 3,
    Motivation = 4,
    Preference = 5
}

/// <summary>
/// İçerik durumu. Removed olan içerik akışta hiç görünmez; UnderReview
/// perdelenir ama "yine de göster" ile açılabilir.
/// </summary>
public enum ForumContentStatus
{
    Visible = 0,

    /// <summary>Eşiği geçen şikayet sayısı yüzünden otomatik perdelendi.</summary>
    UnderReview = 1,

    /// <summary>Moderasyon kararıyla kaldırıldı. Satır durur, içerik gösterilmez.</summary>
    Removed = 2
}

public class CommunityPost : BaseEntity
{
    public Guid AuthorUserId { get; set; }

    public ForumTag Tag { get; set; }

    public string Title { get; set; } = null!;

    public string Body { get; set; } = null!;

    public ForumContentStatus Status { get; set; } = ForumContentStatus.Visible;

    /*
      Oy sayaçları AYRI tutuluyor, tek bir "puan" değil. Tartışmalı sıralaması
      ikisinin ORANINA bakıyor ve fark tek sayıya indirilseydi (184-6 ile 95-89
      benzer farkı verir) o sıralama hesaplanamazdı — arayüzdeki tartismaPuani
      fonksiyonunun sunucu karşılığı bu iki sütuna dayanıyor.
    */
    public int UpvoteCount { get; set; }
    public int DownvoteCount { get; set; }

    public int CommentCount { get; set; }

    /// <summary>
    /// Açık şikayet sayısı. Eşiği (ForumRules.AutoReviewThreshold) geçince Status
    /// otomatik UnderReview olur — arayüzdeki "3 şikayet alan gönderi akışta
    /// kapatılır" vaadinin gerçek karşılığı.
    /// </summary>
    public int ReportCount { get; set; }

    /// <summary>Moderasyon kararı sonrası doldurulur; denetim izi ayrıca tutulur.</summary>
    public DateTime? ModeratedAtUtc { get; set; }

    /// <summary>
    /// xmin iyimser kilidi. Oy sayaçları eşzamanlı isteklerle güncelleniyor; bu alan
    /// olmadan iki oy birbirinin sayaç yazmasını sessizce ezer (son yazan kazanır) ve
    /// sayaçlar oy satırlarından kalıcı olarak ayrışır. ConcurrencyRetry buna dayanıyor.
    /// </summary>
    public uint Version { get; set; }
}

public class CommunityComment : BaseEntity
{
    public Guid PostId { get; set; }

    public Guid AuthorUserId { get; set; }

    public string Body { get; set; } = null!;

    public ForumContentStatus Status { get; set; } = ForumContentStatus.Visible;

    public int UpvoteCount { get; set; }
    public int DownvoteCount { get; set; }

    public int ReportCount { get; set; }

    public DateTime? ModeratedAtUtc { get; set; }

    /// <summary>Gönderideki ile aynı gerekçe: oy sayaçları eşzamanlı güncelleniyor.</summary>
    public uint Version { get; set; }
}

/// <summary>
/// Tek bir kullanıcının tek bir içeriğe verdiği oy.
///
/// PostId ve CommentId'den TAM OLARAK BİRİ dolu. İki ayrı tablo yerine tek tablo
/// seçildi çünkü oy verme mantığı (aynı yöne ikinci tık = geri alma, ters yöne tık =
/// çevirme) ikisinde de birebir aynı; iki tabloda o mantık iki kez yazılır ve
/// biri düzeltilirken diğeri unutulurdu.
///
/// Tekillik KISMİ index'lerle korunuyor (bkz. CommunityConfigurations): NULL'lar
/// UNIQUE kısıtında birbirinden AYRI sayıldığı için (UserId, PostId) üzerindeki düz
/// bir UNIQUE, yorum oylarını hiç kısıtlamazdı.
/// </summary>
public class CommunityVote : BaseEntity
{
    public Guid UserId { get; set; }

    public Guid? PostId { get; set; }

    public Guid? CommentId { get; set; }

    /// <summary>+1 ya da -1. Sıfır YAZILMAZ: oyun geri alınması satırın silinmesidir.</summary>
    public short Value { get; set; }
}

/// <summary>
/// Forum kuralları — arayüzdeki "Burası nasıl korunuyor" kutusunun SUNUCU karşılığı.
///
/// ⚠️ O kutu 2026-08-25'te yazıldığında bu sayıların kodda karşılığı YOKTU; yan sütun
/// var olmayan korumalar vaat ediyordu ve denetimde bulgu olarak çıktı. Değerler artık
/// burada ve gerçekten uygulanıyor. Kutudaki metni değiştirirken buraya da bak.
/// </summary>
/// <summary>
/// Topluluk katkısının PUANA dönüşme kuralı (ürün sahibi kararı, 2026-08-29).
/// </summary>
/// <remarks>
/// NEDEN VAR: puan kazanmanın tek yolu ders anlatmaktı. Foruma gelip hiç ders
/// vermeyen kullanıcının seviyesi hep 1'de kalıyordu — katkı veriyor ama karşılığı yok.
///
/// ─── NEDEN KUR BU KADAR PAHALI ──────────────────────────────────────────────
/// İlk öneri 100 oy = 50 puan idi (ders başına 50 ile aynı) ve reddedildi: oy vermek
/// BEDAVA, ders anlatmak pahalı (gerçek zaman + karşı tarafın onayı + günlük tavan).
/// İki farklı maliyetteki eylemi aynı kura bağlarsan ucuz olan kazanır ve oy, para
/// basma makinesine döner.
///
/// Çare "frenlemek" (seviye kapısı, günlük tavan) DEĞİL, kuru pahalılaştırmak oldu —
/// gerekçe ürün tarafında: topluluğa gelen dürüst kullanıcıyı kapıda durdurmak, ona
/// puan yolu açmamakla aynı şey. Kur pahalıysa dürüst kullanıcı yavaş ama kesin
/// ilerler; kasmak isteyen ise aynı puan için orantısız iş yapmak zorunda kalır.
///
/// Ölçek (kıyas için 60 dk ders = 100 puan):
///   300 net oy   →  100 puan   ≈ bir ders
///   1.800 net oy →  600 puan   ≈ 5. seviye
///   30.000 net oy → 10.000 puan ≈ 10. seviye (ders yoluyla 200 ders)
///
/// ⚠️ ASIL DUVAR BU ORAN DEĞİL, E-POSTA DOĞRULAMASI. Her hesap her içeriğe bir kez oy
/// verebildiği için 300 oy ~10 sahte hesap × 30 gönderi demek. O 10 hesabı DOĞRULANMIŞ
/// e-postayla açmak, 300 tık atmaktan çok daha pahalı. Doğrulama zayıflarsa bu kur da
/// yeniden gözden geçirilmeli.
///
/// NET OY (artı − eksi) sayılıyor, ham artı değil: 300 artı / 400 eksi alan bir gönderi
/// topluluğun DEĞERSİZ bulduğu bir katkıdır ve puan üretmemeli. Ham sayıyla trolleme
/// kârlı bir strateji olurdu.
/// </remarks>
public static class CommunityRewardRules
{
    /// <summary>Bir ödül için gereken NET oy (artı − eksi, görünür içerikte).</summary>
    public const int NetUpvotesPerReward = 300;

    /// <summary>Eşik aşıldığında basılan puan.</summary>
    public const int CreditsPerReward = 100;

    /// <summary>
    /// Verilen net oya karşılık BUGÜNE KADAR hak edilen toplam puan.
    /// </summary>
    /// <remarks>
    /// Tam bölme bilinçli: eşiğin altındaki artık oylar bir sonraki ödüle SAYILMAYA
    /// devam ediyor (kaybolmuyor), yalnızca henüz ödenmiyor.
    ///
    /// Negatif net oy 0 sayılıyor — eksi oya boğulmuş bir kullanıcının borcu olmaz.
    /// </remarks>
    public static int HakEdilenToplam(int netOy)
        => Math.Max(0, netOy) / NetUpvotesPerReward * CreditsPerReward;
}

public static class ForumRules
{
    /// <summary>Bu kadar açık şikayet alan içerik akışta otomatik perdelenir.</summary>
    public const int AutoReviewThreshold = 3;

    /// <summary>Yeni hesabın ilk günlerinde günlük gönderi tavanı (spam duvarı).</summary>
    public const int NewAccountDailyPostLimit = 3;

    /// <summary>Hesabın "yeni" sayıldığı süre.</summary>
    public const int NewAccountDays = 7;

    /// <summary>Yerleşik hesaplar için günlük gönderi tavanı.</summary>
    public const int DailyPostLimit = 10;

    /// <summary>Dışarıya bağlantı paylaşmak için gereken en düşük seviye.</summary>
    public const int LinkMinLevel = 3;

    public const int TitleMinLength = 10;
    public const int TitleMaxLength = 120;
    public const int BodyMinLength = 20;
    public const int BodyMaxLength = 2000;
    public const int CommentMinLength = 5;
    public const int CommentMaxLength = 1000;
}
