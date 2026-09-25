using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Options;
using PeerLearn.Domain.Communication;
using PeerLearn.Domain.Identity;
using PeerLearn.Domain.Matchmaking;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Application.Features.Communication.Bildirimler;

/// <summary>
/// Hatırlatmaları (yaklaşan ders 60/10 dk, otomatik onay 24/2 sa, günlük istek özeti)
/// bildirim defterine ÖNCEDEN yazar. Gönderim zamanını ve güncel durumu dağıtıcı belirler.
/// </summary>
public sealed record EnqueuePushRemindersCommand : IRequest<PushIsSonucu>;

/// <summary>
/// Dakikada bir (PushReminderJob) ve elle (POST admin/jobs/push-reminders).
/// </summary>
/// <remarks>
/// ─── İLERİYE DÖNÜK KUYRUKLAMA ───────────────────────────────────────────────
/// "Anı geldi mi" taraması değil: anı (now − tolerans, now + 2 sa] aralığına giren her
/// hatırlatma DueAt = an, ExpiresAt = an + tolerans ile şimdiden yazılır. Bu iş bir turu
/// (hatta bir saati) kaçırsa bile hatırlatma düşmez; dağıtıcı DueAt'e göre gönderir.
/// Kurallar ve sorgu aralıkları HatirlatmaPenceresi'nde; burada yalnızca uygulanıyor.
///
/// ─── KİLİT YOK, SAHİPLENME TEKİL INDEX'TE ───────────────────────────────────
/// Aynı hatırlatmayı iki sunucu kopyası aynı dakikada, ya da yeniden başlayan süreç ikinci
/// kez yazmaya çalışır. Redis'siz kurulumda süreç içi kilit kopyalar arasında dışlama
/// sağlamıyor; bu yüzden kilit değil <c>INSERT … ON CONFLICT ("RecipientUserId",
/// "DedupeKey") DO NOTHING</c>: ikinci yazım sessizce hiçbir şey yapmaz. EF'in Add'i aynı
/// çakışmada bütün SaveChanges'i düşürürdü.
///
/// ─── TOPLU YAZIM ────────────────────────────────────────────────────────────
/// İleri bakış 2 saat olduğu için aynı hatırlatma dakikada bir, iki saat boyunca yeniden
/// "yazılmaya" çalışılıyor (her seferinde çakışıp hiçbir şey yazmadan). Satır başına ayrı
/// ifade bunu satır × 120 gidiş-dönüşe çevirirdi; tür başına tek <c>unnest</c> ifadesi
/// sayfa başına bir gidiş-dönüş.
///
/// ─── SAYFALAMA ──────────────────────────────────────────────────────────────
/// Tür başına sayfa 500 kayıt, zamana göre sıralı; tur, sayfa kalmayana kadar ilerliyor.
/// Düz "ilk 500" yetmezdi: pencerede 500'den fazla kayıt olsa, sonrakiler öndekiler
/// pencereden çıkana kadar HİÇ yazılmazdı (öndekiler her turda yeniden çakışıp yer tutar).
/// Ofsetle sayfalamada eşzamanlı bir değişiklik bir kaydı bu turda atlatabilir; bir sonraki
/// dakika onu yakalar (ileri bakış 2 saat), iki kez okunan kayıt ise çakışıp geçilir.
///
/// ─── SİLİNMİŞ HESABA SATIR YAZILMAZ ─────────────────────────────────────────
/// Hesap silme (DeleteAccount) alıcısı olduğu defter satırlarını BİR KEZ siliyor ama dersleri
/// ve istekleri kapatmıyor: silinmiş kullanıcının üç saat sonraki dersi Booked, onay bekleyen
/// dersi AwaitingApproval, yanıtlamadığı istekleri Pending kalıyor. Koşulsuz yazım bir dakika
/// sonra aynı kimlik adına yeniden hatırlatma ve 14 güne kadar her gün özet satırı açıyordu;
/// dağıtıcı onları HesapPasif atlasa da 30 gün defterde kalıyorlardı — oysa gizlilik metni
/// "alıcısı olduğu bildirim kayıtları silinir" diyor. Yazım (<see cref="YazAsync"/>) bu yüzden
/// alıcının hesap durumunu AYNI ifadede sınar; özet adayları ayrıca sorguda süzülür (aşağıda).
/// Kalan pencere: silme transaction'ı sürerken yazılan satır (durum henüz commit olmamış);
/// dağıtıcı onu HesapPasif atlar.
/// </remarks>
public sealed class EnqueuePushRemindersHandler : IRequestHandler<EnqueuePushRemindersCommand, PushIsSonucu>
{
    /// <summary>Tür başına tek sayfadaki kayıt.</summary>
    public const int SayfaBoyutu = 500;

