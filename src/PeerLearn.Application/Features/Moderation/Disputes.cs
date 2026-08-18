using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Economy;
using PeerLearn.Application.Options;
using PeerLearn.Application.Scheduling;
using PeerLearn.Domain.Moderation;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Application.Features.Moderation;

/// <summary>
/// İtiraz açma (Modül 4.3): "Ders yapılmadı" / "Sahte SS yüklendi". Ders Disputed'a
/// geçer, escrow DONAR (Active kalır) — çözüm yalnızca admin kararıyla.
/// </summary>
public sealed record OpenDisputeCommand(
    Guid SessionId,
    Guid StudentUserId,
    DisputeReason Reason,
    string Description) : IRequest<Guid>;

/// <remarks>
/// İtiraz açmak escrow'un KADERİNİ belirler (donar → sonra iade ya da transfer edilir),
/// bu yüzden krediyi değiştiren diğer yollarla AYNI korumaya sahip olmalıdır.
///
/// Kilit: <see cref="LockKeys.OrderedWalletKeys"/> ile ÖĞRENCİ + EĞİTMEN cüzdanları.
/// ApproveSession ve ResolveDispute de tam olarak bu iki anahtarı aynı sırada aldığı için
/// üçü karşılıklı dışlanır (sıralı alım deadlock'u önler). Süpürücünün iade fazı öğrenci
/// cüzdanını kilitlediğinden o da dışlanır.
///
/// Öncesinde bu yol kilitsiz/transaction'sız/retry'sizdi ve tek dayanağı LessonSession.xmin
/// idi: yarışı kaybeden kullanıcı, anlamlı bir alan hatası yerine ham CONCURRENCY_CONFLICT
/// alıyor ve itirazı sessizce kayboluyordu (eşzamanlılık testinde gözlendi).
/// </remarks>
public sealed class OpenDisputeHandler : IRequestHandler<OpenDisputeCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly IDistributedLockProvider _locks;
    private readonly EconomyOptions _economy;

    public OpenDisputeHandler(IAppDbContext db, IClock clock,
        IDistributedLockProvider locks, IOptions<EconomyOptions> economy)
    {
        _db = db;
        _clock = clock;
        _locks = locks;
        _economy = economy.Value;
    }

    public async Task<Guid> Handle(OpenDisputeCommand request, CancellationToken ct)
    {
        var description = request.Description?.Trim() ?? string.Empty;
        if (description.Length is < 10 or > 2000)
        {
            throw new AppException(ErrorCodes.DisputeAlreadyOpen, "Açıklama 10-2000 karakter olmalı.");
        }

        // Ön okuma: kilit anahtarlarını öğrenmek + kilit almadan hızlı reddetmek için.
        var preview = await _db.LessonSessions.AsNoTracking()
                          .SingleOrDefaultAsync(s => s.Id == request.SessionId, ct)
                      ?? throw new AppException(ErrorCodes.SessionNotFound, "Ders bulunamadı.", statusCode: 404);

        SessionRules.EnsureCanDispute(preview, request.StudentUserId, _clock.UtcNow);

        // Yalnızca eğitmenin cüzdanına yazılıyor (basım); öğrenci cüzdanına dokunan yol
        // kalmadığı için ikinci kilit ve deterministik sıralama gereksiz.
        var lockTimeout = TimeSpan.FromSeconds(_economy.LockTimeoutSeconds);
        var lockKeys = new List<string> { LockKeys.Wallet(preview.TutorUserId) };

        var handles = new List<IAsyncDisposable>(lockKeys.Count);
        try
        {
            foreach (var key in lockKeys)
            {
                handles.Add(await _locks.AcquireAsync(key, lockTimeout, ct));
            }

            return await ConcurrencyRetry.RunAsync(_db, async () =>
            {
                await using var tx = await _db.BeginTransactionAsync(cancellationToken: ct);

                // Kilit altında TAZE okuma: preview'dan sonra ders onaylanmış, iptal edilmiş
                // veya süpürücü tarafından düşürülmüş olabilir.
                var session = await _db.LessonSessions.SingleAsync(s => s.Id == request.SessionId, ct);
                SessionRules.EnsureCanDispute(session, request.StudentUserId, _clock.UtcNow);

                var openExists = await _db.Disputes.AnyAsync(d =>
                    d.SessionId == session.Id &&
                    (d.Status == DisputeStatus.Open || d.Status == DisputeStatus.UnderReview), ct);

                if (openExists)
                {
                    throw new AppException(ErrorCodes.DisputeAlreadyOpen,
                        "Bu ders için zaten açık bir itiraz var.", statusCode: 409);
                }

                /*
                  REDDEDİLEN İTİRAZ TEKRARLANAMAZ.

                  Reddedilen itiraz dersi AwaitingApproval'a döndürüyor ve 48 saatlik
                  otomatik onay sayacını SIFIRLIYOR. Tekrar sınırı olmadığı için öğrenci,
                  reddedilen itirazı hemen yeniden açarak bu sayacı sonsuza kadar
                  sıfırlayabiliyor ve eğitmenin kredisini süresiz bloke edebiliyordu —
                  hakem kararının hiçbir bağlayıcılığı kalmıyordu.

                  Kararın kendisine itiraz yolu ürün içinde YOK; hatalı bir ret durumunda
                  yönetimle iletişime geçilmesi gerekir (mesaj bunu söylüyor).
                */
                var dismissedBefore = await _db.Disputes.AnyAsync(d =>
                    d.SessionId == session.Id && d.Status == DisputeStatus.Dismissed, ct);

                if (dismissedBefore)
                {
                    throw new AppException(ErrorCodes.DisputeAlreadyOpen,
                        "Bu ders için açtığın itiraz daha önce geçersiz bulundu; yeniden itiraz " +
                        "açılamaz. Kararın hatalı olduğunu düşünüyorsan yönetime başvur.",
                        statusCode: 409);
                }

                var dispute = new Dispute
                {
                    SessionId = session.Id,
                    RaisedByUserId = request.StudentUserId,
                    Reason = request.Reason,
                    Description = description
                };
                _db.Disputes.Add(dispute);

                // Dispute satırı ile durum geçişi AYNI atomik sınırda: aksi halde Completed bir
                // ders üzerinde çözülemeyen "zombi" bir Open itiraz kalabilirdi.
                session.Status = SessionStatus.Disputed;

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return dispute.Id;
            }, ct: ct);
        }
        finally
        {
            // Kilitler alınış sırasının tersine bırakılır.
            for (var i = handles.Count - 1; i >= 0; i--)
            {
                await handles[i].DisposeAsync();
            }
        }
    }
}

