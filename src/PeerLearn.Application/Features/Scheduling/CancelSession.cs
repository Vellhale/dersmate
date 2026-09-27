using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Features.Communication.Bildirimler;
using PeerLearn.Application.Scheduling;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Application.Features.Scheduling;

/// <summary>
/// Ders başlamadan iptal: ders Cancelled olur.
/// </summary>
/// <remarks>
/// İADE ADIMI KALKTI. Eskiden iptal, escrow'daki krediyi öğrencinin lotlarına geri
/// yazıyordu; öğrenci artık hiçbir şey ödemediği için iade edilecek bir şey de yok.
/// Cüzdan kilidi de bu yüzden kaldırıldı — kilitlenecek kaynak kalmadı.
///
/// Ödül basımı yalnızca ONAY anında olur; iptal edilen ders hiç basım üretmez, dolayısıyla
/// burada geri alınacak bir puan da yoktur.
/// </remarks>
public sealed record CancelSessionCommand(Guid SessionId, Guid CallerUserId, string? Reason)
    : IRequest;

public sealed class CancelSessionHandler : IRequestHandler<CancelSessionCommand>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly IBildirimSinyali _sinyal;

    public CancelSessionHandler(IAppDbContext db, IClock clock, IBildirimSinyali sinyal)
    {
        _db = db;
        _clock = clock;
        _sinyal = sinyal;
    }

    public async Task Handle(CancelSessionCommand request, CancellationToken ct)
    {
        var preview = await _db.LessonSessions.AsNoTracking()
                          .SingleOrDefaultAsync(s => s.Id == request.SessionId, ct)
                      ?? throw new AppException(ErrorCodes.SessionNotFound, "Ders bulunamadı.", statusCode: 404);

        SessionRules.EnsureCanCancel(preview, request.CallerUserId, _clock.UtcNow);

        await ConcurrencyRetry.RunAsync<object?>(_db, async () =>
        {
            await using var tx = await _db.BeginTransactionAsync(cancellationToken: ct);

            // Kilit altında taze okuma: preview'dan sonra durum değişmiş olabilir.
            var session = await _db.LessonSessions.SingleAsync(s => s.Id == request.SessionId, ct);
            var now = _clock.UtcNow;
            SessionRules.EnsureCanCancel(session, request.CallerUserId, now);

            session.Status = SessionStatus.Cancelled;
            session.CancelledAtUtc = now;
            session.CancelledByUserId = request.CallerUserId;
            session.CancelReason = request.Reason?.Trim();

            /*
              PUSH: iptal etmeyen tarafa "ders iptal edildi" — iptalle AYNI transaction'da.

              Aynı dersin bildirimleri cihazda ortak etiketi paylaşıyor; bu bildirim teslim
              edilmiş bir "1 saat sonra başlıyor"un YERİNE geçer. Olmasaydı karşı taraf iptal
              edilmiş derse hazırlanırdı. Bekleyen "yaklaşıyor" satırları dağıtıcıda durum
              kontrolüyle (Booked değil) atlanır; burada silinmez.

              İptal yalnızca dersin iki tarafından birine açık (SessionRules.EnsureCanCancel),
              yani alıcı her zaman "diğeri". İptal nedeni metne girmez (serbest metin).
              Lambda içinde yalnızca Ekle; sinyal RunAsync döndükten sonra (BookSession notu).
            */
            var alici = session.TutorUserId == request.CallerUserId
                ? session.StudentUserId
                : session.TutorUserId;
            BildirimKuyrugu.Ekle(_db, BildirimKuyrugu.DersIptal(
                session.Id, alici, request.CallerUserId, now, session.ScheduledStartUtc));

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return null;
        }, ct: ct);

        // Commit SONRASI; fırlatmaz (IBildirimSinyali sözleşmesi).
        _sinyal.Uyandir();
    }
}
