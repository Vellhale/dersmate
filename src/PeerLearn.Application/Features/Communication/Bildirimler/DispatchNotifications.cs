using System.Diagnostics;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Matchmaking;
using PeerLearn.Application.Options;
using PeerLearn.Domain.Communication;
using PeerLearn.Domain.Identity;
using PeerLearn.Domain.Matchmaking;
using PeerLearn.Domain.Moderation;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Application.Features.Communication.Bildirimler;

/// <summary>
/// Push işlerinin (hatırlatma, dağıtım, makbuz, temizlik) ortak sonucu. Yönetim uçları
/// (admin/jobs/push-*) bunu döndürür; e2e testleri alan adlarıyla okur.
/// </summary>
/// <param name="Eklenen">Deftere yazılan yeni hatırlatma satırı.</param>
/// <param name="Gonderilen">Sent'e geçen satır (en az bir cihazdan Expo "ok").</param>
/// <param name="Atlanan">Skipped'a geçen satır (tercih, engel, durum, cihaz yok, bayat…).</param>
/// <param name="Ertelenen">Kuyruğa geri dönen satır: geçici hata, mesaj kısması, kira bitmesi.</param>
/// <param name="Basarisiz">Failed'a geçen satır (kalıcı hata ya da deneme tavanı).</param>
/// <param name="SilinenCihaz">DeviceNotRegistered ya da başka projeye ait olduğu için silinen cihaz kaydı.</param>
/// <param name="SilinenKayit">Temizlik ve makbuzda silinen defter/bilet/kısma satırı.</param>
public sealed record PushIsSonucu(
    int Eklenen, int Gonderilen, int Atlanan, int Ertelenen, int Basarisiz, int SilinenCihaz, int SilinenKayit)
{
    public static PushIsSonucu Bos { get; } = new(0, 0, 0, 0, 0, 0, 0);

    public static PushIsSonucu operator +(PushIsSonucu a, PushIsSonucu b) => new(
        a.Eklenen + b.Eklenen, a.Gonderilen + b.Gonderilen, a.Atlanan + b.Atlanan, a.Ertelenen + b.Ertelenen,
        a.Basarisiz + b.Basarisiz, a.SilinenCihaz + b.SilinenCihaz, a.SilinenKayit + b.SilinenKayit);

    /// <summary>Hiçbir şey olmadı mı (günlük gürültüsünü kesmek için). Yanıt sözleşmesinin parçası değil.</summary>
    [JsonIgnore]
    public bool BosMu => this == Bos;
}

/// <summary>Bildirim defterindeki vadesi gelmiş satırları gönderir.</summary>
public sealed record DispatchNotificationsCommand : IRequest<PushIsSonucu>;

/// <summary>
/// Dağıtıcı: sinyalle anında, değilse 5 saniyede bir (NotificationDispatchJob) ve elle
/// (POST admin/jobs/push-dispatch).
/// </summary>
/// <remarks>
/// ─── BİR PARTİNİN AKIŞI ─────────────────────────────────────────────────────
/// <list type="number">
/// <item><b>Sahiplenme</b>: vadesi gelmiş ve kirası olmayan en fazla 200 satır, parti başına
/// YENİ bir kimlikle (LeaseOwner) 2 dakikalığına kiralanır (<c>FOR UPDATE SKIP LOCKED</c>):
/// iki sunucu kopyası aynı satırı alamaz.</item>
/// <item><b>Bağlam</b>: alıcı/aktör durumu, tercihler, engeller, olayların GÜNCEL durumu ve
/// bağlı cihazlar toplu sorgularla okunur. Metin burada, güncel veriden kurulur.</item>
/// <item><b>Eleme</b>: gitmeyecek satırlar nedeniyle birlikte (Outcome) tek UPDATE ile yazılır.</item>
/// <item><b>Kısma</b>: mesaj satırları için sohbet başına yuva; alınamazsa satır ertelenir.</item>
/// <item><b>Gönderim</b>: 100'lük alt partiler. Her Expo çağrısından ÖNCE kalan kira
/// yerel saatle ölçülür; 30 sn'den azsa o alt parti gönderilmeden bırakılır.</item>
/// <item><b>Sonuç yazımı</b>: Expo yanıtından HEMEN SONRA, başka hiçbir yazımdan önce.</item>
/// <item><b>Bilet ve ölü cihaz</b>: en iyi çaba; düşerse uyarı, durum geri alınmaz.</item>
/// </list>
///
/// ─── FENCING: HER DURUM YAZIMI "BU SATIR HÂLÂ BENİM Mİ" DİYE SORAR ──────────
/// <c>WHERE "Id" = ANY(@ids) AND "LeaseOwner" = @tur AND "Status" = 'Pending'</c>. Bu parti
/// takılıp kirası dolarsa satırı başka bir tur alır; geç kalan bu tur onun sonucunu EZEMEZ.
/// Etkilenen satır 0 ise sessizce geçilir: satır ya başka turda ya da hesap silmeyle gitti.
///
/// ─── SONUÇ YAZIMI NEDEN ÖNCE VE NEDEN AYRI İPTAL JETONUYLA ──────────────────
/// Expo kabul ettikten sonra durum yazılamazsa satır Pending ve kiralı kalır; kira dolunca
/// başka bir tur onu ALIR VE YENİDEN GÖNDERİR. Bu yüzden durum yazımı her şeyden önce gelir
/// ve kapanıştaki stoppingToken'a değil, 10 saniyelik kendi jetonuna bağlıdır: kapanış
/// sinyali tam bu anda gelse bile yazım tamamlanır. Bilet ve cihaz silme ondan SONRA ve
/// ayrı: FK çakışması, eşzamanlı silme ya da ağ hatası onları düşürürse durum işaretlemesi
/// geri alınmaz — kaybedilen yalnızca bir makbuz kontrolü, mükerrer bildirim değil.
///
/// Kalan tek mükerrer penceresi: Expo kabul etti ama süreç sonuç yazılmadan öldü. Bu "en
/// az bir kez" teslimattır; Android'de tag, iOS'ta apns-collapse-id cihazdaki ikinci kopyayı
/// yenisiyle DEĞİŞTİRİR, yani kullanıcı iki bildirim görmez.
///
/// ─── GÜNCEL VERİ, İÇERİKSİZ DEFTER ──────────────────────────────────────────
/// Satır yalnızca kimlik taşır; mesaj okunmuşsa (Okundu), ders iptal edilmişse, onay damgası
/// yenilenmişse (DurumDegisti) satır gitmez. Okunmamış sayısı GÖNDERİM ANINDA sayılır.
/// Mesaj içeriği hiçbir koşulda okunmaz ve yüke girmez.
/// </remarks>
public sealed class DispatchNotificationsHandler : IRequestHandler<DispatchNotificationsCommand, PushIsSonucu>
{
    /// <summary>Bir sahiplenmedeki en fazla satır.</summary>
    public const int PartiBoyutu = 200;

    /// <summary>Bir turdaki en fazla parti; kalan satırlar bir sonraki turda.</summary>
    public const int TurBasinaEnFazlaParti = 10;

    /// <summary>İstek düzeyi 400'de bölme derinliği: 100 → 50 → … → 1 yedi düzey.</summary>
    public const int BolmeDerinligi = 7;

    /// <summary>Kira süresi. Kirası dolan satırı başka bir tur alabilir.</summary>
    public static readonly TimeSpan KiraSuresi = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Expo çağrısından önce kirada en az bu kadar kalmalı. Çağrının zaman aşımı 15 sn;
    /// kalan kira bundan kısaysa yanıt gelmeden kira dolabilir ve satır başka bir turda
    /// İKİNCİ KEZ gönderilir. Yerel saat (Stopwatch) kullanılıyor: sunucu saatinin DB
    /// saatinden sapması kira hesabını bozmasın.
    /// </summary>
    public static readonly TimeSpan KiraPayi = TimeSpan.FromSeconds(30);

    /// <summary>Sonuç yazımının kendi zaman aşımı (kapanış jetonundan bağımsız).</summary>
    public static readonly TimeSpan SonucYazimSuresi = TimeSpan.FromSeconds(10);

    /// <summary>Aynı kişiden aynı kişiye istek bildirimi freni.</summary>
    public static readonly TimeSpan IstekFreni = TimeSpan.FromDays(7);

    /// <summary>
    /// Otomatik onay hatırlatması, onaya bundan az kaldıysa gitmez (Bayat): "yaklaşık 5
    /// dakika sonra onaylanacak, itiraz için dokun" dendiğinde itiraz etmeye zaman kalmaz.
    /// </summary>
    public static readonly TimeSpan OtoOnayEnAzKalan = TimeSpan.FromMinutes(15);

    /// <summary>Bir satırın gidebileceği en fazla cihaz (bir alt partiye sığsın).</summary>
    public const int SatirBasinaEnFazlaCihaz = PushSinirlari.EnFazlaMesaj;

    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly IPushGonderici _gonderici;
    private readonly BildirimEtiketi _etiket;
    private readonly PushOptions _push;
    private readonly EconomyOptions _ekonomi;
    private readonly ILogger<DispatchNotificationsHandler> _logger;