    /// <summary>
    /// Tür başına bir turdaki en fazla sayfa. Tavana ulaşılırsa kalan kayıtlar bir sonraki
    /// dakikaya kalır; uyarı yazılır, çünkü bu hacim bu mimarinin tasarlandığı ölçeğin üstünde.
    /// </summary>
    public const int EnFazlaSayfa = 20;

    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly IBildirimSinyali _sinyal;
    private readonly EconomyOptions _ekonomi;
    private readonly ILogger<EnqueuePushRemindersHandler> _logger;

    public EnqueuePushRemindersHandler(
        IAppDbContext db,
        IClock clock,
        IBildirimSinyali sinyal,
        IOptions<EconomyOptions> ekonomi,
        ILogger<EnqueuePushRemindersHandler> logger)
    {
        _db = db;
        _clock = clock;
        _sinyal = sinyal;
        _ekonomi = ekonomi.Value;
        _logger = logger;
    }

    public async Task<PushIsSonucu> Handle(EnqueuePushRemindersCommand request, CancellationToken ct)
    {
        var now = Utc(_clock.UtcNow);

        var eklenen = await DersHatirlatmalariAsync(now, ct)
                      + await OtoOnayHatirlatmalariAsync(now, ct)
                      + await IstekOzetiAsync(now, ct);

        if (eklenen > 0)
        {
            // Anı gelmiş (ya da tolerans içinde gecikmiş) satır varsa beklemeden gitsin.
            // Geri kalanları dağıtıcı kendi taramasında DueAt gelince alır.
            _sinyal.Uyandir();
        }

        return PushIsSonucu.Bos with { Eklenen = eklenen };
    }

    // ─── Yaklaşan ders (60 / 10 dk) ─────────────────────────────────────────────

    private async Task<int> DersHatirlatmalariAsync(DateTime now, CancellationToken ct)
    {
        var (alt, ust) = HatirlatmaPenceresi.DersSorguAraligi(now);
        var eklenen = 0;

        for (var sayfa = 0; ; sayfa++)
        {
            if (sayfa == EnFazlaSayfa)
            {
                SayfaTavaniUyarisi("yaklaşan ders");
                break;
            }

            /* WHERE `Status == Booked` IX_LessonSessions_YaklasanDers filtresiyle BİREBİR
               (kısmi index yalnızca o koşulla kullanılır). Sıralama (başlangıç, Id): aynı
               dakikaya iki ders düşebilir, sayfalar arasında sıra kararlı kalsın. */
            var dersler = await _db.LessonSessions.AsNoTracking()
                .Where(s => s.Status == SessionStatus.Booked && s.ScheduledStartUtc > alt && s.ScheduledStartUtc <= ust)
                .OrderBy(s => s.ScheduledStartUtc).ThenBy(s => s.Id)
                .Skip(sayfa * SayfaBoyutu)
                .Take(SayfaBoyutu)
                .Select(s => new { s.Id, s.TutorUserId, s.StudentUserId, s.ScheduledStartUtc, s.CreatedAtUtc })
                .ToListAsync(ct);

            var satirlar = new List<HatirlatmaSatiri>();
            foreach (var d in dersler)
            {
                foreach (var h in HatirlatmaPenceresi.KuyruklanacakDersHatirlatmalari(now, d.ScheduledStartUtc, d.CreatedAtUtc))
                {
                    var anahtar = BildirimAnahtarlari.Yaklasan(d.Id, h.Ofset);

                    // Her ders için iki satır: iki taraf da hatırlatma alır. Aktör karşı taraf:
                    // dağıtıcı "karşı taraf banlı ya da silinmiş mi" kontrolünü buna yapar.
                    satirlar.Add(new HatirlatmaSatiri(anahtar, d.TutorUserId, d.StudentUserId, d.Id, null, h.AnUtc, h.SonUtc));
                    satirlar.Add(new HatirlatmaSatiri(anahtar, d.StudentUserId, d.TutorUserId, d.Id, null, h.AnUtc, h.SonUtc));
                }
            }

            eklenen += await YazAsync(NotificationType.LessonSoon, satirlar, now, ct);

            if (dersler.Count < SayfaBoyutu)
            {
                break;
            }
        }

        return eklenen;
    }

    // ─── Otomatik onay yaklaşıyor (24 / 2 sa) ──────────────────────────────────

