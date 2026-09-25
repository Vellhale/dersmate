using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Domain.Communication;

namespace PeerLearn.Application.Features.Communication.Bildirimler;

/// <summary>Bildirim defteri ve mesaj kısma tablosunun yaş temizliği.</summary>
public sealed record CleanupNotificationsCommand : IRequest<PushIsSonucu>;

/// <summary>
/// Günde bir (PushReceiptJob'un günlük adımı) ve makbuz ucuyla birlikte elle.
/// </summary>
/// <remarks>
/// ─── NE SİLİNİYOR ───────────────────────────────────────────────────────────
/// <list type="bullet">
/// <item>İşlenmiş (Sent/Skipped/Failed) ve 30 günden eski defter satırları. 30 gün en uzun
/// tekilleştirme ihtiyacından güvenle büyük: istek freni 7 gün, istek ömrü 14 gün. Satır
/// erken silinseydi aynı olayın ikinci satırı (ör. aynı çiftin freni) "ilk kez" sayılırdı.</item>
/// <item>Son gönderimi 1 günden eski kısma yuvaları: 60 saniyelik kısma için bir gün sonra
/// hiçbir anlamları yok.</item>
/// </list>
///
/// ─── NE İŞARETLENİYOR ───────────────────────────────────────────────────────
/// Ömrü bir günden fazla önce dolmuş ama hâlâ Pending kalan satırlar Skipped(Bayat) olur.
/// Normalde dağıtıcı onları ilk sahiplenmede zaten Bayat yazar; buraya kalan satır, sunucu
/// uzun süre kapalı kaldığında ya da dağıtım durduğunda birikenlerdir. Kirası süren (başka
/// bir turun elindeki) satıra dokunulmaz.
///
/// Pending satırlar yaşları ne olursa olsun SİLİNMEZ: işlenmemiş bir olay sessizce kaybolmasın,
/// önce Bayat olarak iz bıraksın, 30 gün sonra silinsin.
/// </remarks>
public sealed class CleanupNotificationsHandler : IRequestHandler<CleanupNotificationsCommand, PushIsSonucu>
{
    public const int SaklamaGunu = 30;

    public static readonly TimeSpan BayatPayi = TimeSpan.FromDays(1);

    public static readonly TimeSpan KismaSaklama = TimeSpan.FromDays(1);

    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public CleanupNotificationsHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<PushIsSonucu> Handle(CleanupNotificationsCommand request, CancellationToken ct)
    {
        var now = DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc);
        var saklamaEsigi = now.AddDays(-SaklamaGunu);
        var bayatEsigi = now - BayatPayi;
        var kismaEsigi = now - KismaSaklama;

        var silinen = await _db.Notifications
            .Where(n => n.Status != NotificationStatus.Pending && n.ProcessedAtUtc < saklamaEsigi)
            .ExecuteDeleteAsync(ct);

        var bayat = await _db.Notifications
            .Where(n => n.Status == NotificationStatus.Pending && n.ExpiresAtUtc < bayatEsigi
                        && (n.LeaseUntilUtc == null || n.LeaseUntilUtc < now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.Status, NotificationStatus.Skipped)
                .SetProperty(n => n.Outcome, (NotificationOutcome?)NotificationOutcome.Bayat)
                .SetProperty(n => n.ProcessedAtUtc, now), ct);

        silinen += await _db.MessagePushThrottles
            .Where(t => t.LastSentAtUtc < kismaEsigi)
            .ExecuteDeleteAsync(ct);

        return PushIsSonucu.Bos with { Atlanan = bayat, SilinenKayit = silinen };
    }
}