    public DispatchNotificationsHandler(
        IAppDbContext db,
        IClock clock,
        IPushGonderici gonderici,
        BildirimEtiketi etiket,
        IOptions<PushOptions> push,
        IOptions<EconomyOptions> ekonomi,
        ILogger<DispatchNotificationsHandler> logger)
    {
        _db = db;
        _clock = clock;
        _gonderici = gonderici;
        _etiket = etiket;
        _push = push.Value;
        _ekonomi = ekonomi.Value;
        _logger = logger;
    }

    public async Task<PushIsSonucu> Handle(DispatchNotificationsCommand request, CancellationToken ct)
    {
        var toplam = PushIsSonucu.Bos;

        for (var parti = 0; parti < TurBasinaEnFazlaParti; parti++)
        {
            var sonuc = await PartiAsync(ct);
            if (sonuc is null)
            {
                break;
            }

            toplam += sonuc;
        }

        return toplam;
    }

    // ═══ 1. Sahiplenme ══════════════════════════════════════════════════════════

    /// <summary>Bir parti; sahiplenecek satır yoksa null.</summary>
    private async Task<PushIsSonucu?> PartiAsync(CancellationToken ct)
    {
        // Kira yerel saatle ölçülür; sahiplenmeden ÖNCE başlatılıyor ki pay ihtiyatlı kalsın.
        var kira = Stopwatch.StartNew();
        var now = Utc(_clock.UtcNow);
        var tur = Guid.NewGuid();
        var kiraSonu = now + KiraSuresi;

        /* Alt sorgunun WHERE'i IX_Notifications_Bekleyen filtresini ("Status" = 'Pending')
           BİREBİR içeriyor; yoksa index sessizce devreden çıkar. Kirası dolmuş satır da
           alınır: onu tutan tur takıldı ya da süreç öldü. */
        var idler = await _db.Database.SqlQuery<Guid>(
            $"""
            UPDATE comms."Notifications" SET "LeaseOwner" = {tur}, "LeaseUntilUtc" = {kiraSonu}
            WHERE "Id" IN (
                SELECT "Id" FROM comms."Notifications"
                WHERE "Status" = 'Pending' AND "DueAtUtc" <= {now}
                  AND ("LeaseUntilUtc" IS NULL OR "LeaseUntilUtc" < {now})
                ORDER BY "DueAtUtc"
                LIMIT {PartiBoyutu}
                FOR UPDATE SKIP LOCKED)
            RETURNING "Id" AS "Value"
            """).ToListAsync(ct);

        if (idler.Count == 0)
        {
            return null;
        }

        var satirlar = await _db.Notifications.AsNoTracking()
            .Where(n => idler.Contains(n.Id) && n.LeaseOwner == tur)
            .OrderBy(n => n.DueAtUtc)
            .ToListAsync(ct);

        var parti = new Parti(tur, kira);
        var baglam = await BaglamAsync(satirlar, now, ct);

        // ═══ 2-3. Eleme ═══
        var isler = new List<Is>();
        foreach (var satir in satirlar)
        {
            /* Tek bir bozuk satır (eşlemesi olmayan tür, kurulamayan yük) bütün partiyi
               düşürmesin: düşseydi satırlar kiralı kalır, iki dakika sonra aynı satır yine
               aynı yerde patlar ve partideki herkesin bildirimi o döngüde takılırdı. */
            try
            {
                var karar = Degerlendir(satir, baglam, now);
                switch (karar.Tur)
                {
                    case KararTuru.Atla:
                        parti.Atla(satir.Id, karar.Sonuc!.Value);
                        break;
                    default:
                        isler.Add(new Is(satir, karar.Taslak!, karar.Cihazlar!, karar.Okunmamislar));
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Sessizce yanlış kanala gitmesin: Failed ve iz.
                _logger.LogError(ex, "Bildirim satırı değerlendirilemedi: {Id} ({Tur}).", satir.Id, satir.Type);
                parti.Basarisiz(satir.Id, PushHataKurali.SonHata("Degerlendirme", ex.Message));
            }
        }

        BirlestirVeFrenle(isler, parti);
        await KismaAsync(isler, parti, now, ct);

        // Eleme ve ertelemeler Expo'dan ÖNCE yazılır: gönderim uzarsa (ya da süreç ölürse)
        // bu satırlar kiraları dolana kadar boşuna beklemesin.
        await parti.YazAsync(this);

        // ═══ 4-6. Gönderim ═══
        foreach (var altParti in AltPartiler(isler))
        {
            await GonderAsync(altParti.SelectMany(i => i.Mesajlar).ToList(), 0, parti, ct);
            await TamamlananlariYazAsync(altParti, parti);
        }

        // Güvenlik ağı: yazılmamış iş kalmadığından emin ol (her şey yazıldıysa hiçbir şey yapmaz).
        await TamamlananlariYazAsync(isler, parti);

        // ═══ 7. En iyi çaba ═══
        await EnIyiCabaAsync(parti);

        return parti.Sonuc;
    }

    // ═══ 2. Bağlam ══════════════════════════════════════════════════════════════

    private async Task<Baglam> BaglamAsync(IReadOnlyList<Notification> satirlar, DateTime now, CancellationToken ct)
    {
        var b = new Baglam();

        var alicilar = satirlar.Select(n => n.RecipientUserId).Distinct().ToList();
        var kisiler = alicilar
            .Concat(satirlar.Where(n => n.ActorUserId is not null).Select(n => n.ActorUserId!.Value))
            .Distinct()
            .ToList();

        b.Kullanicilar = await _db.Users.AsNoTracking()
            .Where(u => kisiler.Contains(u.Id))
            .Select(u => new KullaniciDurumu(u.Id, u.Status, u.SuspendedUntilUtc, u.DisplayName))
            .ToDictionaryAsync(u => u.Id, ct);

        b.Tercihler = await _db.NotificationPreferences.AsNoTracking()
            .Where(p => alicilar.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, ct);

        // Engel iki yönlü etkili (UserBlock): tek satır A→B, kontrol her iki yöne bakar.
        var engeller = await _db.UserBlocks.AsNoTracking()
            .Where(e => kisiler.Contains(e.BlockerUserId) && kisiler.Contains(e.BlockedUserId))
            .Select(e => new { e.BlockerUserId, e.BlockedUserId })
            .ToListAsync(ct);
        foreach (var e in engeller)
        {
            b.Engeller.Add(Cift(e.BlockerUserId, e.BlockedUserId));
        }

        // Bağlı cihazlar: kural TEK yerde (OturumBagi) — kayıt ucuyla aynı tanım.
        var cihazlar = await _db.PushDevices.AsNoTracking()
            .Where(d => alicilar.Contains(d.UserId))
            .Where(OturumBagi.BagliCihaz(_db.RefreshTokens, now))
            .Select(d => new Cihaz(d.Id, d.UserId, d.Token, d.Platform, d.KapaliKanallar, d.LastSeenAtUtc))
            .ToListAsync(ct);
        b.Cihazlar = cihazlar
            .GroupBy(c => c.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.LastSeenAtUtc).ToList());

        await MesajBaglamiAsync(b, satirlar, ct);
        await IstekBaglamiAsync(b, satirlar, now, ct);
        await DersBaglamiAsync(b, satirlar, ct);

        var konuIdleri = b.Eslesmeler.Values.Where(m => m.KonuId is not null).Select(m => m.KonuId!.Value)
            .Concat(b.Dersler.Values.Select(d => d.KonuId))
            .Distinct()
            .ToList();
        if (konuIdleri.Count > 0)
        {
            b.Konular = await _db.Topics.AsNoTracking()
                .Where(t => konuIdleri.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        }

        return b;
    }

    private async Task MesajBaglamiAsync(Baglam b, IReadOnlyList<Notification> satirlar, CancellationToken ct)
    {
        var mesajSatirlari = satirlar.Where(n => n.Type == NotificationType.NewMessage && n.ConversationId is not null).ToList();
        if (mesajSatirlari.Count == 0)
        {
            return;
        }

        var mesajIdleri = mesajSatirlari.Select(n => n.RecordId).Distinct().ToList();
        b.Mesajlar = await _db.Messages.AsNoTracking()
            .Where(m => mesajIdleri.Contains(m.Id))
            .Select(m => new MesajDurumu(m.Id, m.ReadAtUtc, m.IsDeleted))
            .ToDictionaryAsync(m => m.Id, ct);

        /* Okunmamışlar (sohbet, gönderen) başına: gövdedeki sayı ve "bu gönderimin kapsadığı
           mesajlar". WHERE, (ConversationId, SenderUserId) kısmi index'inin filtresini
           (ReadAtUtc IS NULL AND IsDeleted = FALSE) BİREBİR içeriyor. Yalnızca kimlik okunur:
           içerik (Content) hiçbir koşulda seçilmez. */
        var sohbetler = mesajSatirlari.Select(n => n.ConversationId!.Value).Distinct().ToList();
        var gonderenler = mesajSatirlari.Where(n => n.ActorUserId is not null).Select(n => n.ActorUserId!.Value).Distinct().ToList();
        var okunmamislar = await _db.Messages.AsNoTracking()
            .Where(m => sohbetler.Contains(m.ConversationId) && gonderenler.Contains(m.SenderUserId)
                        && m.ReadAtUtc == null && !m.IsDeleted)
            .Select(m => new { m.Id, m.ConversationId, m.SenderUserId })
            .ToListAsync(ct);
        b.Okunmamis = okunmamislar
            .GroupBy(m => (m.ConversationId, m.SenderUserId))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(m => m.Id).ToList());

        /* iOS rozeti: alıcının TOPLAM okunmamış mesajı (bütün sohbetler). Yalnızca iOS cihazı
           olan alıcılar için sayılır; Android'de rozeti uygulama kendisi yönetiyor. Tanım
           sohbet listesininkiyle aynı (GetConversations): kabul edilmiş ya da sonlandırılmış
           eşleşmenin sohbeti, karşı tarafın okunmamış ve silinmemiş mesajı. */
        var iosAlicilar = mesajSatirlari.Select(n => n.RecipientUserId).Distinct()
            .Where(a => b.Cihazlar.TryGetValue(a, out var c) && c.Any(x => x.Platform == PushPlatform.Ios))
            .ToList();
        if (iosAlicilar.Count > 0)
        {
            var rozetler = await (
                    from msg in _db.Messages.AsNoTracking()
                    join c in _db.Conversations.AsNoTracking() on msg.ConversationId equals c.Id
                    join m in _db.Matches.AsNoTracking() on c.MatchId equals m.Id
                    where msg.ReadAtUtc == null && !msg.IsDeleted
                          && (m.Status == MatchStatus.Accepted || m.Status == MatchStatus.Closed)
                    let alici = msg.SenderUserId == m.InitiatorUserId ? m.ResponderUserId : m.InitiatorUserId
                    where iosAlicilar.Contains(alici)
                    group msg by alici into g
                    select new { Alici = g.Key, Sayi = g.Count() })
                .ToListAsync(ct);
            b.Rozetler = rozetler.ToDictionary(r => r.Alici, r => r.Sayi);
        }
    }