    private async Task<int> OtoOnayHatirlatmalariAsync(DateTime now, CancellationToken ct)
    {
        var saat = _ekonomi.AutoApproveHours;
        var (alt, ust) = HatirlatmaPenceresi.OtoOnaySorguAraligi(now, saat);
        var eklenen = 0;

        for (var sayfa = 0; ; sayfa++)
        {
            if (sayfa == EnFazlaSayfa)
            {
                SayfaTavaniUyarisi("otomatik onay");
                break;
            }

            // WHERE `Status == AwaitingApproval` IX_LessonSessions_OnayBekleyen filtresiyle BİREBİR.
            var dersler = await _db.LessonSessions.AsNoTracking()
                .Where(s => s.Status == SessionStatus.AwaitingApproval
                            && s.CompletionRequestedAtUtc > alt && s.CompletionRequestedAtUtc <= ust)
                .OrderBy(s => s.CompletionRequestedAtUtc).ThenBy(s => s.Id)
                .Skip(sayfa * SayfaBoyutu)
                .Take(SayfaBoyutu)
                .Select(s => new { s.Id, s.TutorUserId, s.StudentUserId, Damga = s.CompletionRequestedAtUtc!.Value })
                .ToListAsync(ct);

            var satirlar = new List<HatirlatmaSatiri>();
            foreach (var d in dersler)
            {
                /* Damga veritabanından okunuyor (mikrosaniye). Onay bekliyor satırı ise handler'da
                   bellekteki değerden yazıldı; ikisi aynı anahtara iner çünkü anahtar zaten
                   mikrosaniyeye kesiyor (BildirimAnahtarlari.Damga). OlayDamgasiUtc de bu
                   okunmuş değer: dağıtıcının eşitlik kontrolü birebir tutar. */
                var damga = Utc(d.Damga);
                foreach (var h in HatirlatmaPenceresi.KuyruklanacakOtoOnayHatirlatmalari(now, damga, saat))
                {
                    satirlar.Add(new HatirlatmaSatiri(
                        BildirimAnahtarlari.OtoOnay(d.Id, damga, h.Ofset),
                        d.StudentUserId, d.TutorUserId, d.Id, damga, h.AnUtc, h.SonUtc));
                }
            }

            eklenen += await YazAsync(NotificationType.AutoApproveSoon, satirlar, now, ct);

            if (dersler.Count < SayfaBoyutu)
            {
                break;
            }
        }

        return eklenen;
    }

    // ─── İstekler düşmek üzere (günlük özet) ──────────────────────────────────

    private async Task<int> IstekOzetiAsync(DateTime now, CancellationToken ct)
    {
        if (HatirlatmaPenceresi.OzetYuvasi(now) is not { } yuva)
        {
            return 0;
        }

        var (alt, ust) = HatirlatmaPenceresi.OzetAdayAraligi(yuva);
        var anahtar = BildirimAnahtarlari.IstekDusecek(yuva);
        var son = HatirlatmaPenceresi.OzetSonu(yuva);
        var eklenen = 0;

        for (var sayfa = 0; ; sayfa++)
        {
            if (sayfa == EnFazlaSayfa)
            {
                SayfaTavaniUyarisi("istek özeti");
                break;
            }

            /* WHERE `Status == Pending` IX_Matches_PendingAge filtresiyle BİREBİR.

               Özeti zaten yazılmış alıcılar SORGUDA süzülüyor; sayfalama bu yüzden hep ilk
               sayfayı istiyor (yazılanlar bir sonraki sorgudan kendiliğinden düşer). Yuva
               penceresi üç saat ve bu iş dakikada bir koşuyor: süzülmeseydi her tur aynı
               alıcıları yeniden okuyup çakıştırırdı.

               Silinmiş hesaplar da SORGUDA süzülüyor, yalnızca yazımda değil: yazım onları sessizce
               atladığı için "yazılanlar bir sonraki sorgudan düşer" varsayımı onlarda tutmaz.
               Tam bir sayfa silinmiş alıcıdan oluşsaydı yazilan == 0 olur ve döngü o gün kalan
               herkesin özetini keserdi. */
            var alicilar = await _db.Matches.AsNoTracking()
                .Where(m => m.Status == MatchStatus.Pending && m.CreatedAtUtc > alt && m.CreatedAtUtc <= ust)
                .Select(m => m.ResponderUserId)
                .Distinct()
                .Where(alici => !_db.Notifications.Any(n => n.RecipientUserId == alici && n.DedupeKey == anahtar))
                .Where(alici => _db.Users.Any(u => u.Id == alici && u.Status != UserStatus.Deleted))
                .OrderBy(alici => alici)
                .Take(SayfaBoyutu)
                .ToListAsync(ct);

            // Özet satırında aktör yok (birden çok isteği özetliyor); RecordId alıcının kendisi.
            var satirlar = alicilar
                .Select(a => new HatirlatmaSatiri(anahtar, a, null, a, null, yuva, son))
                .ToList();

            var yazilan = await YazAsync(NotificationType.MatchExpiringDigest, satirlar, now, ct);
            eklenen += yazilan;

            // Tam sayfa geldi ama hiçbiri yazılamadıysa (başka bir kopya aynı anda yazdı)
            // aynı sayfayı yeniden okumak ilerleme sağlamaz; sonraki tur devam eder.
            if (alicilar.Count < SayfaBoyutu || yazilan == 0)
            {
                break;
            }
        }

        return eklenen;
    }

