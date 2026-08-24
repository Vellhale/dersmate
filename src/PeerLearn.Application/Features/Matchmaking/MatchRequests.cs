using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Features.Community;
using PeerLearn.Domain.Communication;
using PeerLearn.Domain.Identity;
using PeerLearn.Domain.Matchmaking;

namespace PeerLearn.Application.Features.Matchmaking;

/// <summary>
/// Eşleşme isteği: RequestedTopicId = karşı taraftan almak istediğim konu;
/// OfferedTopicId (opsiyonel) = karşılığında anlatmayı önerdiğim konu (çapraz teklif).
///
/// RequestedTopicId NULL ise bu bir ÜNİVERSİTE AĞI isteğidir: ders değil, tanışma.
/// İki türün doğrulama kapıları farklı — aşağıdaki handler'da gerekçesiyle birlikte.
/// </summary>
public sealed record CreateMatchRequestCommand(
    Guid InitiatorUserId,
    Guid ResponderUserId,
    Guid? RequestedTopicId,
    Guid? OfferedTopicId) : IRequest<Guid>;

public sealed class CreateMatchRequestHandler : IRequestHandler<CreateMatchRequestCommand, Guid>
{
    private readonly IAppDbContext _db;

    public CreateMatchRequestHandler(IAppDbContext db) => _db = db;

    public async Task<Guid> Handle(CreateMatchRequestCommand request, CancellationToken ct)
    {
        if (request.InitiatorUserId == request.ResponderUserId)
        {
            throw new AppException(ErrorCodes.SelfMatch, "Kendinizle eşleşemezsiniz.");
        }

        /*
          İKİ TÜR, İKİ AYRI KAPI — ve ikisi de "rastgele kişiye istek" spam'ini kesmek için.

          DERS isteğinde kapı: karşı taraf o konuyu gerçekten sunuyor mu. Yani istek,
          karşı tarafın kendi ilan ettiği bir şeye dayanıyor.

          ÜNİVERSİTE AĞI isteğinde konu yok, dolayısıyla o kapı çalışamaz. Yerine konan
          kapı aynı mantığı taşıyor: karşı taraf üniversite bilgisini GİRMİŞ olmalı.
          Profiline üniversitesini yazmak, bu ağda görünmeyi seçmektir; yazmayan kişi
          listede zaten çıkmıyor (bkz. SearchUniversityPeers) ve ona bu yoldan istek de
          gidemez.

          Bu kapı olmadan uç, "herhangi bir kullanıcıya doğrudan mesaj isteği" hâline
          gelirdi — bu üründe engelleme/blok mekanizması OLMADIĞI için bunun bedeli
          yüksek olurdu.
        */
        if (request.RequestedTopicId is { } topicId)
        {
            var offers = await _db.PortfolioEntries.AnyAsync(p =>
                p.UserId == request.ResponderUserId && p.TopicId == topicId &&
                p.Direction == PortfolioDirection.Offer && p.IsActive, ct);

            if (!offers)
            {
                throw new AppException(ErrorCodes.MatchNotFound,
                    "Karşı taraf bu konuyu portföyünde sunmuyor.", statusCode: 409);
            }
        }
        else
        {
            var agdaMi = await _db.Users.AnyAsync(u =>
                u.Id == request.ResponderUserId &&
                u.Status == UserStatus.Active &&
                u.University != null && u.University != "", ct);

            if (!agdaMi)
            {
                throw new AppException(ErrorCodes.MatchNotFound,
                    "Bu kişi üniversite ağında görünmüyor.", statusCode: 409);
            }
        }

        /*
          MÜKERRER BEKLEYEN İSTEK.

          Konusuz istekte karşılaştırma `RequestedTopicId == null` ile yapılmalı,
          `== request.RequestedTopicId` ile DEĞİL: SQL'de NULL = NULL sonucu NULL'dır,
          yani hiçbir zaman true olmaz ve kontrol sessizce hiçbir şey bulmazdı. Aynı
          tuzak veritabanı tarafında da var — bu yüzden ikinci bir kısmi tekillik indeksi
          eklendi (bkz. MatchmakingConfigurations).
        */
        var pendingExists = request.RequestedTopicId is { } istenenKonu
            ? await _db.Matches.AnyAsync(m =>
                m.InitiatorUserId == request.InitiatorUserId &&
                m.ResponderUserId == request.ResponderUserId &&
                m.RequestedTopicId == istenenKonu &&
                m.Status == MatchStatus.Pending, ct)
            : await _db.Matches.AnyAsync(m =>
                m.InitiatorUserId == request.InitiatorUserId &&
                m.ResponderUserId == request.ResponderUserId &&
                m.RequestedTopicId == null &&
                m.Status == MatchStatus.Pending, ct);

        if (pendingExists)
        {
            throw new AppException(ErrorCodes.DuplicateMatchRequest,
                request.RequestedTopicId is null
                    ? "Bu kişiye zaten bekleyen bir isteğiniz var."
                    : "Bu kişiye bu konu için zaten bekleyen isteğiniz var.",
                statusCode: 409);
        }

        var match = new Match
        {
            InitiatorUserId = request.InitiatorUserId,
            ResponderUserId = request.ResponderUserId,
            RequestedTopicId = request.RequestedTopicId,
            OfferedTopicId = request.OfferedTopicId
        };

        _db.Matches.Add(match);
        await _db.SaveChangesAsync(ct); // Partial unique index (Pending) eşzamanlı istekte son savunma.

        return match.Id;
    }
}

