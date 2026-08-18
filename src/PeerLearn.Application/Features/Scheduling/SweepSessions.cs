using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Options;
using PeerLearn.Domain.Identity;
using PeerLearn.Domain.Matchmaking;
using PeerLearn.Domain.Moderation;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Application.Features.Scheduling;

/// <summary>
/// Background job süpürmesi:
///  1. OTOMATİK ONAY: öğrenci 48 saat içinde onay/itiraz vermediyse ders sistem adına
///     onaylanır (eğitmenin emeği öğrencinin sessizliğine kurban gitmesin). Basımı
///     yapan tek yol burada da ApproveSessionCommand'dir.
///  2. DÜŞMÜŞ REZERVASYON: ders saati geçtiği halde eğitmen hiç tamamlama istemediyse
///     rezervasyon Expired yapılır. PARA/PUAN HAREKETİ YOKTUR (aşağıya bakınız).
///  3. YANITSIZ EŞLEŞME İSTEĞİ: muhatabın hiç dokunmadığı istekler süresi dolmuş sayılır.
///  4. SÜRESİ DOLAN ASKI: geçici yaptırımı biten kullanıcıların durumu gerçeğe döner.
///  5. GECİKMİŞ İTİRAZ UYARISI: karara bağlanmayan itirazlar log'a düşer.
/// (Status, ScheduledEndUtc) index'i bu taramalar içindir.
/// </summary>
/// <remarks>
/// BU SINIF ARTIK DEFTERE DOKUNMUYOR.
///
/// Escrow modelinde 2. faz bir İADE fazıydı: düşen rezervasyonda öğrencinin bloke kredisi
/// çözülür, lotuna geri yazılırdı. Bu yüzden sınıf hem <c>CreditLedgerService</c> hem de
/// cüzdan kilidi alırdı. Ders almak ücretsizleşince iade edilecek bir şey kalmadı; iki
/// bağımlılık da kaldırıldı (kullanılmadıkları hâlde her süpürme turunda kuruluyorlardı
/// ve okuyucuya "süpürücü krediye dokunuyor" izlenimi veriyorlardı).
///
/// Bugün süpürücünün puan üzerindeki TEK etkisi dolaylıdır: 1. fazdaki otomatik onay,
/// ApproveSessionCommand üzerinden eğitmene basım yaptırır. Düşürme (2. faz) ve diğer
/// fazlar hiçbir ekonomik kayıt üretmez.
/// </remarks>
public sealed record SweepSessionsCommand : IRequest<SweepSessionsResult>;

public sealed record SweepSessionsResult(int AutoApproved, int Expired, int ExpiredMatches);

public sealed class SweepSessionsHandler : IRequestHandler<SweepSessionsCommand, SweepSessionsResult>
{
    private const int BatchSize = 100;

    /// <summary>Bu süreyi aşan açık itiraz operasyonel bir sorundur; log'a uyarı düşülür.</summary>
    private const int DisputeWarnAfterDays = 3;

    /// <summary>
    /// Muhatabın hiç yanıt vermediği isteğin ömrü. Süresi dolan istek Pending olmaktan
    /// çıkar; bu, gönderenin AYNI kişiye AYNI konu için yeniden istek atabilmesini sağlar
    /// (mükerrer istek engeli yalnızca Pending kayıtlara bakıyor). Süresiz bekleyen bir
    /// istek, gönderen açısından kalıcı bir kilittir.
    /// </summary>
    private const int MatchRequestExpireDays = 14;

    // ---- Geri çekilme (backoff) ayarları ----

    /// <summary>İlk başarısızlıktan sonraki bekleme. Bir süpürme turuna eşit.</summary>
    private static readonly TimeSpan BackoffBase = TimeSpan.FromMinutes(10);

    /// <summary>Üst sınır: en umutsuz kayıt bile günde bir denenmeye devam eder.</summary>
    private static readonly TimeSpan BackoffMax = TimeSpan.FromHours(24);

    /// <summary>Bu sayıya ulaşan kayıt artık geçici bir aksaklık değil; hata olarak loglanır.</summary>
    private const int KaliciHataEsigi = 5;

    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly IMediator _mediator;
    private readonly EconomyOptions _economy;
    private readonly ILogger<SweepSessionsHandler> _logger;

    public SweepSessionsHandler(IAppDbContext db, IClock clock, IMediator mediator,
        IOptions<EconomyOptions> economy, ILogger<SweepSessionsHandler> logger)
    {
        _db = db;
        _clock = clock;
        _mediator = mediator;
        _economy = economy.Value;
        _logger = logger;
    }