    private async Task IstekBaglamiAsync(Baglam b, IReadOnlyList<Notification> satirlar, DateTime now, CancellationToken ct)
    {
        var eslesmeIdleri = satirlar
            .Where(n => n.Type is NotificationType.MatchRequest or NotificationType.MatchAccepted)
            .Select(n => n.RecordId).Distinct().ToList();
        if (eslesmeIdleri.Count > 0)
        {
            b.Eslesmeler = await _db.Matches.AsNoTracking()
                .Where(m => eslesmeIdleri.Contains(m.Id))
                .Select(m => new EslesmeDurumu(m.Id, m.Status, m.InitiatorUserId, m.ResponderUserId, m.RequestedTopicId, m.CreatedAtUtc))
                .ToDictionaryAsync(m => m.Id, ct);
        }

        /* İstek freni (aynı kişiden aynı kişiye 7 günde bir istek bildirimi): önceki Sent
           istek bildirimi ya da reddedilmiş istek. Retten sonra aynı kişiye sınırsız istek
           gönderilebiliyor (MatchRequests); fren olmasaydı her istek alıcının telefonunu
           yeniden çaldırırdı. İstek yine oluşur, yalnızca push gitmez. */
        var istekler = satirlar.Where(n => n.Type == NotificationType.MatchRequest && n.ActorUserId is not null).ToList();
        if (istekler.Count > 0)
        {
            var esik = now - IstekFreni;
            var alanlar = istekler.Select(n => n.RecipientUserId).Distinct().ToList();
            var gonderenler = istekler.Select(n => n.ActorUserId!.Value).Distinct().ToList();

            var gidenler = await _db.Notifications.AsNoTracking()
                .Where(n => n.Type == NotificationType.MatchRequest && n.Status == NotificationStatus.Sent
                            && n.ProcessedAtUtc >= esik
                            && alanlar.Contains(n.RecipientUserId)
                            && n.ActorUserId != null && gonderenler.Contains(n.ActorUserId.Value))
                .Select(n => new { Gonderen = n.ActorUserId!.Value, Alan = n.RecipientUserId })
                .ToListAsync(ct);
            var retler = await _db.Matches.AsNoTracking()
                .Where(m => m.Status == MatchStatus.Declined && m.RespondedAtUtc >= esik
                            && gonderenler.Contains(m.InitiatorUserId) && alanlar.Contains(m.ResponderUserId))
                .Select(m => new { Gonderen = m.InitiatorUserId, Alan = m.ResponderUserId })
                .ToListAsync(ct);

            foreach (var x in gidenler.Concat(retler))
            {
                b.Frenli.Add((x.Gonderen, x.Alan));
            }
        }

        /* Günlük özet: gönderim anında yeniden sayılır. WHERE `Status == Pending`
           IX_Matches_PendingAge filtresiyle birebir; süresi dolmuş ama süpürücünün henüz
           düşürmediği istekler sayılmaz (MatchRules ile aynı sınır). */
        var ozetAlicilari = satirlar.Where(n => n.Type == NotificationType.MatchExpiringDigest)
            .Select(n => n.RecipientUserId).Distinct().ToList();
        if (ozetAlicilari.Count > 0)
        {
            var dusmeEsigi = now.AddDays(-MatchRules.RequestExpireDays);
            var ozetler = await _db.Matches.AsNoTracking()
                .Where(m => m.Status == MatchStatus.Pending && m.CreatedAtUtc > dusmeEsigi
                            && ozetAlicilari.Contains(m.ResponderUserId))
                .GroupBy(m => m.ResponderUserId)
                .Select(g => new { Alici = g.Key, Sayi = g.Count(), Ilk = g.Min(m => m.CreatedAtUtc) })
                .ToListAsync(ct);
            b.Ozetler = ozetler.ToDictionary(o => o.Alici, o => (o.Sayi, Utc(o.Ilk)));
        }
    }

    private async Task DersBaglamiAsync(Baglam b, IReadOnlyList<Notification> satirlar, CancellationToken ct)
    {
        var dersIdleri = satirlar.Where(n => DersTuru(n.Type)).Select(n => n.RecordId).Distinct().ToList();
        if (dersIdleri.Count == 0)
        {
            return;
        }

        b.Dersler = await _db.LessonSessions.AsNoTracking()
            .Where(s => dersIdleri.Contains(s.Id))
            .Select(s => new DersDurumu(s.Id, s.Status, s.TopicId, s.ScheduledStartUtc, s.CompletionRequestedAtUtc))
            .ToDictionaryAsync(s => s.Id, ct);

        /* "İtiraz sonrası" metni: bu damgayla AYNI anda reddedilmiş bir itiraz var mı?
           ResolveDispute'un Dismissed dalı ResolvedAtUtc ile yeni CompletionRequestedAtUtc'yi
           aynı `now`dan yazıyor; eşitlik veritabanında birebir tutuyor. */
        var onayDersleri = satirlar.Where(n => n.Type == NotificationType.ApprovalPending).Select(n => n.RecordId).Distinct().ToList();
        if (onayDersleri.Count > 0)
        {
            var itirazlar = await _db.Disputes.AsNoTracking()
                .Where(d => onayDersleri.Contains(d.SessionId) && d.Status == DisputeStatus.Dismissed && d.ResolvedAtUtc != null)
                .Select(d => new { d.SessionId, Damga = d.ResolvedAtUtc!.Value })
                .ToListAsync(ct);
            foreach (var i in itirazlar)
            {
                b.ReddedilenItirazlar.Add((i.SessionId, Utc(i.Damga)));
            }
        }
    }

    // ═══ 3. Eleme ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Bir satırın kaderi: atla (neden) ya da gönder (metin + cihazlar). Kurulamayan satır
    /// (eşlemesi olmayan tür) fırlatır; çağıran onu Failed yazar.
    /// Sıra bilinçli: ucuzdan pahalıya ve en "kalıcı" nedenden en geçiciye.
    /// </summary>
    private Karar Degerlendir(Notification n, Baglam b, DateTime now)
    {
        if (n.ExpiresAtUtc is { } son && son <= now)
        {
            return Karar.Atla(NotificationOutcome.Bayat);
        }

        // Alıcı uygulamayı kullanamıyorsa (askı, ban, silinmiş) bildirim anlamsız.
        if (!b.Kullanicilar.TryGetValue(n.RecipientUserId, out var alici) || !Erisebilir(alici, now))
        {
            return Karar.Atla(NotificationOutcome.HesapPasif);
        }

        KullaniciDurumu? aktor = null;
        if (n.ActorUserId is { } aktorId)
        {
            if (!b.Kullanicilar.TryGetValue(aktorId, out aktor) || !AktorUygun(n.Type, aktor, now))
            {
                return Karar.Atla(NotificationOutcome.HesapPasif);
            }

            /* Engel yalnızca KİŞİYİ adıyla ya da isteğiyle getiren türlerde. Ders metinleri
               engelli ve engelsiz vakada BAYT BAYT aynı ve ad taşımıyor: yalnızca engelde
               düşen bir ders bildirimi engeli ilan ederdi. */
            if (KisiTuru(n.Type) && b.Engeller.Contains(Cift(n.RecipientUserId, aktorId)))
            {
                return Karar.Atla(NotificationOutcome.Engel);
            }
        }

        b.Tercihler.TryGetValue(n.RecipientUserId, out var tercih);
        if (!BildirimKanallari.TercihAcik(tercih, n.Type))
        {
            return Karar.Atla(NotificationOutcome.TercihKapali);
        }

        var icerik = Icerik(n, b, aktor, now, out var durum, out var okunmamislar);
        if (durum is { } atla)
        {
            return Karar.Atla(atla);
        }

        var kanal = BildirimKanallari.Kanal(n.Type, n.Type == NotificationType.Test ? BildirimAnahtarlari.TestKanali(n.DedupeKey) : null);

        if (!b.Cihazlar.TryGetValue(n.RecipientUserId, out var tumCihazlar) || tumCihazlar.Count == 0)
        {
            return Karar.Atla(NotificationOutcome.CihazYok);
        }

        // Android'de telefon ayarlarından kapatılan kanal: o cihaza gönderilmez. TÜM
        // cihazlarda kapalıysa satır KanalKapali (ayarlar ekranı bunu kullanıcıya gösteriyor).
        var cihazlar = tumCihazlar.Where(c => !c.KapaliKanallar.Contains(kanal, StringComparer.Ordinal)).ToList();
        if (cihazlar.Count == 0)
        {
            return Karar.Atla(NotificationOutcome.KanalKapali);
        }

        if (cihazlar.Count > SatirBasinaEnFazlaCihaz)
        {
            _logger.LogWarning(
                "Kullanıcı {UserId} için {Sayi} bağlı cihaz var; en son görülen {Tavan} tanesine gönderiliyor.",
                n.RecipientUserId, cihazlar.Count, SatirBasinaEnFazlaCihaz);
            cihazlar = cihazlar.Take(SatirBasinaEnFazlaCihaz).ToList();
        }

        var taslak = new BildirimTaslagi(
            n.Type,
            kanal,
            icerik!,
            Url(n),
            _etiket.Alici(n.RecipientUserId),
            Etiket(n, b),
            Ttl(n, b, now),
            n.Type == NotificationType.NewMessage && b.Rozetler.TryGetValue(n.RecipientUserId, out var rozet) ? rozet : null);

        return Karar.Gonder(taslak, cihazlar, okunmamislar);
    }

