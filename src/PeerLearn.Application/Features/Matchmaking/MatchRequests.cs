using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Features.Community;
using PeerLearn.Domain.Communication;
using PeerLearn.Domain.Matchmaking;

namespace PeerLearn.Application.Features.Matchmaking;

/// <summary>
/// Eşleşme isteği: RequestedTopicId = karşı taraftan almak istediğim konu;
/// OfferedTopicId (opsiyonel) = karşılığında anlatmayı önerdiğim konu (çapraz teklif).
/// </summary>
public sealed record CreateMatchRequestCommand(
    Guid InitiatorUserId,
    Guid ResponderUserId,
    Guid RequestedTopicId,
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

        // Karşı taraf bu konuyu gerçekten Offer ediyor olmalı (rastgele istek spam'ini keser).
        var offers = await _db.PortfolioEntries.AnyAsync(p =>
            p.UserId == request.ResponderUserId && p.TopicId == request.RequestedTopicId &&
            p.Direction == PortfolioDirection.Offer && p.IsActive, ct);

        if (!offers)
        {
            throw new AppException(ErrorCodes.MatchNotFound,
                "Karşı taraf bu konuyu portföyünde sunmuyor.", statusCode: 409);
        }

        var pendingExists = await _db.Matches.AnyAsync(m =>
            m.InitiatorUserId == request.InitiatorUserId &&
            m.ResponderUserId == request.ResponderUserId &&
            m.RequestedTopicId == request.RequestedTopicId &&
            m.Status == MatchStatus.Pending, ct);

        if (pendingExists)
        {
            throw new AppException(ErrorCodes.DuplicateMatchRequest,
                "Bu kişiye bu konu için zaten bekleyen isteğiniz var.", statusCode: 409);
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
