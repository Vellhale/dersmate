using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Matchmaking;
using PeerLearn.Domain.Communication;

namespace PeerLearn.Application.Features.Communication.Bildirimler;

/// <summary>
/// Olay noktalarının (handler'ların) bildirim defterine satır yazma kapısı.
/// </summary>
/// <remarks>
/// ─── SÖZLEŞME ───────────────────────────────────────────────────────────────
/// <see cref="Ekle"/> YALNIZCA <c>_db.Notifications.Add</c> yapar: SaveChanges yok, Expo
/// çağrısı yok, sinyal yok. Handler'ın kuralları:
/// <list type="number">
/// <item>Satırı olayın kendi kaydıyla AYNI SaveChanges'ten (ya da transaction'dan) ÖNCE
/// ekle. Ayrı yazılsaydı olay kaydedilip bildirim kaybolabilir ya da tersi olurdu.</item>
/// <item>Commit'ten SONRA <c>IBildirimSinyali.Uyandir()</c> çağır (fırlatmaz).</item>
/// <item>ConcurrencyRetry içinde eklemek güvenli: yeniden denemeden önce ChangeTracker
/// temizleniyor, eklenen satır da onunla gidiyor ve deneme yeniden ekliyor.</item>
/// </list>
///
/// Fabrika metotları saf (DB'ye dokunmaz, test edilebilir): anahtar, DueAt ve ExpiresAt
/// kuralları burada TEK yerde. Handler hangi olayın satırını yazacağını seçer, zaman
/// kurallarını yeniden yazmaz.
///
/// Hatırlatmalar (yaklaşan ders, otomatik onay, günlük özet) buradan GEÇMEZ: hatırlatma
/// işi onları ham <c>INSERT … ON CONFLICT DO NOTHING</c> ile yazar, çünkü iki sunucu
/// kopyası aynı hatırlatmayı aynı anda yazmaya çalışabilir ve EF'in Add'i çakışmada tüm
/// SaveChanges'i düşürürdü. Anahtar ve anlar yine BildirimAnahtarlari / HatirlatmaPenceresi'nden.
/// </remarks>
public static class BildirimKuyrugu
{
    /// <summary>
    /// Mesaj bildiriminin ömrü: 6 saatten geç gelen "yeni mesaj" bildirimi bilgi değil
    /// gürültü (uygulama açılınca rozet zaten görünüyor). Ttl de buna göre.
    /// </summary>
    public static readonly TimeSpan MesajOmru = TimeSpan.FromHours(6);

    /// <summary>Onay bekliyor: sessiz saat kaydırması otomatik onaya en az bu kadar kala bitmeli.</summary>
    public static readonly TimeSpan OnaySessizPay = TimeSpan.FromHours(2);

    /// <summary>Test bildirimi ömrü: kullanıcı ekranın başında bekliyor, geç gelen test yanıltır.</summary>
    public static readonly TimeSpan TestOmru = TimeSpan.FromMinutes(15);

    /// <summary>Satırı izlemeye ekler. Kaydetmez; kaydetmek çağıranın SaveChanges'i.</summary>
    public static Notification Ekle(IAppDbContext db, Notification satir)
    {
        db.Notifications.Add(satir);
        return satir;
    }

    /// <summary>Yeni mesaj. Sessiz saat UYGULANMAZ; <paramref name="gecikme"/> PushOptions.MesajGecikmeSaniye.</summary>
    public static Notification YeniMesaj(
        Guid mesajId, Guid sohbetId, Guid aliciId, Guid gonderenId, DateTime nowUtc, TimeSpan gecikme)
        => Satir(NotificationType.NewMessage, BildirimAnahtarlari.Mesaj(mesajId), aliciId, gonderenId, mesajId, nowUtc,
            dueAt: nowUtc + gecikme,
            expiresAt: nowUtc + MesajOmru,
            sohbetId: sohbetId);

    /// <summary>Gelen istek. Sessiz saatte 09:00'a kayar; istek düşünce anlamsız.</summary>
    /// <param name="istekOlusturmaUtc">Match.CreatedAtUtc (düşme anı buradan).</param>
    public static Notification YeniIstek(
        Guid istekId, Guid aliciId, Guid gonderenId, DateTime nowUtc, DateTime istekOlusturmaUtc)
        => Satir(NotificationType.MatchRequest, BildirimAnahtarlari.Istek(istekId), aliciId, gonderenId, istekId, nowUtc,
            dueAt: SessizSaat.Kaydir(nowUtc),
            expiresAt: MatchRules.DusmeAni(istekOlusturmaUtc));

