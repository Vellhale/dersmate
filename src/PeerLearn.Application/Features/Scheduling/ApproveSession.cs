using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Economy;
using PeerLearn.Application.Features.Community;
using PeerLearn.Application.Options;
using PeerLearn.Application.Scheduling;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Application.Features.Scheduling;

/// <summary>
/// ÇİFT TARAFLI ONAY + ATOMİK PUAN BASIMI (Modül 3.3 + 4.1).
///
/// Öğrenci onay verdiği an eğitmene süreye göre puan BASILIR (30 dk = 50, 60 dk = 100),
/// vadesiz lot açılır, unvan sayacı artar ve ders Completed olur — hepsi tek ReadCommitted
/// transaction içinde. ÖĞRENCİDEN HİÇBİR ŞEY DÜŞMEZ.
///
/// Savunma katmanları:
///   1. Distributed lock — artık TEK cüzdan (eğitmenin). İki cüzdanı deterministik sırada
///      kilitleyip deadlock'tan kaçınma ihtiyacı, transferin tek bacağa inmesiyle bitti.
///   2. xmin optimistic concurrency + retry (Wallet, LessonSession)
///   3. DB unique index'leri: ders başına tek kazanç lotu (CreditLots.SourceSessionId) ve
///      tek kazanç hareketi (RelatedSessionId, Type). Bunlar escrow'dan kalma değil, YENİ
///      modelin çifte basıma karşı tek korumasıdır — kaldırılmamalı.
/// </summary>
/// <remarks>
/// ESKİ MODEL: bu akış bir escrow tahsilatıydı — bloke kredi öğrenciden çıkar, eğitmene
/// 30 gün vadeli lot olarak geçerdi ve iki bacak aynı CorrelationId ile toplam 0 ederdi.
/// Bugün tek bacak var, karşılığı olmayan bir basım; vade de yok (kazanılan puan yanmaz).
/// </remarks>
/// <param name="AsSystem">
/// true → 48 saatlik otomatik onay job'ı çağırıyor demektir; öğrenci kimliği doğrulanmaz.
/// API controller'ı bu bayrağı ASLA istemciden almaz.
/// </param>
public sealed record ApproveSessionCommand(Guid SessionId, Guid? ApproverUserId, bool AsSystem = false)
    : IRequest<ApproveSessionResult>;

/// <param name="CreditsMinted">
/// Eğitmene basılan puan (gönüllü derste 0). "Transferred" değil "Minted": öğrenciden
/// alınıp eğitmene verilen bir şey yok, puan bu anda üretiliyor.
/// </param>
public sealed record ApproveSessionResult(Guid SessionId, int CreditsMinted, DateTime ApprovedAtUtc);

public sealed class ApproveSessionHandler : IRequestHandler<ApproveSessionCommand, ApproveSessionResult>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly CreditLedgerService _ledger;
    private readonly IDistributedLockProvider _locks;
    private readonly EconomyOptions _economy;
    private readonly SubjectBadgeEngine _subjectBadges;

    public ApproveSessionHandler(IAppDbContext db, IClock clock, CreditLedgerService ledger,
        IDistributedLockProvider locks, IOptions<EconomyOptions> economy,
        SubjectBadgeEngine subjectBadges)
    {
        _db = db;
        _clock = clock;
        _ledger = ledger;
        _locks = locks;
        _economy = economy.Value;
        _subjectBadges = subjectBadges;
    }

    public async Task<ApproveSessionResult> Handle(ApproveSessionCommand request, CancellationToken ct)
    {
        // Otomatik onay eşiği: sistem yalnızca 48 saati gerçekten dolmuş dersleri onaylayabilir.
        var systemDeadline = request.AsSystem
            ? _clock.UtcNow.AddHours(-_economy.AutoApproveHours)
            : (DateTime?)null;

        // Ön okuma (kilitsiz): katılımcıları öğrenip kilit anahtarlarını sıralayabilmek için.
        var preview = await _db.LessonSessions.AsNoTracking()
                          .SingleOrDefaultAsync(s => s.Id == request.SessionId, ct)
                      ?? throw new AppException(ErrorCodes.SessionNotFound, "Ders bulunamadı.", statusCode: 404);

        SessionRules.EnsureCanApprove(preview, request.ApproverUserId, request.AsSystem, systemDeadline);

        /*
          TEK CÜZDAN KİLİDİ YETER.

          Eskiden iki cüzdan (öğrenci + eğitmen) deterministik sırada kilitleniyordu; sıra
          deadlock önlemi içindi. Artık yalnızca eğitmenin cüzdanına yazılıyor, dolayısıyla
          ikinci kilit ve sıralama kuralı gereksiz — ve gereksiz kilit, ölçekte gereksiz
          bekleme demek.
        */
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

                // Kilit altında taze okuma: preview'dan sonra durum değişmiş olabilir.
                var session = await _db.LessonSessions.SingleAsync(s => s.Id == request.SessionId, ct);
                SessionRules.EnsureCanApprove(session, request.ApproverUserId, request.AsSystem, systemDeadline);

                // ÖĞRENCİ CÜZDANINA DOKUNULMAZ: ders almak ücretsiz, düşecek bakiye yok.
                var tutorWallet = await _ledger.EnsureWalletAsync(session.TutorUserId, ct);

                var minted = await _ledger.MintLessonRewardAsync(session, tutorWallet, ct);

                var now = _clock.UtcNow;
                session.Status = SessionStatus.Completed;
                session.ApprovedAtUtc = now;

                // Deneyim sayaçları: gönüllü dersler DE sayılır — modülün amacı kredi
                // kazandırmadan deneyim biriktirmek. Aynı transaction'da artırılır ki
                // "ders tamamlandı ama sayaç artmadı" durumu oluşamasın.
                var tutor = await _db.Users.SingleAsync(u => u.Id == session.TutorUserId, ct);
                tutor.TaughtSessionCount += 1;
                tutor.TaughtMinutes += session.DurationMinutes;

                await _db.SaveChangesAsync(ct);

                /*
                  BRANŞ ROZETLERİ — SIRA KRİTİK.

                  Motor süreyi DB'den okuyor, bu yüzden yukarıdaki SaveChanges'ten SONRA
                  çağrılmalı: bu ders henüz Completed olarak yazılmamış olsaydı süresi
                  hesaba girmez, eşiği tam bu derste dolduran eğitmen rozetini bir ders
                  geç alırdı. Desen: SaveChanges → Evaluate → SaveChanges → Commit.
                  (Aynı tuzağa proje boyunca dört kez düşüldü; bkz. docs/DEVAM-EDILECEK.md.)

                  AYNI TRANSACTION İÇİNDE: "ders tamamlandı ama rozet yazılmadı" gibi bir
                  ara durum oluşamaz. Rozet hesabı patlarsa onay da geri alınır — rozet,
                  sessizce atlanacak bir süs değil, onayın parçası.
                */
                await _subjectBadges.EvaluateAsync(session.TutorUserId, ct);
                await _db.SaveChangesAsync(ct);

                await tx.CommitAsync(ct);

                return new ApproveSessionResult(session.Id, minted, now);
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