    // ─── Ortak yazım ──────────────────────────────────────────────────────────

    /// <summary>
    /// Satırları tek ifadede yazar; çakışanlar (zaten yazılmış hatırlatma) sessizce atlanır.
    /// Dönüş: GERÇEKTEN eklenen satır sayısı.
    /// </summary>
    /// <remarks>
    /// Kolonlar Notification varlığıyla birebir; varsayılanı olan Attempts açıkça 0, Status
    /// 'Pending' (enum metin olarak saklanıyor, bkz. CLAUDE.md). Aynı ifadede iki satır aynı
    /// anahtara düşerse (olmaması gerekir) ikincisi de ON CONFLICT ile atlanır; DO NOTHING
    /// bunu hata saymaz.
    ///
    /// Alıcısı silinmiş hesap olan satır yazılmaz (sınıf açıklaması, SİLİNMİŞ HESABA SATIR
    /// YAZILMAZ). Koşul ayrı bir okuma değil aynı ifadenin JOIN'i: okuma ile yazım arasında
    /// silinen hesap için pencere açılmasın. Aktör koşulu YOK: aktörü silinmiş satır karşı
    /// tarafın kaydı ve DeleteAccount onu da silmiyor, Skipped(HesapPasif) yapıyor; dağıtıcı
    /// aynı kararı gönderim anında veriyor.
    /// </remarks>
    private async Task<int> YazAsync(NotificationType tur, IReadOnlyList<HatirlatmaSatiri> satirlar, DateTime now, CancellationToken ct)
    {
        if (satirlar.Count == 0)
        {
            return 0;
        }

        var idler = satirlar.Select(_ => Guid.NewGuid()).ToArray();
        var anahtarlar = satirlar.Select(s => s.Anahtar).ToArray();
        var alicilar = satirlar.Select(s => s.Alici).ToArray();
        var aktorler = satirlar.Select(s => s.Aktor).ToArray();
        var kayitlar = satirlar.Select(s => s.Kayit).ToArray();
        var damgalar = satirlar.Select(s => s.Damga).ToArray();
        var anlar = satirlar.Select(s => Utc(s.An)).ToArray();
        var sonlar = satirlar.Select(s => Utc(s.Son)).ToArray();
        var turMetni = tur.ToString();
        var silinmis = UserStatus.Deleted.ToString();

        return await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO comms."Notifications"
                ("Id", "CreatedAtUtc", "Type", "DedupeKey", "RecipientUserId", "ActorUserId", "RecordId",
                 "OlayDamgasiUtc", "DueAtUtc", "ExpiresAtUtc", "Status", "Attempts")
            SELECT v.id, {now}, {turMetni}, v.anahtar, v.alici, v.aktor, v.kayit, v.damga, v.an, v.son, 'Pending', 0
            FROM unnest({idler}, {anahtarlar}, {alicilar}, {aktorler}, {kayitlar}, {damgalar}, {anlar}, {sonlar})
                AS v(id, anahtar, alici, aktor, kayit, damga, an, son)
            JOIN identity."Users" u ON u."Id" = v.alici
            WHERE u."Status" <> {silinmis}
            ON CONFLICT ("RecipientUserId", "DedupeKey") DO NOTHING
            """, ct);
    }

    private void SayfaTavaniUyarisi(string tur)
        => _logger.LogWarning(
            "Push hatırlatma kuyruklaması: {Tur} için {Sayfa} sayfa ({Kayit} kayıt) tavanına ulaşıldı; " +
            "kalanlar sonraki turda yazılacak. Bu hacim beklenenin çok üstünde.",
            tur, EnFazlaSayfa, EnFazlaSayfa * SayfaBoyutu);

    private static DateTime Utc(DateTime deger) => DateTime.SpecifyKind(deger, DateTimeKind.Utc);

    private sealed record HatirlatmaSatiri(
        string Anahtar, Guid Alici, Guid? Aktor, Guid Kayit, DateTime? Damga, DateTime An, DateTime Son);
}