    /// <summary>İsteğin kabulü (alıcı: isteği GÖNDEREN). Sessiz saatte kayar.</summary>
    /// <remarks>Ret dalında bu çağrılmaz: ret bildirimi engeli sızdırırdı (BlockUser da Declined yazıyor).</remarks>
    public static Notification IstekKabul(
        Guid istekId, Guid sohbetId, Guid aliciId, Guid kabulEdenId, DateTime nowUtc)
        => Satir(NotificationType.MatchAccepted, BildirimAnahtarlari.IstekKabul(istekId), aliciId, kabulEdenId, istekId, nowUtc,
            dueAt: SessizSaat.Kaydir(nowUtc),
            expiresAt: null,
            sohbetId: sohbetId);

    /// <summary>
    /// Onay bekliyor (kanıt yüklendi ya da itiraz reddedildi). Alıcı öğrenci.
    /// </summary>
    /// <param name="damgaUtc">
    /// ⚠️ CompletionRequestedAtUtc'ye atanan AYNI bellek değeri (handler'daki <c>now</c>).
    /// OlayDamgasiUtc'ye kopyalanır; dağıtıcı SQL eşitliğiyle "hâlâ bu tamamlama mı" diye bakar.
    /// </param>
    /// <param name="otomatikOnaySaati">EconomyOptions.AutoApproveHours.</param>
    public static Notification OnayBekliyor(
        Guid dersId, Guid ogrenciId, Guid egitmenId, DateTime damgaUtc, int otomatikOnaySaati)
    {
        var otomatikOnay = damgaUtc.AddHours(otomatikOnaySaati);
        var satir = Satir(NotificationType.ApprovalPending, BildirimAnahtarlari.Onay(dersId, damgaUtc), ogrenciId, egitmenId,
            dersId, damgaUtc,
            dueAt: SessizSaat.Kaydir(damgaUtc, otomatikOnay, OnaySessizPay),
            expiresAt: otomatikOnay);
        satir.OlayDamgasiUtc = Utc(damgaUtc);
        return satir;
    }

    /// <summary>Eğitmene: yeni ders planlandı. 12 saatten yakın derste sessiz saat uygulanmaz.</summary>
    public static Notification DersPlanlandi(
        Guid dersId, Guid egitmenId, Guid ogrenciId, DateTime nowUtc, DateTime baslangicUtc)
        => Satir(NotificationType.LessonBooked, BildirimAnahtarlari.Rezervasyon(dersId), egitmenId, ogrenciId, dersId, nowUtc,
            dueAt: SessizSaat.UzakPlan(nowUtc, baslangicUtc),
            expiresAt: baslangicUtc);

    /// <summary>
    /// İptal etmeyen tarafa: ders iptal edildi. Cihazda aynı dersin "yaklaşıyor"
    /// bildiriminin yerine geçer (ortak etiket).
    /// </summary>
    public static Notification DersIptal(
        Guid dersId, Guid aliciId, Guid iptalEdenId, DateTime nowUtc, DateTime baslangicUtc)
        => Satir(NotificationType.LessonCancelled, BildirimAnahtarlari.Iptal(dersId), aliciId, iptalEdenId, dersId, nowUtc,
            dueAt: SessizSaat.UzakPlan(nowUtc, baslangicUtc),
            expiresAt: baslangicUtc);

    /// <summary>
    /// Test bildirimi: kullanıcının kendi cihazlarına, seçtiği kanaldan. Kuyruktan geçer ki
    /// bütün hat (defter → dağıtıcı → Expo → cihaz) uçtan uca sınansın.
    /// </summary>
    /// <param name="kanal">BildirimKanallari.TestKanali(istek.tur).</param>
    public static Notification Test(Guid kullaniciId, string kanal, DateTime nowUtc)
    {
        var id = Guid.NewGuid();
        var satir = Satir(NotificationType.Test, BildirimAnahtarlari.Test(kanal, id), kullaniciId, aktorId: null, id, nowUtc,
            dueAt: nowUtc,
            expiresAt: nowUtc + TestOmru);
        satir.Id = id;
        return satir;
    }

    private static Notification Satir(
        NotificationType tur, string anahtar, Guid aliciId, Guid? aktorId, Guid kayitId, DateTime nowUtc,
        DateTime dueAt, DateTime? expiresAt, Guid? sohbetId = null)
        => new()
        {
            Type = tur,
            DedupeKey = anahtar,
            RecipientUserId = aliciId,
            ActorUserId = aktorId,
            RecordId = kayitId,
            ConversationId = sohbetId,
            DueAtUtc = Utc(dueAt),
            ExpiresAtUtc = expiresAt is null ? null : Utc(expiresAt.Value),
            Status = NotificationStatus.Pending,
            CreatedAtUtc = Utc(nowUtc),
        };

    private static DateTime Utc(DateTime deger) => DateTime.SpecifyKind(deger, DateTimeKind.Utc);
}