public enum DisputeResolution
{
    /// <summary>Öğrenci haklı: escrow iade edilir, ders Cancelled olur.</summary>
    ForStudent = 0,

    /// <summary>Eğitmen haklı: escrow capture edilir (normal transfer), ders Completed olur.</summary>
    ForTutor = 1,

    /// <summary>İtiraz geçersiz: ders yeniden öğrenci onayına döner (48 saat sayacı sıfırlanır).</summary>
    Dismissed = 2
}

/// <summary>Admin kararı — üç sonuçtan biri. Ekonomik etkisi olan kollar cüzdan kilidi + transaction ister.</summary>
public sealed record ResolveDisputeCommand(
    Guid DisputeId,
    Guid AdminUserId,
    DisputeResolution Resolution,
    string? ResolutionNote) : IRequest;

public sealed class ResolveDisputeHandler : IRequestHandler<ResolveDisputeCommand>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly CreditLedgerService _ledger;
    private readonly IDistributedLockProvider _locks;
    private readonly EconomyOptions _economy;

    public ResolveDisputeHandler(IAppDbContext db, IClock clock, CreditLedgerService ledger,
        IDistributedLockProvider locks, IOptions<EconomyOptions> economy)
    {
        _db = db;
        _clock = clock;
        _ledger = ledger;
        _locks = locks;
        _economy = economy.Value;
    }

    public async Task Handle(ResolveDisputeCommand request, CancellationToken ct)
    {
        var preview = await (
                from d in _db.Disputes.AsNoTracking()
                join s in _db.LessonSessions.AsNoTracking() on d.SessionId equals s.Id
                where d.Id == request.DisputeId
                select new { d.Status, s.StudentUserId, s.TutorUserId })
            .SingleOrDefaultAsync(ct)
            ?? throw new AppException(ErrorCodes.DisputeNotFound, "İtiraz bulunamadı.", statusCode: 404);

        if (preview.Status is not (DisputeStatus.Open or DisputeStatus.UnderReview))
        {
            throw new AppException(ErrorCodes.DisputeNotFound, "İtiraz zaten çözülmüş.", statusCode: 409);
        }

        // Yalnızca eğitmenin cüzdanına yazılıyor (basım); öğrenci cüzdanına dokunan yol
        // kalmadığı için ikinci kilit ve deterministik sıralama gereksiz.
        var lockTimeout = TimeSpan.FromSeconds(_economy.LockTimeoutSeconds);
        var lockKeys = new List<string> { LockKeys.Wallet(preview.TutorUserId) };

        var handles = new List<IAsyncDisposable>(lockKeys.Count);
        try
        {
            foreach (var key in lockKeys)
            {
                handles.Add(await _locks.AcquireAsync(key, lockTimeout, ct));
            }

            await ConcurrencyRetry.RunAsync<object?>(_db, async () =>
            {
                await using var tx = await _db.BeginTransactionAsync(cancellationToken: ct);

                var dispute = await _db.Disputes.SingleAsync(d => d.Id == request.DisputeId, ct);
                var session = await _db.LessonSessions.SingleAsync(s => s.Id == dispute.SessionId, ct);

                if (dispute.Status is not (DisputeStatus.Open or DisputeStatus.UnderReview) ||
                    session.Status != SessionStatus.Disputed)
                {
                    throw new AppException(ErrorCodes.DisputeNotFound, "İtiraz zaten çözülmüş.", statusCode: 409);
                }

                var now = _clock.UtcNow;

                switch (request.Resolution)
                {
                    case DisputeResolution.ForStudent:
                    {
                        /*
                          İADE ADIMI YOK — ve gerek de yok.

                          İtiraz her zaman ONAY ÖNCESİ açılır (ders Disputed olur, basım
                          donar). Yani öğrenci lehine karar verildiğinde henüz basılmış bir
                          puan yoktur; geri alınacak bir şey de. Öğrenci zaten hiçbir şey
                          ödemediği için iade kavramı tümden ortadan kalktı.

                          Sonuç: eğitmen bu dersten puan KAZANMAZ. Yaptırım budur.
                        */
                        session.Status = SessionStatus.Cancelled;
                        session.CancelledAtUtc = now;
                        session.CancelReason = "İtiraz öğrenci lehine sonuçlandı.";
                        dispute.Status = DisputeStatus.ResolvedForStudent;
                        break;
                    }
                    case DisputeResolution.ForTutor:
                    {
                        // Basımın İKİNCİ yolu (birincisi normal onay). Unvan sayacı
                        // MintLessonRewardAsync'in içinde arttığı için burada ayrıca
                        // yapılacak bir şey yok — iki yol da kendiliğinden doğru davranır.
                        var tutorWallet = await _ledger.EnsureWalletAsync(session.TutorUserId, ct);
                        await _ledger.MintLessonRewardAsync(session, tutorWallet, ct);

                        session.Status = SessionStatus.Completed;
                        session.ApprovedAtUtc = now;
                        dispute.Status = DisputeStatus.ResolvedForTutor;
                        break;
                    }
                    case DisputeResolution.Dismissed:
                    {
                        if (session.CompletionRequestedAtUtc is null)
                        {
                            // İtiraz "ders hiç yapılmadı" (Booked) kökenli: eğitmen hiç tamamlama
                            // istemedi, kanıt yok. Onay kuyruğuna SOKULMAZ — Booked'a döner ve
                            // süpürücünün doğal Expired + escrow iadesi yolu işler. Aksi halde
                            // reddedilen itiraz, eğitmene hak etmediği otomatik capture kapısı açardı.
                            session.Status = SessionStatus.Booked;
                        }
                        else
                        {
                            session.Status = SessionStatus.AwaitingApproval;
                            session.CompletionRequestedAtUtc = now; // 48 saatlik otomatik onay sayacı yeniden başlar.
                        }

                        dispute.Status = DisputeStatus.Dismissed;
                        break;
                    }
                    default:
                        throw new AppException(ErrorCodes.DisputeNotFound, "Geçersiz karar.", statusCode: 400);
                }

                dispute.ResolvedByAdminId = request.AdminUserId;
                dispute.ResolutionNote = request.ResolutionNote?.Trim();
                dispute.ResolvedAtUtc = now;

                // Denetim izi AYNI transaction'da: karar commit olup iz kaybolamaz.
                var actorRole = await _db.Users.AsNoTracking()
                    .Where(u => u.Id == request.AdminUserId)
                    .Select(u => u.Role)
                    .SingleAsync(ct);

                AdminAudit.Record(
                    _db,
                    request.AdminUserId,
                    actorRole,
                    AdminActionType.DisputeResolved,
                    targetType: "Dispute",
                    targetId: dispute.Id,
                    summary: $"İtiraz {request.Resolution} olarak karara bağlandı ({session.CreditCost} kredi).",
                    metadata: new
                    {
                        sessionId = session.Id,
                        resolution = request.Resolution.ToString(),
                        creditCost = session.CreditCost,
                        studentUserId = session.StudentUserId,
                        tutorUserId = session.TutorUserId,
                        sessionStatusAfter = session.Status.ToString(),
                        note = dispute.ResolutionNote
                    });

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return null;
            }, ct: ct);
        }
        finally
        {
            for (var i = handles.Count - 1; i >= 0; i--)
            {
                await handles[i].DisposeAsync();
            }
        }
    }
}