    /// <summary>
    /// Metni GÜNCEL veriden kurar; olay artık geçerli değilse <paramref name="atla"/> dolar.
    /// </summary>
    private BildirimIcerigi? Icerik(
        Notification n, Baglam b, KullaniciDurumu? aktor, DateTime now,
        out NotificationOutcome? atla, out IReadOnlyList<Guid>? okunmamislar)
    {
        atla = null;
        okunmamislar = null;

        switch (n.Type)
        {
            case NotificationType.NewMessage:
            {
                if (!b.Mesajlar.TryGetValue(n.RecordId, out var mesaj) || mesaj.Silindi || n.ConversationId is null || n.ActorUserId is null)
                {
                    atla = NotificationOutcome.DurumDegisti;
                    return null;
                }

                if (mesaj.OkunduUtc is not null)
                {
                    atla = NotificationOutcome.Okundu;
                    return null;
                }

                okunmamislar = b.Okunmamis.GetValueOrDefault((n.ConversationId.Value, n.ActorUserId.Value)) ?? [];
                if (okunmamislar.Count == 0)
                {
                    atla = NotificationOutcome.Okundu;
                    return null;
                }

                return BildirimMetni.YeniMesaj(aktor?.Ad, okunmamislar.Count);
            }

            case NotificationType.MatchRequest:
            {
                if (!b.Eslesmeler.TryGetValue(n.RecordId, out var m)
                    || m.Durum != MatchStatus.Pending
                    || MatchRules.SuresiDoldu(m.OlusturmaUtc, now))
                {
                    atla = NotificationOutcome.DurumDegisti;
                    return null;
                }

                if (n.ActorUserId is { } gonderen && b.Frenli.Contains((gonderen, n.RecipientUserId)))
                {
                    atla = NotificationOutcome.Tekrar;
                    return null;
                }

                // Konu katalogdan (güvenli); gönderenin adı YAZILMAZ (BildirimMetni kuralları).
                return BildirimMetni.YeniIstek(m.KonuId is { } k ? b.Konular.GetValueOrDefault(k) : null);
            }

            case NotificationType.MatchAccepted:
            {
                if (!b.Eslesmeler.TryGetValue(n.RecordId, out var m) || m.Durum != MatchStatus.Accepted)
                {
                    atla = NotificationOutcome.DurumDegisti;
                    return null;
                }

                return BildirimMetni.IstekKabul(aktor?.Ad);
            }

            case NotificationType.MatchExpiringDigest:
            {
                /* n ve "ilk düşecek" gönderim anında. Özetin konusu "düşmek üzere olanlar":
                   yuvaya aday olan istekler (düşmesine 30 saatten az) yanıtlandıysa ve
                   kalanların hiçbiri yakında düşmüyorsa gönderilmez — "300 saat sonra düşecek"
                   diye uyarmak özetin amacı değil. */
                if (!b.Ozetler.TryGetValue(n.RecipientUserId, out var ozet) || ozet.Sayi == 0)
                {
                    atla = NotificationOutcome.DurumDegisti;
                    return null;
                }

                var ilkDusme = MatchRules.DusmeAni(ozet.Ilk);
                var yuva = (n.ExpiresAtUtc ?? now) - HatirlatmaPenceresi.OzetSonra;
                if (ilkDusme > yuva + HatirlatmaPenceresi.OzetEnAzKalan + TimeSpan.FromDays(1))
                {
                    atla = NotificationOutcome.DurumDegisti;
                    return null;
                }

                return BildirimMetni.IstekDusecek(ozet.Sayi, ilkDusme - now);
            }

            case NotificationType.ApprovalPending:
            case NotificationType.AutoApproveSoon:
            {
                /* "Hâlâ bu tamamlama mı": durum VE damga. Damga itiraz reddinde yenilenir;
                   eski damganın satırı yeni tamamlama için gitmemeli (yenisinin kendi satırı
                   var). Damga anahtardan değil OlayDamgasiUtc sütunundan (BildirimAnahtarlari). */
                if (!b.Dersler.TryGetValue(n.RecordId, out var d)
                    || d.Durum != SessionStatus.AwaitingApproval
                    || d.TamamlamaUtc is null || n.OlayDamgasiUtc is null
                    || Utc(d.TamamlamaUtc.Value) != Utc(n.OlayDamgasiUtc.Value))
                {
                    atla = NotificationOutcome.DurumDegisti;
                    return null;
                }

                var kalan = OtomatikOnay(n.OlayDamgasiUtc.Value) - now;
                var konu = Konu(b, d.KonuId);

                if (n.Type == NotificationType.ApprovalPending)
                {
                    if (kalan <= TimeSpan.Zero)
                    {
                        atla = NotificationOutcome.Bayat;
                        return null;
                    }

                    var itirazSonrasi = b.ReddedilenItirazlar.Contains((n.RecordId, Utc(n.OlayDamgasiUtc.Value)));
                    return BildirimMetni.OnayBekliyor(konu, kalan, itirazSonrasi);
                }

                if (kalan <= OtoOnayEnAzKalan)
                {
                    atla = NotificationOutcome.Bayat;
                    return null;
                }

                return BildirimMetni.OtoOnayYaklasiyor(konu, kalan);
            }

            case NotificationType.LessonBooked:
            case NotificationType.LessonSoon:
            {
                if (!b.Dersler.TryGetValue(n.RecordId, out var d) || d.Durum != SessionStatus.Booked || d.BaslangicUtc <= now)
                {
                    atla = NotificationOutcome.DurumDegisti;
                    return null;
                }

                var kalan = d.BaslangicUtc - now;
                var konu = Konu(b, d.KonuId);

                /* Başlık için ofset anahtardan AYRIŞTIRILMIYOR (BildirimAnahtarlari kuralı):
                   gerçek kalan süreden. 60'lık hatırlatma en geç 45, 10'luk en erken ~15
                   dakika kala gidiyor; 30 dakika ikisini güvenle ayırır. */
                return n.Type == NotificationType.LessonBooked
                    ? BildirimMetni.DersPlanlandi(konu, kalan)
                    : BildirimMetni.DersYaklasiyor(konu, kalan, kalan > TimeSpan.FromMinutes(30) ? 60 : 10);
            }

            case NotificationType.LessonCancelled:
            {
                if (!b.Dersler.TryGetValue(n.RecordId, out var d) || d.Durum != SessionStatus.Cancelled)
                {
                    atla = NotificationOutcome.DurumDegisti;
                    return null;
                }

                return BildirimMetni.DersIptal(Konu(b, d.KonuId));
            }

            case NotificationType.Test:
            {
                var kanal = BildirimAnahtarlari.TestKanali(n.DedupeKey)
                            ?? throw new InvalidOperationException("Test satırının kanalı anahtardan okunamadı.");
                return BildirimMetni.Test(kanal);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(n), n.Type, "Bu bildirim türünün metni tanımlı değil.");
        }
    }

