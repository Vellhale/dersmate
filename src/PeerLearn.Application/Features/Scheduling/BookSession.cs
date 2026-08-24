using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Economy;
using PeerLearn.Application.Options;
using PeerLearn.Application.Scheduling;
using PeerLearn.Domain.Matchmaking;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Application.Features.Scheduling;

/// <summary>
/// Oturum rezervasyonu (Modül 2.3): takvime işlenir ve benzersiz doğrulama kodu (Session ID)
/// üretilir. ÖĞRENCİDEN HİÇBİR ŞEY DÜŞMEZ — ders almak ücretsizdir.
/// </summary>
public sealed record BookSessionCommand(
    Guid StudentUserId,
    Guid MatchId,
    Guid TopicId,
    DateTime ScheduledStartUtc,
    int DurationMinutes) : IRequest<BookSessionResult>;

public sealed record BookSessionResult(
    Guid SessionId,
    string VerificationCode,

    /// <summary>
    /// Ders onaylandığında EĞİTMENE basılacak puan. Öğrencinin ödeyeceği tutar DEĞİLDİR;
    /// alan adı bunu açıkça söylesin diye "CreditCost" değil "MintAmount".
    /// </summary>
    int MintAmount,

    /// <summary>Gönüllü ders: eğitmen puan kazanmaz.</summary>
    bool IsVolunteer,

    DateTime ScheduledStartUtc,
    DateTime ScheduledEndUtc);