    public async Task<SweepSessionsResult> Handle(SweepSessionsCommand request, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var autoApproved = 0;
        var expired = 0;

        // 1) Otomatik onay — atomik transferin TEK yolu olan ApproveSessionCommand yeniden kullanılır.
        var approvalDeadline = now.AddHours(-_economy.AutoApproveHours);

        /*
          GERİ ÇEKİLME SORGUYA GÖMÜLÜ.

          Sol birleşim (LEFT JOIN) ile her aday kaydın süpürme-hatası satırına bakılır ve
          bekleme süresi dolmamış olanlar sonuç kümesine HİÇ girmez. Bunu bellekte
          filtrelemek yetmezdi: parti boyutu sabit (Take), yani takılı kayıtlar SQL'den
          gelmeye devam etseydi partinin yerini yine onlar doldururdu — asıl sorun olan
          sıra tıkanması aynen sürerdi.
        */
        var toApprove = await (
                from s in _db.LessonSessions.AsNoTracking()
                where s.Status == SessionStatus.AwaitingApproval &&
                      s.CompletionRequestedAtUtc != null &&
                      s.CompletionRequestedAtUtc <= approvalDeadline
                from f in _db.SweepFailures.AsNoTracking()
                    .Where(f => f.RecordType == SweepRecordType.SessionAutoApprove && f.RecordId == s.Id)
                    .DefaultIfEmpty()
                where f == null || f.NextAttemptAtUtc <= now
                orderby s.CompletionRequestedAtUtc
                select new { s.Id, HataVar = f != null })
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var item in toApprove)
        {
            try
            {
                await _mediator.Send(new ApproveSessionCommand(item.Id, ApproverUserId: null, AsSystem: true), ct);
                autoApproved++;

                if (item.HataVar)
                {
                    await HataKaydiniSilAsync(SweepRecordType.SessionAutoApprove, item.Id, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (AppException ex)
            {
                /*
                  Yarış: bu arada öğrenci onayladı/itiraz etti — beklenen bir durum.

                  YİNE DE GERİ ÇEKİLME KAYDEDİLİR. Sebebi ince: gerçek bir yarışta kaydın
                  durumu değiştiği için sorgudan kendiliğinden düşer ve satırın hiçbir etkisi
                  olmaz. Ama AppException tekrar tekrar aynı kayıtta atılıyorsa (durumu
                  değişmeden) o kayıt kalıcı olarak takılmıştır ve tam da sıra tıkayan
                  şeydir. "Beklenen hata" saymak, o durumu görünmez kılardı.
                */
                _logger.LogInformation("Otomatik onay atlandı ({SessionId}): {Code}", item.Id, ex.Code);
                _db.ClearChangeTracker();
                await HataKaydetAsync(SweepRecordType.SessionAutoApprove, item.Id, ex.Code, ct);
            }
            catch (Exception ex)
            {
                // KRİTİK: dar catch tek zehirli kayıtta TÜM süpürmeyi süresiz bloke ederdi —
                // sonraki fazlar (düşürme, eşleşme süresi, askı bitişi) hiç çalışamazdı.
                // Kayıt atlanır, tracker temizlenir, süpürme devam eder.
                _logger.LogError(ex, "Otomatik onay başarısız, kayıt atlandı ({SessionId}).", item.Id);
                _db.ClearChangeTracker();
                await HataKaydetAsync(SweepRecordType.SessionAutoApprove, item.Id, ex.Message, ct);
            }
        }

        // 2) Süresi geçmiş, hiç tamamlama istenmemiş rezervasyonlar.
        var expireDeadline = now.AddDays(-_economy.BookingExpireGraceDays);
        var toExpire = await (
                from s in _db.LessonSessions.AsNoTracking()
                where s.Status == SessionStatus.Booked && s.ScheduledEndUtc <= expireDeadline
                from f in _db.SweepFailures.AsNoTracking()
                    .Where(f => f.RecordType == SweepRecordType.SessionExpire && f.RecordId == s.Id)
                    .DefaultIfEmpty()
                where f == null || f.NextAttemptAtUtc <= now
                orderby s.ScheduledEndUtc
                select new { s.Id, s.StudentUserId, HataVar = f != null })
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var item in toExpire)
        {
            try
            {
                /*
                  SADECE DURUM DEĞİŞİYOR. Öğrenci ödemediği için düşürülen rezervasyonda
                  geri verilecek bir şey yok; eğitmene de basım yapılmaz (ders yapılmadı).
                  Cüzdan kilidi ve defter çağrısı bu yüzden kaldırıldı.

                  Transaction yine de açılıyor: durum kontrolü ile yazma arasındaki yarışta
                  (öğrenci aynı anda iptal ediyor olabilir) tek satırlık da olsa okuma-yazma
                  atomik kalmalı; xmin çakışmasında ConcurrencyRetry baştan dener.
                */
                await ConcurrencyRetry.RunAsync<object?>(_db, async () =>
                {
                    await using var tx = await _db.BeginTransactionAsync(cancellationToken: ct);

                    var session = await _db.LessonSessions.SingleAsync(s => s.Id == item.Id, ct);
                    if (session.Status != SessionStatus.Booked)
                    {
                        return null; // Yarış: durum değişmiş.
                    }

                    session.Status = SessionStatus.Expired;

                    await _db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    return null;
                }, ct: ct);

                expired++;

                if (item.HataVar)
                {
                    await HataKaydiniSilAsync(SweepRecordType.SessionExpire, item.Id, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (AppException ex)
            {
                _logger.LogWarning("Rezervasyon düşürme atlandı ({SessionId}): {Code}", item.Id, ex.Code);
                _db.ClearChangeTracker();
                await HataKaydetAsync(SweepRecordType.SessionExpire, item.Id, ex.Code, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rezervasyon düşürme başarısız, kayıt atlandı ({SessionId}).", item.Id);
                _db.ClearChangeTracker();
                await HataKaydetAsync(SweepRecordType.SessionExpire, item.Id, ex.Message, ct);
            }
        }

        /*
          3) YANITSIZ EŞLEŞME İSTEKLERİ.

          Geri çekilme YOK ve gerekmiyor: bu faz tek bir durum güncellemesi, dış bağımlılığı
          (kilit, başka komut) olmayan tek fazdır. Kayıt bazında kalıcı olarak başarısız
          olabileceği bir yol yok; SaveChanges düşerse tüm parti düşer ve sonraki turda
          aynı parti yeniden denenir.

          RespondedAtUtc'ye DOKUNULMAZ: süre dolumu bir yanıt DEĞİLDİR, yanıtın hiç
          gelmemesidir. Bu alanın anlamı "muhatap ne zaman karar verdi" — süre dolumunda
          damga atmak, hiç dokunulmamış bir isteği yanıtlanmış gibi gösterirdi.

          (Bu kuralın ilk gerekçesi ⚡ hızlı yanıt rozetinin ortancasıydı; o rozet emekli
          edildi ama kural DEĞİŞMEDİ, çünkü dayanağı rozet değil alanın kendi anlamı.
          Gelen kutusu, panel ve "yanıt bekleyen istekler" sayımı da aynı ayrıma güveniyor.
          e2e-social/H bölümü iki yönü de sınıyor.)
        */
        var matchDeadline = now.AddDays(-MatchRequestExpireDays);
        var expiredMatches = await _db.Matches
            .Where(m => m.Status == MatchStatus.Pending && m.CreatedAtUtc <= matchDeadline)
            .OrderBy(m => m.CreatedAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var match in expiredMatches)
        {
            match.Status = MatchStatus.Expired;
        }

        if (expiredMatches.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("{Sayi} eşleşme isteğinin süresi doldu ({Gun} gün yanıtsız).",
                expiredMatches.Count, MatchRequestExpireDays);
        }

        // 4) Süresi dolan geçici askılar. Erişim kontrolü tarihi zaten kendisi kontrol
        //    ediyor (kullanıcı bu iş koşmadan da girebilir); burada yapılan, DURUMU
        //    gerçeğe döndürmek — aksi halde ceza bitmiş kullanıcılar kalıcı olarak
        //    "Suspended" görünür ve yönetim ekranları yanlış tablo gösterirdi.
        var suresiDolan = await _db.Users
            .Where(u => u.Status == UserStatus.Suspended &&
                        u.SuspendedUntilUtc != null &&
                        u.SuspendedUntilUtc <= now)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var user in suresiDolan)
        {
            user.Status = user.EmailVerifiedAtUtc is null
                ? UserStatus.PendingVerification
                : UserStatus.Active;
            user.SuspendedUntilUtc = null;
        }

        if (suresiDolan.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("{Sayi} kullanıcının geçici askısı sona erdi.", suresiDolan.Count);
        }

        /*
          5) İTİRAZLI DERSLERİN YAŞI. Disputed durumu hiçbir süpürücünün kapsamında değil
             ve olmamalı da — basımın yapılıp yapılmayacağını insan kararı belirlemeli,
             otomatik bir iş değil.

             BEKLEMENİN BEDELİ DEĞİŞTİ AMA SIFIRLANMADI. Escrow modelinde karar gecikince
             öğrencinin kredisi süresiz bloke kalırdı; artık kimsenin bloke parası yok.
             Bekleyen taraf şimdi EĞİTMEN: hak ettiği puan karara bağlanana kadar
             basılmıyor ve unvanı olduğu yerde duruyor. Yani üst sınırın olmaması hâlâ
             gerçek bir maliyet, yalnızca yüzü değişti.

             Burada karar verilmiyor, yalnızca GÖRÜNÜR kılınıyor: eşiği aşan itiraz varsa
             log'a uyarı düşer (panelde de metrik olarak gösteriliyor).
        */
        var bekleyenEsik = now.AddDays(-DisputeWarnAfterDays);
        var gecikmisItiraz = await _db.Disputes.AsNoTracking()
            .CountAsync(d => (d.Status == DisputeStatus.Open || d.Status == DisputeStatus.UnderReview)
                             && d.CreatedAtUtc <= bekleyenEsik, ct);

        if (gecikmisItiraz > 0)
        {
            _logger.LogWarning(
                "{Sayi} itiraz {Gun} günden uzun süredir karara bağlanmadı — bu derslerde eğitmenin puanı basılmayı bekliyor.",
                gecikmisItiraz, DisputeWarnAfterDays);
        }

        return new SweepSessionsResult(autoApproved, expired, expiredMatches.Count);
    }

    /// <summary>
    /// Başarısızlığı kaydeder ve kaydın bir sonraki denemesini üstel olarak erteler.
    /// </summary>
    private async Task HataKaydetAsync(SweepRecordType tur, Guid kayitId, string hata, CancellationToken ct)
    {
        try
        {
            var now = _clock.UtcNow;

            var kayit = await _db.SweepFailures
                .SingleOrDefaultAsync(f => f.RecordType == tur && f.RecordId == kayitId, ct);

            if (kayit is null)
            {
                kayit = new SweepFailure { RecordType = tur, RecordId = kayitId };
                _db.SweepFailures.Add(kayit);
            }

            kayit.FailureCount++;
            kayit.LastFailedAtUtc = now;
            kayit.LastError = hata.Length > 500 ? hata[..500] : hata;
            kayit.NextAttemptAtUtc = now + Gecikme(kayit.FailureCount);

            await _db.SaveChangesAsync(ct);

            if (kayit.FailureCount >= KaliciHataEsigi)
            {
                _logger.LogError(
                    "Süpürme kaydı KALICI olarak takılı: {Tur}/{KayitId}, {Sayi}. başarısızlık, " +
                    "sonraki deneme {Sonraki:u}. Son hata: {Hata}",
                    tur, kayitId, kayit.FailureCount, kayit.NextAttemptAtUtc, kayit.LastError);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            /*
              Geri çekilme kaydının kendisi düşerse süpürme DURMAZ. İki instance aynı anda
              aynı kayda satır eklemeye çalışırsa (benzersiz index) burası tetiklenir; sonuç
              yalnızca "bu tur ertelenemedi", bir sonraki tur yeniden dener.
            */
            _logger.LogWarning(ex, "Süpürme hata kaydı yazılamadı ({Tur}/{KayitId}).", tur, kayitId);
            _db.ClearChangeTracker();
        }
    }

    /// <summary>Kayıt sonunda başarılı oldu: geri çekilme geçmişi silinir.</summary>
    private async Task HataKaydiniSilAsync(SweepRecordType tur, Guid kayitId, CancellationToken ct)
    {
        try
        {
            await _db.SweepFailures
                .Where(f => f.RecordType == tur && f.RecordId == kayitId)
                .ExecuteDeleteAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Silinemezse en fazla bir tur daha ertelenir; sonraki başarıda yine denenir.
            _logger.LogWarning(ex, "Süpürme hata kaydı silinemedi ({Tur}/{KayitId}).", tur, kayitId);
        }
    }

    /// <summary>
    /// Üstel geri çekilme: 10 dk, 20, 40, 80… en fazla 24 saat.
    /// Üs 12'de kırpılır — <c>2^count</c> sınırsız büyüseydi tick çarpımı taşardı ve
    /// taşma NEGATİF bir gecikme üretip kaydı hemen yeniden denenir hâle getirirdi.
    /// </summary>
    private static TimeSpan Gecikme(int failureCount)
    {
        var us = Math.Min(Math.Max(failureCount - 1, 0), 12);
        var ticks = BackoffBase.Ticks * (1L << us);

        return TimeSpan.FromTicks(Math.Min(ticks, BackoffMax.Ticks));
    }
}