    /// <summary>
    /// Aynı (alıcı, sohbet) için sahiplenilen mesaj satırlarından yalnızca EN YENİSİ gider;
    /// aynı (gönderen → alan) çiftinin istek satırlarından yalnızca EN ESKİSİ.
    /// </summary>
    /// <remarks>
    /// Mesajda en yenisi: gövdedeki okunmamış sayısı hepsini zaten kapsıyor. İstekte en
    /// eskisi: çift freni "bu çift için bir bildirim yeter" diyor, hangisinin gittiği önemsiz,
    /// ama deterministik olsun. İki sunucu kopyası aynı çiftin iki isteğini aynı anda
    /// sahiplenirse ikisi de gidebilir; cihazda ortak etiket (i-…) onları tek bildirime indirir.
    /// </remarks>
    private static void BirlestirVeFrenle(List<Is> isler, Parti parti)
    {
        var elenen = new HashSet<Is>();

        foreach (var grup in isler.Where(i => i.Satir.Type == NotificationType.NewMessage)
                     .GroupBy(i => (i.Satir.RecipientUserId, i.Satir.ConversationId)))
        {
            foreach (var eski in grup.OrderByDescending(i => i.Satir.CreatedAtUtc).ThenByDescending(i => i.Satir.Id).Skip(1))
            {
                parti.Atla(eski.Satir.Id, NotificationOutcome.Birlestirildi);
                elenen.Add(eski);
            }
        }

        foreach (var grup in isler.Where(i => i.Satir.Type == NotificationType.MatchRequest)
                     .GroupBy(i => (i.Satir.ActorUserId, i.Satir.RecipientUserId)))
        {
            foreach (var yeni in grup.OrderBy(i => i.Satir.CreatedAtUtc).ThenBy(i => i.Satir.Id).Skip(1))
            {
                parti.Atla(yeni.Satir.Id, NotificationOutcome.Tekrar);
                elenen.Add(yeni);
            }
        }

        isler.RemoveAll(elenen.Contains);
    }