public sealed class BookSessionHandler : IRequestHandler<BookSessionCommand, BookSessionResult>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly MintGuard _mintGuard;
    private readonly IDistributedLockProvider _locks;
    private readonly EconomyOptions _economy;

    public BookSessionHandler(IAppDbContext db, IClock clock, MintGuard mintGuard,
        IDistributedLockProvider locks, IOptions<EconomyOptions> economy)
    {
        _db = db;
        _clock = clock;
        _mintGuard = mintGuard;
        _locks = locks;
        _economy = economy.Value;
    }

    public async Task<BookSessionResult> Handle(BookSessionCommand request, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        SessionRules.EnsureCanBook(now, request.ScheduledStartUtc, request.DurationMinutes);

        var match = await _db.Matches.AsNoTracking()
                        .SingleOrDefaultAsync(m => m.Id == request.MatchId, ct)
                    ?? throw new AppException(ErrorCodes.MatchNotFound, "Eşleşme bulunamadı.", statusCode: 404);

        if (match.Status != MatchStatus.Accepted)
        {
            throw new AppException(ErrorCodes.MatchNotAccepted,
                "Ders yalnızca kabul edilmiş eşleşme üzerinden rezerve edilebilir.", statusCode: 409);
        }

        if (match.InitiatorUserId != request.StudentUserId && match.ResponderUserId != request.StudentUserId)
        {
            throw new AppException(ErrorCodes.NotMatchParticipant, "Bu eşleşmenin tarafı değilsiniz.", statusCode: 403);
        }

        /*
          ÜNİVERSİTE AĞI EŞLEŞMESİNDEN DERS REZERVE EDİLEMEZ — AÇIK MUHAFIZ.

          Konusuz eşleşmede iki konu alanı da null; aşağıdaki kapsam kontrolü bunu
          zaten engelliyordu ama TESADÜFEN: C#'ta `Guid != Guid?` karşılaştırması
          null tarafta false döndüğü için koşul sağlanıyor ve "Konu bu eşleşmenin
          kapsamında değil" hatası veriliyordu. Doğru sonuç, yanlış gerekçe — ve
          kullanıcıya anlamsız bir mesaj.

          Ayrı bir muhafız hem doğru mesajı veriyor hem de niyeti kayda geçiriyor:
          üniversite ağı eşleşmesi sohbet içindir, ders akışına girmez. Kapsam kontrolü
          bir gün değişirse bu kural onunla birlikte sessizce kaybolmaz.
        */
        if (match.RequestedTopicId is null && match.OfferedTopicId is null)
        {
            throw new AppException(ErrorCodes.InvalidBooking,
                "Bu eşleşme üniversite ağı üzerinden kuruldu; ders rezervasyonu içermiyor.",
                statusCode: 409);
        }

        if (request.TopicId != match.RequestedTopicId && request.TopicId != match.OfferedTopicId)
        {
            throw new AppException(ErrorCodes.InvalidBooking, "Konu bu eşleşmenin kapsamında değil.");
        }

        // Rezervasyonu yapan taraf ÖĞRENCİDİR; eğitmen karşı taraftır.
        var tutorUserId = match.InitiatorUserId == request.StudentUserId
            ? match.ResponderUserId
            : match.InitiatorUserId;

        // İstemci "...Z" (Utc), "+03:00" (Local'a dönüşür) veya offset'siz gönderebilir.
        // Kind körlemesine Utc'ye çevrilirse offset'li istek saatler kayar; her üç durum da doğru ele alınır.
        var start = request.ScheduledStartUtc.Kind switch
        {
            DateTimeKind.Utc => request.ScheduledStartUtc,
            DateTimeKind.Local => request.ScheduledStartUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(request.ScheduledStartUtc, DateTimeKind.Utc),
        };
        var end = start.AddMinutes(request.DurationMinutes);

        /*
          GÖNÜLLÜLÜK VE ÖDÜL.

          Eğitmenin o konudaki AKTİF ilanı gönüllüyse ders puan üretmez. Karar rezervasyon
          anında LessonSession'a KOPYALANIR: eğitmen ilanını sonradan değiştirse bile
          açılmış dersin koşulları değişmez — emeği verilmiş bir dersin karşılığı sonradan
          sıfırlanamamalı, tersi de olmamalı.

          İlan bulunamazsa gönüllü SAYILMAZ (ödül basılır). Eskiden bu varsayımın gerekçesi
          "ekonomiyi sızdırmamak"tı; artık gerekçe eğitmenin emeğinin kayıt eksikliği
          yüzünden karşılıksız kalmaması.
        */
        var volunteerOffer = await _db.PortfolioEntries.AsNoTracking()
            .AnyAsync(p => p.UserId == tutorUserId
                           && p.TopicId == request.TopicId
                           && p.Direction == PortfolioDirection.Offer
                           && p.IsActive
                           && p.IsVolunteer, ct);

        var mintAmount = volunteerOffer ? 0 : SessionRules.CalculateMintAmount(request.DurationMinutes);

        /*
          EĞİTMEN KİLİDİ — suistimal freninin işe yaraması için ŞART.

          Cüzdan kilidi kalktı (öğrenciden düşen bakiye yok) ama yerine başka bir
          serileştirme ihtiyacı doğdu: MintGuard "bu eğitmen bugün kaç ders açtı" diye
          SAYIYOR, ve sayma ile yazma arasındaki boşluk kilitsiz bırakılırsa eşzamanlı
          on iki istek on ikisi de sayacı 0 görüp geçer. Tavan, tam olarak onu aşmak
          isteyen kişinin en kolay ulaşacağı yolla — paralel istekle — devre dışı kalır.

          Anahtar ÇİFT değil EĞİTMEN bazında. Çift anahtarıyla yazıldığında sahte öğrenci
          ordusu freni tamamen atlıyordu: her öğrenci farklı kilide düşüyor, kimse kimseyi
          beklemiyordu (ölçüm: tools/e2e-mintguard.ps1). Gerekçenin tamamı LockKeys.Tutor'da.

          Bu, iki farklı öğrenciyle aynı anda ders açmayı serileştirir ama ENGELLEMEZ —
          tavan dolmadığı sürece ikisi de kabul edilir, yalnızca sıraya girerler.
        */
        var tutorKey = LockKeys.Tutor(tutorUserId);
        await using var tutorLock = await _locks.AcquireAsync(
            tutorKey, TimeSpan.FromSeconds(_economy.LockTimeoutSeconds), ct);

        await _mintGuard.EnsureCanBookAsync(tutorUserId, request.StudentUserId, start, ct);

        return await ConcurrencyRetry.RunAsync(_db, async () =>
        {
            await using var tx = await _db.BeginTransactionAsync(cancellationToken: ct);

            // Çakışma kontrolü (double booking): iki tarafın da aktif dersleriyle kesişme yasak.
            // Şema seviyesindeki EXCLUDE constraint bilinçli AŞAMA-sonrası iş (docs §6).
            var conflict = await _db.LessonSessions.AnyAsync(s =>
                (s.Status == SessionStatus.Booked || s.Status == SessionStatus.AwaitingApproval) &&
                (s.TutorUserId == tutorUserId || s.StudentUserId == tutorUserId ||
                 s.TutorUserId == request.StudentUserId || s.StudentUserId == request.StudentUserId) &&
                s.ScheduledStartUtc < end && start < s.ScheduledEndUtc, ct);

            if (conflict)
            {
                throw new AppException(ErrorCodes.ScheduleConflict,
                    "Seçilen saat aralığı taraflardan birinin başka dersiyle çakışıyor.", statusCode: 409);
            }

            var session = new LessonSession
            {
                MatchId = match.Id,
                TutorUserId = tutorUserId,
                StudentUserId = request.StudentUserId,
                TopicId = request.TopicId,
                ScheduledStartUtc = start,
                DurationMinutes = request.DurationMinutes,
                ScheduledEndUtc = end,
                CreditCost = mintAmount,
                IsVolunteer = volunteerOffer,
                VerificationCode = await NewUniqueCodeAsync(ct)
            };
            _db.LessonSessions.Add(session);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new BookSessionResult(
                session.Id, session.VerificationCode, mintAmount, volunteerOffer, start, end);
        }, ct: ct);
    }

    private async Task<string> NewUniqueCodeAsync(CancellationToken ct)
    {
        // Pratikte ilk denemede benzersizdir (32^8 uzay); unique index son savunmadır.
        for (var i = 0; i < 5; i++)
        {
            var code = CodeGenerator.NewVerificationCode();
            if (!await _db.LessonSessions.AnyAsync(s => s.VerificationCode == code, ct))
            {
                return code;
            }
        }

        throw new AppException(ErrorCodes.InvalidBooking, "Doğrulama kodu üretilemedi, tekrar deneyin.", 500);
    }
}