/// <summary>Kabulde sohbet kanalı otomatik açılır (Modül 2.1: Match &amp; Chat).</summary>
public sealed record RespondMatchCommand(Guid MatchId, Guid ResponderUserId, bool Accept)
    : IRequest<RespondMatchResult>;

public sealed record RespondMatchResult(Guid MatchId, string Status, Guid? ConversationId);

public sealed class RespondMatchHandler : IRequestHandler<RespondMatchCommand, RespondMatchResult>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly BadgeEngine _badges;

    public RespondMatchHandler(IAppDbContext db, IClock clock, BadgeEngine badges)
    {
        _db = db;
        _clock = clock;
        _badges = badges;
    }

    public async Task<RespondMatchResult> Handle(RespondMatchCommand request, CancellationToken ct)
    {
        var match = await _db.Matches.SingleOrDefaultAsync(m => m.Id == request.MatchId, ct)
                    ?? throw new AppException(ErrorCodes.MatchNotFound, "Eşleşme bulunamadı.", statusCode: 404);

        if (match.ResponderUserId != request.ResponderUserId)
        {
            throw new AppException(ErrorCodes.NotMatchParticipant,
                "Bu isteği yalnızca muhatabı yanıtlayabilir.", statusCode: 403);
        }

        if (match.Status != MatchStatus.Pending)
        {
            throw new AppException(ErrorCodes.MatchNotPending,
                $"İstek zaten yanıtlanmış ({match.Status}).", statusCode: 409);
        }

        match.Status = request.Accept ? MatchStatus.Accepted : MatchStatus.Declined;
        match.RespondedAtUtc = _clock.UtcNow;

        Conversation? conversation = null;
        if (request.Accept)
        {
            conversation = new Conversation { MatchId = match.Id };
            _db.Conversations.Add(conversation);
        }

        /*
          ROZET DEĞERLENDİRMESİ BURADA ÇAĞRILIR.

          ⚡ Hızlı yanıt rozetinin dayandığı veriyi ÜRETEN akış burasıdır; tetikleyici
          listesinde olmasaydı, yalnızca isteklere hızla dönen (ama ders değerlendirmesi
          almayan, öğretmen adaylığı beyanı vermeyen) bir kullanıcı rozeti HİÇ göremezdi —
          rozet motoru arka planda koşmuyor, yalnızca kullanıcının verisini değiştiren
          akışların sonunda çalışıyor.

          SIRA: yanıt ÖNCE yazılır. Motor veriyi DB'den okuduğu için SaveChanges'ten önce
          çağrılsaydı az önceki yanıtı göremez ve 5. yanıtta açılması gereken eşik 6.'ya
          kayardı (bu projede iki kez düşülen tuzak; bkz. CreateReview notu).
        */
        await using var tx = await _db.BeginTransactionAsync(cancellationToken: ct);

        await _db.SaveChangesAsync(ct);
        await _badges.EvaluateAsync(request.ResponderUserId, ct);
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);

        return new RespondMatchResult(match.Id, match.Status.ToString(), conversation?.Id);
    }
}