    /// <summary>
    /// Mesaj kısması: (alıcı, sohbet) başına 60 saniyede en fazla bir push. Yuva tek atomik
    /// ifadeyle alınır; iki sunucu kopyası aynı yuvayı alamaz. Alınamazsa satır, yuvanın
    /// açılacağı ana ertelenir (kirası bırakılır).
    /// </summary>
    /// <remarks>
    /// Yuva gönderimden ÖNCE alınıyor: sonra alınsaydı iki kopya aynı anda gönderip ikisi de
    /// yuvayı yazardı. Bedeli: gönderim geçici hatayla düşerse yuva boşuna harcanmış olur ve
    /// yeniden deneme en erken 60 sn sonra gider — telefonun iki kez çalmasından iyidir.
    /// </remarks>
    private async Task KismaAsync(List<Is> isler, Parti parti, DateTime now, CancellationToken ct)
    {
        var kisma = TimeSpan.FromSeconds(Math.Max(0, _push.MesajKismaSaniye));
        if (kisma == TimeSpan.Zero)
        {
            return;
        }

        var esik = now - kisma;
        var ertelenen = new List<Is>();

        foreach (var i in isler.Where(i => i.Satir.Type == NotificationType.NewMessage && i.Satir.ConversationId is not null))
        {
            var alici = i.Satir.RecipientUserId;
            var sohbet = i.Satir.ConversationId!.Value;

            var alindi = await _db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO comms."MessagePushThrottles" AS t ("RecipientUserId", "ConversationId", "LastSentAtUtc")
                VALUES ({alici}, {sohbet}, {now})
                ON CONFLICT ("RecipientUserId", "ConversationId") DO UPDATE SET "LastSentAtUtc" = EXCLUDED."LastSentAtUtc"
                WHERE t."LastSentAtUtc" <= {esik}
                """, ct);

            if (alindi > 0)
            {
                continue;
            }

            var sonGonderim = await _db.MessagePushThrottles.AsNoTracking()
                .Where(t => t.RecipientUserId == alici && t.ConversationId == sohbet)
                .Select(t => (DateTime?)t.LastSentAtUtc)
                .FirstOrDefaultAsync(ct);

            var acilis = sonGonderim is { } s ? Utc(s) + kisma : now;
            parti.Ertele(i.Satir.Id, acilis > now ? acilis : now + TimeSpan.FromSeconds(1));
            ertelenen.Add(i);
        }

        isler.RemoveAll(ertelenen.Contains);
    }

    // ═══ 4-5. Gönderim ══════════════════════════════════════════════════════════

    /// <summary>
    /// İşleri en fazla 100 mesajlık alt partilere böler; bir işin mesajları BÖLÜNMEZ (satırın
    /// sonucu bütün cihazlarının sonucundan türüyor ve sonuç, yanıttan hemen sonra yazılıyor).
    /// </summary>
    private static IEnumerable<List<Is>> AltPartiler(IReadOnlyList<Is> isler)
    {
        var parti = new List<Is>();
        var sayi = 0;
        foreach (var i in isler)
        {
            if (sayi + i.Mesajlar.Count > PushSinirlari.EnFazlaMesaj && parti.Count > 0)
            {
                yield return parti;
                parti = [];
                sayi = 0;
            }

            parti.Add(i);
            sayi += i.Mesajlar.Count;
        }

        if (parti.Count > 0)
        {
            yield return parti;
        }
    }

    /// <summary>
    /// Mesajları gönderir ve her mesajın sonucunu işaretler; istek düzeyi hatalarda
    /// yabancı token'ları ayırır ya da bölerek yeniden dener. Her Expo yanıtından hemen sonra
    /// bütün mesajları sonuçlanmış işlerin durumu yazılır.
    /// </summary>
    private async Task GonderAsync(List<CihazMesaji> mesajlar, int duzey, Parti parti, CancellationToken ct)
    {
        // Yük sınırını aşan mesaj Expo'ya gitmez: tek mesaj yüzünden bütün istek reddedilmesin.
        foreach (var m in mesajlar.Where(m => m.Sonuc is null && BildirimYuku.BaytBoyutu(m.Mesaj) > PushSinirlari.EnFazlaBayt))
        {
            _logger.LogError("Push yükü {Sinir} baytı aşıyor ({Tur}); gönderilmedi. Kod hatası: yük kurulumu sınırı korumuyor.",
                PushSinirlari.EnFazlaBayt, m.Is.Satir.Type);
            m.Sonuc = new MesajSonucu(MesajSonu.Kalici, null, PushHataKurali.MesajCokBuyuk, null);
        }

        mesajlar = mesajlar.Where(m => m.Sonuc is null).ToList();
        if (mesajlar.Count == 0)
        {
            return;
        }

        if (parti.Kira.Elapsed > KiraSuresi - KiraPayi)
        {
            // Yanıt kira bitmeden gelmeyebilir: gönderme, bırak. Bir sonraki tur yeniden alır.
            foreach (var m in mesajlar)
            {
                m.Sonuc = MesajSonucu.Gonderilmedi;
            }

            return;
        }

        PushGonderimSonucu sonuc;
        try
        {
            sonuc = await _gonderici.GonderAsync(mesajlar.Select(m => m.Mesaj).ToList(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sözleşme "fırlatmaz" diyor; yine de fırlatırsa geçici hata say, turu düşürme.
            _logger.LogError(ex, "Push göndericisi sözleşme dışı istisna fırlattı.");
            sonuc = PushGonderimSonucu.Hata(new PushIstekHatasi(null, "Istisna", PushTokenKurali.MetniMaskele(ex.Message), null));
        }

        if (sonuc.IstekHatasi is null)
        {
            if (sonuc.Biletler.Count != mesajlar.Count)
            {
                // Eşleme yalnızca sırayla kuruluyor; sayı tutmazsa hangi biletin kime ait
                // olduğu bilinemez. Geçici say: yeniden gönderim mükerrer olabilir ama sıradan
                // yanlış cihaza "ok" yazmaktan iyidir.
                _logger.LogError("Push yanıtında {Bilet} bilet var, {Mesaj} mesaj gönderildi.", sonuc.Biletler.Count, mesajlar.Count);
                IsaretleHepsi(mesajlar, new MesajSonucu(MesajSonu.Gecici, null, "YanitUyumsuz", null));
            }
            else
            {
                for (var i = 0; i < mesajlar.Count; i++)
                {
                    mesajlar[i].Sonuc = BiletSonucu(sonuc.Biletler[i]);
                }
            }
        }
        else
        {
            await IstekHatasiAsync(mesajlar, sonuc.IstekHatasi, duzey, parti, ct);
        }

        await TamamlananlariYazAsync(mesajlar.Select(m => m.Is).Distinct().ToList(), parti);
    }

    private async Task IstekHatasiAsync(
        List<CihazMesaji> mesajlar, PushIstekHatasi hata, int duzey, Parti parti, CancellationToken ct)
    {
        var karar = PushHataKurali.Istek(hata);

        if (karar == IstekKarari.YabanciDeneyim && YabanciTokenlar(hata) is { Count: > 0 } yabancilar)
        {
            var yabanciMesajlar = mesajlar.Where(m => yabancilar.Contains(m.Mesaj.To)).ToList();
            if (yabanciMesajlar.Count > 0)
            {
                foreach (var m in yabanciMesajlar)
                {
                    m.Sonuc = new MesajSonucu(MesajSonu.Yabanci, null, PushHataKurali.CokDeneyim, null);
                    parti.OluCihazlar.Add(m.CihazId);
                }

                /* Kullanıcı kimliği yazılıyor, token YAZILMIYOR. Aynı kullanıcı tekrar tekrar
                   yabancı token kaydediyorsa (başka bir Expo uygulamasından) iz burada. */
                _logger.LogWarning(
                    "Başka bir Expo projesine ait {Sayi} push token'ı partiden ayrıldı ve cihaz kayıtları silinecek. Kullanıcılar: {Kullanicilar}",
                    yabanciMesajlar.Count, string.Join(", ", yabanciMesajlar.Select(m => m.Is.Satir.RecipientUserId).Distinct()));

                // Kalanlar DENEME SAYILMADAN ve aynı düzeyde hemen yeniden: onların suçu yok.
                await GonderAsync(mesajlar.Except(yabanciMesajlar).ToList(), duzey, parti, ct);
                return;
            }
        }

        if (karar != IstekKarari.Gecici)
        {
            // Bölünebilir (ya da yabancılar ayrıştırılamadı: bölmek tek projeli alt istekler üretir).
            if (mesajlar.Count > 1 && duzey < BolmeDerinligi)
            {
                var yari = (mesajlar.Count + 1) / 2;
                await GonderAsync(mesajlar.Take(yari).ToList(), duzey + 1, parti, ct);
                await GonderAsync(mesajlar.Skip(yari).ToList(), duzey + 1, parti, ct);
                return;
            }

            _logger.LogWarning("Push isteği kalıcı olarak reddedildi (HTTP {Durum}, {Kod}): {Mesaj}",
                hata.HttpDurumu, hata.Kod, hata.Mesaj);
            IsaretleHepsi(mesajlar, new MesajSonucu(MesajSonu.Kalici, null, hata.Kod ?? $"HTTP {hata.HttpDurumu}", hata.Mesaj));
            return;
        }

        if (PushHataKurali.YetkiHatasi(hata))
        {
            _logger.LogCritical(
                "Expo push isteği YETKİ hatasıyla reddedildi (HTTP {Durum}, {Kod}). Push:AccessToken yanlış ya da " +
                "Enhanced Security açık ama token verilmemiş. Hiçbir bildirim gitmiyor; satırlar beklemeyle yeniden denenecek.",
                hata.HttpDurumu, hata.Kod);
        }
        else
        {
            _logger.LogWarning("Push isteği geçici hatayla düştü (HTTP {Durum}, {Kod}): {Mesaj}",
                hata.HttpDurumu, hata.Kod, hata.Mesaj);
        }

        IsaretleHepsi(mesajlar, new MesajSonucu(MesajSonu.Gecici, null, hata.Kod ?? $"HTTP {hata.HttpDurumu}", hata.Mesaj));
    }

    /// <summary>
    /// PUSH_TOO_MANY_EXPERIENCE_IDS ayrıntısından BİZİM deneyimimiz dışındaki token'lar.
    /// Bizim deneyimimiz haritada yoksa BOŞ döner: yapılandırma yanlış olabilir (slug
    /// değişmiş) ve o durumda "hepsi yabancı" deyip bütün cihazları silmek felaket olurdu.
    /// Çağıran o zaman bölmeye düşer; tek token'lı istekler bu hatayı hiç üretmez.
    /// </summary>
    private HashSet<string> YabanciTokenlar(PushIstekHatasi hata)
    {
        var harita = hata.DeneyimTokenlari;
        var bizim = _push.DeneyimKimligi?.Trim();
        if (harita is null || string.IsNullOrEmpty(bizim)
            || !harita.Keys.Any(k => string.Equals(k, bizim, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogError(
                "PUSH_TOO_MANY_EXPERIENCE_IDS geldi ama Push:DeneyimKimligi ('{Bizim}') Expo'nun listesinde yok; " +
                "yabancı token'lar ayrılamadı, parti bölünerek gönderilecek. Deneyimler: {Deneyimler}",
                bizim, harita is null ? "-" : string.Join(", ", harita.Keys));
            return [];
        }

        return harita
            .Where(kv => !string.Equals(kv.Key, bizim, StringComparison.OrdinalIgnoreCase))
            .SelectMany(kv => kv.Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static MesajSonucu BiletSonucu(PushBileti bilet) => PushHataKurali.Bilet(bilet) switch
    {
        BiletKarari.Basarili => new MesajSonucu(MesajSonu.Basarili, bilet.BiletId, null, null),
        BiletKarari.CihazOlu => new MesajSonucu(MesajSonu.CihazOlu, null, bilet.HataKodu, bilet.HataMesaji),
        BiletKarari.Gecici => new MesajSonucu(MesajSonu.Gecici, null, bilet.HataKodu, bilet.HataMesaji),
        _ => new MesajSonucu(MesajSonu.Kalici, null, bilet.HataKodu, bilet.HataMesaji)
    };

    private static void IsaretleHepsi(IEnumerable<CihazMesaji> mesajlar, MesajSonucu sonuc)
    {
        foreach (var m in mesajlar)
        {
            m.Sonuc = sonuc;
        }
    }

    // ═══ 6. Sonuç yazımı ════════════════════════════════════════════════════════

    /// <summary>
    /// Bütün mesajları sonuçlanmış ve henüz yazılmamış işlerin durumunu yazar. Kira payı
    /// yüzünden gönderilmeyen iş (Gonderilmedi) kirası bırakılarak kuyruğa döner.
    /// </summary>
    private async Task TamamlananlariYazAsync(IEnumerable<Is> isler, Parti parti)
    {
        var yazilacak = new Parti(parti.Tur, parti.Kira);

        foreach (var i in isler)
        {
            if (i.Yazildi)
            {
                continue;
            }

            if (i.Mesajlar.Any(m => m.Sonuc is null))
            {
                // Mesajlarından biri henüz gönderilmedi (bölmenin öbür yarısında): bekle.
                continue;
            }

            i.Yazildi = true;
            var sonuclar = i.Mesajlar.Select(m => m.Sonuc!).ToList();

            foreach (var m in i.Mesajlar.Where(m => m.Sonuc!.Tur == MesajSonu.CihazOlu))
            {
                parti.OluCihazlar.Add(m.CihazId);
            }

            if (sonuclar.Any(s => s.Tur == MesajSonu.Basarili))
            {
                yazilacak.Gonderildi(i.Satir.Id);
                foreach (var m in i.Mesajlar.Where(m => m.Sonuc!.Tur == MesajSonu.Basarili && m.Sonuc.BiletId is not null))
                {
                    parti.Biletler.Add((m.Sonuc!.BiletId!, m.CihazId, i.Satir.Id));
                }

                if (i.Satir.Type == NotificationType.NewMessage && i.Okunmamislar is { Count: > 0 })
                {
                    parti.KapsananMesajlar.Add(i);
                }

                continue;
            }

            if (sonuclar.Any(s => s.Tur == MesajSonu.Gonderilmedi))
            {
                yazilacak.Birak(i.Satir.Id);
                continue;
            }

            if (sonuclar.FirstOrDefault(s => s.Tur == MesajSonu.Gecici) is { } gecici)
            {
                var hata = PushHataKurali.SonHata(gecici.Kod, gecici.Mesaj);
                var deneme = i.Satir.Attempts + 1;
                if (deneme >= PushHataKurali.EnFazlaDeneme)
                {
                    _logger.LogWarning("Bildirim {Id} ({Tur}) {Deneme} denemeden sonra gönderilemedi: {Hata}",
                        i.Satir.Id, i.Satir.Type, deneme, hata);
                    yazilacak.Basarisiz(i.Satir.Id, hata);
                }
                else
                {
                    yazilacak.YenidenDene(i.Satir.Id, i.Satir.Attempts, hata);
                }

                continue;
            }

            if (sonuclar.All(s => s.Tur is MesajSonu.CihazOlu or MesajSonu.Yabanci))
            {
                yazilacak.Atla(i.Satir.Id, NotificationOutcome.CihazGecersiz);
                continue;
            }

            var kalici = sonuclar.First(s => s.Tur == MesajSonu.Kalici);
            if (PushHataKurali.YapilandirmaHatasi(kalici.Kod))
            {
                _logger.LogError("Push bileti yapılandırma/kod hatası döndü ({Kod}) — bildirim {Id} ({Tur}) gönderilemedi. " +
                                 "Tek kullanıcının değil herkesin sorunu olabilir.", kalici.Kod, i.Satir.Id, i.Satir.Type);
            }

            yazilacak.Basarisiz(i.Satir.Id, PushHataKurali.SonHata(kalici.Kod, kalici.Mesaj));
        }

        await yazilacak.YazAsync(this);
        parti.Topla(yazilacak);
    }

    /// <summary>
    /// Durum yazımı. Hepsi fencing'li: <c>LeaseOwner = @tur AND Status = 'Pending'</c>.
    /// </summary>
    /// <remarks>
    /// Kendi 10 saniyelik jetonu var (bkz. sınıf açıklaması). Düşerse istisna yukarı gider ve
    /// tur biter: satırlar kiralı kalır, kira dolunca yeniden alınır. Yutulsaydı aynı turun
    /// devamı, veritabanına yazamazken Expo'ya göndermeye devam ederdi.
    /// </remarks>
    private async Task<(int Gonderilen, int Atlanan, int Ertelenen, int Basarisiz)> DurumlariYazAsync(Parti p)
    {
        using var zaman = new CancellationTokenSource(SonucYazimSuresi);
        var ct = zaman.Token;
        var now = Utc(_clock.UtcNow);
        var tur = p.Tur;
        int gonderilen = 0, atlanan = 0, ertelenen = 0, basarisiz = 0;

        IQueryable<Notification> Benim(IReadOnlyCollection<Guid> idler)
            => _db.Notifications.Where(n => idler.Contains(n.Id) && n.LeaseOwner == tur && n.Status == NotificationStatus.Pending);

        if (p.GonderilenIdler.Count > 0)
        {
            gonderilen += await Benim(p.GonderilenIdler).ExecuteUpdateAsync(s => s
                .SetProperty(n => n.Status, NotificationStatus.Sent)
                .SetProperty(n => n.Outcome, (NotificationOutcome?)null)
                .SetProperty(n => n.ProcessedAtUtc, now)
                .SetProperty(n => n.LastError, (string?)null), ct);
        }

        foreach (var grup in p.Atlananlar.GroupBy(x => x.Sonuc))
        {
            var idler = grup.Select(x => x.Id).ToList();
            NotificationOutcome? sonuc = grup.Key;
            atlanan += await Benim(idler).ExecuteUpdateAsync(s => s
                .SetProperty(n => n.Status, NotificationStatus.Skipped)
                .SetProperty(n => n.Outcome, sonuc)
                .SetProperty(n => n.ProcessedAtUtc, now), ct);
        }

        foreach (var grup in p.Basarisizlar.GroupBy(x => x.Hata))
        {
            var idler = grup.Select(x => x.Id).ToList();
            var hata = grup.Key;
            basarisiz += await Benim(idler).ExecuteUpdateAsync(s => s
                .SetProperty(n => n.Status, NotificationStatus.Failed)
                .SetProperty(n => n.ProcessedAtUtc, now)
                .SetProperty(n => n.LastError, hata), ct);
        }

        /* Yeniden deneme: deneme sayısı SQL'de artırılıyor; bekleme sahiplenmede okunan
           sayıdan hesaplanıyor. Satır bizim kiramızda olduğu için sayı arada değişemez
           (fencing). Aynı sayı ve aynı hatayı taşıyan satırlar tek ifadede: Expo kesintisinde
           bütün parti aynı hatayla döner, satır başına ifade 200 gidiş-dönüş olurdu. */
        foreach (var grup in p.Denenecekler.GroupBy(x => (x.OncekiDeneme, x.Hata)))
        {
            var idler = grup.Select(x => x.Id).ToList();
            var due = now + PushHataKurali.Bekleme(grup.Key.OncekiDeneme + 1);
            var hata = grup.Key.Hata;
            ertelenen += await Benim(idler).ExecuteUpdateAsync(s => s
                .SetProperty(n => n.Attempts, n => n.Attempts + 1)
                .SetProperty(n => n.DueAtUtc, due)
                .SetProperty(n => n.LastError, hata)
                .SetProperty(n => n.LeaseOwner, (Guid?)null)
                .SetProperty(n => n.LeaseUntilUtc, (DateTime?)null), ct);
        }

        // Kısma ertelemesi: deneme sayılmaz, hata değil.
        foreach (var x in p.Ertelenenler)
        {
            var id = x.Id;
            var due = x.DueAtUtc;
            ertelenen += await Benim([id]).ExecuteUpdateAsync(s => s
                .SetProperty(n => n.DueAtUtc, due)
                .SetProperty(n => n.LeaseOwner, (Guid?)null)
                .SetProperty(n => n.LeaseUntilUtc, (DateTime?)null), ct);
        }

        if (p.Birakilanlar.Count > 0)
        {
            ertelenen += await Benim(p.Birakilanlar).ExecuteUpdateAsync(s => s
                .SetProperty(n => n.LeaseOwner, (Guid?)null)
                .SetProperty(n => n.LeaseUntilUtc, (DateTime?)null), ct);
        }

        p.Temizle();
        return (gonderilen, atlanan, ertelenen, basarisiz);
    }

    // ═══ 7. En iyi çaba ═════════════════════════════════════════════════════════

    /// <summary>
    /// Biletler, ölü cihazlar ve kapsanan mesaj satırları. Durumdan SONRA ve ayrı ayrı; biri
    /// düşerse uyarı yazılır, ne durum ne de diğerleri geri alınır.
    /// </summary>
    private async Task EnIyiCabaAsync(Parti p)
    {
        using var zaman = new CancellationTokenSource(SonucYazimSuresi);
        var ct = zaman.Token;
        var now = Utc(_clock.UtcNow);

        if (p.Biletler.Count > 0)
        {
            try
            {
                // Bilet kimliği tekil; aynı bilet iki kez yazılamaz ama yazılmaya çalışılırsa
                // (en az bir kez teslimat) çakışma bütün yazımı düşürmesin.
                var biletler = p.Biletler.Where(b => b.BiletId.Length <= 64).ToList();
                var idler = biletler.Select(_ => Guid.NewGuid()).ToArray();
                var biletIdleri = biletler.Select(b => b.BiletId).ToArray();
                var cihazlar = biletler.Select(b => b.CihazId).ToArray();
                var bildirimler = biletler.Select(b => b.BildirimId).ToArray();

                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO comms."PushTickets" ("Id", "CreatedAtUtc", "TicketId", "PushDeviceId", "NotificationId")
                    SELECT v.id, {now}, v.bilet, v.cihaz, v.bildirim
                    FROM unnest({idler}, {biletIdleri}, {cihazlar}, {bildirimler}) AS v(id, bilet, cihaz, bildirim)
                    ON CONFLICT ("TicketId") DO NOTHING
                    """, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Push biletleri yazılamadı ({Sayi}); makbuz kontrolü bu biletler için yapılmayacak.", p.Biletler.Count);
            }
        }

        if (p.OluCihazlar.Count > 0)
        {
            try
            {
                // ExecuteDelete: satır arada silindiyse 0 satır, hata değil.
                var olu = p.OluCihazlar.ToList();
                p.SilinenCihaz += await _db.PushDevices.Where(d => olu.Contains(d.Id)).ExecuteDeleteAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ölü push cihazları silinemedi ({Sayi}); bir sonraki gönderimde yeniden denenecek.", p.OluCihazlar.Count);
            }
        }

        /* Kapsanan mesajlar: gönderilen bildirimin okunmamış sayısına ZATEN giren mesajların
           bekleyen satırları. Kısma onları 60 sn sonraya ertelerdi ve kullanıcı aynı bilgiyi
           ("2 yeni mesaj") ikinci kez, telefon yeniden çalarak alırdı. Başka bir turun
           kirasındaki satıra dokunulmaz. Düşerse zararı yalnızca o ikinci bildirim. */
        foreach (var i in p.KapsananMesajlar)
        {
            try
            {
                var alici = i.Satir.RecipientUserId;
                var sohbet = i.Satir.ConversationId;
                var mesajlar = i.Okunmamislar!.ToList();
                var kendisi = i.Satir.Id;

                p.KapsananSayisi += await _db.Notifications
                    .Where(n => n.Type == NotificationType.NewMessage && n.RecipientUserId == alici && n.ConversationId == sohbet
                                && n.Status == NotificationStatus.Pending && n.Id != kendisi
                                && mesajlar.Contains(n.RecordId)
                                && (n.LeaseUntilUtc == null || n.LeaseUntilUtc < now))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(n => n.Status, NotificationStatus.Skipped)
                        .SetProperty(n => n.Outcome, (NotificationOutcome?)NotificationOutcome.Birlestirildi)
                        .SetProperty(n => n.ProcessedAtUtc, now), ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kapsanan mesaj bildirimleri birleştirilemedi (bildirim {Id}).", i.Satir.Id);
            }
        }
    }

    // ═══ Yardımcılar ════════════════════════════════════════════════════════════

    private static bool DersTuru(NotificationType t) => t is NotificationType.ApprovalPending or NotificationType.AutoApproveSoon
        or NotificationType.LessonBooked or NotificationType.LessonCancelled or NotificationType.LessonSoon;

    /// <summary>Kişiyi (adıyla ya da isteğiyle) getiren türler: engel ve aktör durumu bunlarda sıkı.</summary>
    private static bool KisiTuru(NotificationType t) => t is NotificationType.NewMessage
        or NotificationType.MatchRequest or NotificationType.MatchAccepted;

    /// <summary>
    /// Kullanıcı uygulamayı şu an kullanabiliyor mu? AccountStatusMiddleware ile aynı kural:
    /// süresi geçmiş askı fiilen bitmiştir (Status'u düzeltecek işi beklemeden).
    /// </summary>
    private static bool Erisebilir(KullaniciDurumu k, DateTime now) => k.Durum switch
    {
        UserStatus.Active => true,
        UserStatus.Suspended => k.AskiBitisUtc is { } bitis && bitis <= now,
        _ => false
    };

    /// <summary>
    /// Aktör (olayı doğuran kişi) koşulu türe göre: mesaj ve istekte aktör ETKİN olmalı
    /// (askıdaki kişinin mesajı bildirim olarak gelmesin); ders ve onay türlerinde yalnızca
    /// banlı ya da silinmiş karşı taraf eler — ders öğrencinin kendi dersi, karşı tarafın
    /// geçici askısı öğrencinin onay/itiraz süresini durdurmuyor.
    /// </summary>
    private static bool AktorUygun(NotificationType tur, KullaniciDurumu aktor, DateTime now)
        => KisiTuru(tur) ? Erisebilir(aktor, now) : aktor.Durum is not (UserStatus.Banned or UserStatus.Deleted);

    private static (Guid, Guid) Cift(Guid a, Guid b) => a.CompareTo(b) < 0 ? (a, b) : (b, a);

    private DateTime OtomatikOnay(DateTime damga) => Utc(damga).AddHours(_ekonomi.AutoApproveHours);

    private static string Konu(Baglam b, Guid konuId) => b.Konular.GetValueOrDefault(konuId) ?? "Bir";

    /// <summary>Mobilin rota beyaz listesiyle birebir (dersmate Mobil → src/lib/bildirimler.js ROTALAR).</summary>
    private static string Url(Notification n) => n.Type switch
    {
        NotificationType.NewMessage => $"/sohbet/{n.ConversationId:D}",
        NotificationType.MatchAccepted => n.ConversationId is { } s ? $"/sohbet/{s:D}" : "/eslesmeler?sekme=active",
        NotificationType.MatchRequest or NotificationType.MatchExpiringDigest => "/eslesmeler?sekme=incoming",
        NotificationType.Test => "/bildirimler",
        _ => $"/dersler?ders={n.RecordId:D}"
    };

    private string Etiket(Notification n, Baglam b) => n.Type switch
    {
        NotificationType.NewMessage => _etiket.Sohbet(n.ConversationId!.Value),
        // İstek ve kabul AYNI çift etiketi (gönderen → alan): cihazda çift başına tek bildirim.
        NotificationType.MatchRequest => _etiket.IstekCifti(n.ActorUserId ?? Guid.Empty, n.RecipientUserId),
        NotificationType.MatchAccepted => b.Eslesmeler.TryGetValue(n.RecordId, out var m)
            ? _etiket.IstekCifti(m.GonderenId, m.AlanId)
            : _etiket.IstekCifti(n.RecipientUserId, n.ActorUserId ?? Guid.Empty),
        NotificationType.MatchExpiringDigest => _etiket.IstekOzeti(n.RecipientUserId),
        NotificationType.Test => _etiket.Test(n.Id),
        _ => _etiket.Ders(n.RecordId)
    };

    /// <summary>
    /// Sağlayıcının teslim etmeye çalışacağı süre. Geç teslim edilen bildirim yanlış bilgidir:
    /// mesajda satırın ömrü, istekte düşme anı, derslerde başlangıç, onayda otomatik onay anı.
    /// </summary>
    private int? Ttl(Notification n, Baglam b, DateTime now)
    {
        DateTime? son = n.Type switch
        {
            NotificationType.LessonSoon or NotificationType.LessonBooked or NotificationType.LessonCancelled
                => b.Dersler.TryGetValue(n.RecordId, out var d) ? d.BaslangicUtc : n.ExpiresAtUtc,
            NotificationType.ApprovalPending or NotificationType.AutoApproveSoon
                => n.OlayDamgasiUtc is { } damga ? OtomatikOnay(damga) : n.ExpiresAtUtc,
            _ => n.ExpiresAtUtc
        };

        return son is { } s ? BildirimYuku.TtlSaniye(Utc(s) - now) : null;
    }

    private static DateTime Utc(DateTime deger) => DateTime.SpecifyKind(deger, DateTimeKind.Utc);

    // ═══ İç tipler ══════════════════════════════════════════════════════════════

    private sealed record KullaniciDurumu(Guid Id, UserStatus Durum, DateTime? AskiBitisUtc, string Ad);

    private sealed record Cihaz(Guid Id, Guid UserId, string Token, PushPlatform Platform, string[] KapaliKanallar, DateTime LastSeenAtUtc);

    private sealed record MesajDurumu(Guid Id, DateTime? OkunduUtc, bool Silindi);

    private sealed record EslesmeDurumu(Guid Id, MatchStatus Durum, Guid GonderenId, Guid AlanId, Guid? KonuId, DateTime OlusturmaUtc);

    private sealed record DersDurumu(Guid Id, SessionStatus Durum, Guid KonuId, DateTime BaslangicUtc, DateTime? TamamlamaUtc);

    private sealed class Baglam
    {
        public Dictionary<Guid, KullaniciDurumu> Kullanicilar { get; set; } = [];
        public Dictionary<Guid, NotificationPreference> Tercihler { get; set; } = [];
        public HashSet<(Guid, Guid)> Engeller { get; } = [];
        public Dictionary<Guid, List<Cihaz>> Cihazlar { get; set; } = [];
        public Dictionary<Guid, MesajDurumu> Mesajlar { get; set; } = [];
        public Dictionary<(Guid Sohbet, Guid Gonderen), IReadOnlyList<Guid>> Okunmamis { get; set; } = [];
        public Dictionary<Guid, int> Rozetler { get; set; } = [];
        public Dictionary<Guid, EslesmeDurumu> Eslesmeler { get; set; } = [];
        public HashSet<(Guid Gonderen, Guid Alan)> Frenli { get; } = [];
        public Dictionary<Guid, (int Sayi, DateTime Ilk)> Ozetler { get; set; } = [];
        public Dictionary<Guid, DersDurumu> Dersler { get; set; } = [];
        public HashSet<(Guid Ders, DateTime Damga)> ReddedilenItirazlar { get; } = [];
        public Dictionary<Guid, string> Konular { get; set; } = [];
    }

    private enum KararTuru { Gonder, Atla }

    private sealed record Karar(
        KararTuru Tur, NotificationOutcome? Sonuc, BildirimTaslagi? Taslak,
        IReadOnlyList<Cihaz>? Cihazlar, IReadOnlyList<Guid>? Okunmamislar)
    {
        public static Karar Atla(NotificationOutcome sonuc) => new(KararTuru.Atla, sonuc, null, null, null);
        public static Karar Gonder(BildirimTaslagi t, IReadOnlyList<Cihaz> c, IReadOnlyList<Guid>? o)
            => new(KararTuru.Gonder, null, t, c, o);
    }

    /// <summary>Gönderilecek bir satır: taslak bir kez, mesaj her cihaz için bir kez.</summary>
    private sealed class Is
    {
        public Is(Notification satir, BildirimTaslagi taslak, IReadOnlyList<Cihaz> cihazlar, IReadOnlyList<Guid>? okunmamislar)
        {
            Satir = satir;
            Okunmamislar = okunmamislar;
            Mesajlar = cihazlar
                .Select(c => new CihazMesaji(this, c.Id, BildirimYuku.Kur(taslak, c.Token, c.Platform)))
                .ToList();
        }

        public Notification Satir { get; }
        public IReadOnlyList<Guid>? Okunmamislar { get; }
        public List<CihazMesaji> Mesajlar { get; }
        public bool Yazildi { get; set; }
    }

    private sealed class CihazMesaji(Is sahip, Guid cihazId, PushMesaji mesaj)
    {
        public Is Is { get; } = sahip;
        public Guid CihazId { get; } = cihazId;
        public PushMesaji Mesaj { get; } = mesaj;
        public MesajSonucu? Sonuc { get; set; }
    }

    private enum MesajSonu { Basarili, CihazOlu, Yabanci, Gecici, Kalici, Gonderilmedi }

    private sealed record MesajSonucu(MesajSonu Tur, string? BiletId, string? Kod, string? Mesaj)
    {
        public static MesajSonucu Gonderilmedi { get; } = new(MesajSonu.Gonderilmedi, null, null, null);
    }

    /// <summary>Bir partinin yazılmayı bekleyen kararları ve biriken sonucu.</summary>
    private sealed class Parti(Guid tur, Stopwatch kira)
    {
        public Guid Tur { get; } = tur;
        public Stopwatch Kira { get; } = kira;

        public List<Guid> GonderilenIdler { get; } = [];
        public List<(Guid Id, NotificationOutcome Sonuc)> Atlananlar { get; } = [];
        public List<(Guid Id, string Hata)> Basarisizlar { get; } = [];
        public List<(Guid Id, int OncekiDeneme, string Hata)> Denenecekler { get; } = [];
        public List<(Guid Id, DateTime DueAtUtc)> Ertelenenler { get; } = [];
        public List<Guid> Birakilanlar { get; } = [];

        public List<(string BiletId, Guid CihazId, Guid BildirimId)> Biletler { get; } = [];
        public HashSet<Guid> OluCihazlar { get; } = [];
        public List<Is> KapsananMesajlar { get; } = [];
        public int SilinenCihaz { get; set; }
        public int KapsananSayisi { get; set; }

        private int _gonderilen, _atlanan, _ertelenen, _basarisiz;

        public void Gonderildi(Guid id) => GonderilenIdler.Add(id);
        public void Atla(Guid id, NotificationOutcome sonuc) => Atlananlar.Add((id, sonuc));
        public void Basarisiz(Guid id, string hata) => Basarisizlar.Add((id, hata));
        public void YenidenDene(Guid id, int oncekiDeneme, string hata) => Denenecekler.Add((id, oncekiDeneme, hata));
        public void Ertele(Guid id, DateTime due) => Ertelenenler.Add((id, due));
        public void Birak(Guid id) => Birakilanlar.Add(id);

        public async Task YazAsync(DispatchNotificationsHandler h)
        {
            var (g, a, e, b) = await h.DurumlariYazAsync(this);
            _gonderilen += g;
            _atlanan += a;
            _ertelenen += e;
            _basarisiz += b;
        }

        public void Topla(Parti alt)
        {
            _gonderilen += alt._gonderilen;
            _atlanan += alt._atlanan;
            _ertelenen += alt._ertelenen;
            _basarisiz += alt._basarisiz;
        }

        public void Temizle()
        {
            GonderilenIdler.Clear();
            Atlananlar.Clear();
            Basarisizlar.Clear();
            Denenecekler.Clear();
            Ertelenenler.Clear();
            Birakilanlar.Clear();
        }

        public PushIsSonucu Sonuc => new(0, _gonderilen, _atlanan + KapsananSayisi, _ertelenen, _basarisiz, SilinenCihaz, 0);
    }
}
