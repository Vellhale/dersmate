using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Domain.Matchmaking;

namespace PeerLearn.Application.Features.Communication;

public sealed record GetConversationsQuery(Guid UserId) : IRequest<IReadOnlyList<ConversationDto>>;

public sealed record ConversationDto(
    Guid ConversationId,
    Guid MatchId,
    Guid OtherUserId,
    string OtherDisplayName,
    DateTime? LastMessageAtUtc,
    int UnreadCount,

    /// <summary>Eşleşme sonlandırıldı: geçmiş okunur, yeni mesaj yazılamaz.</summary>
    bool IsClosed);

public sealed class GetConversationsHandler : IRequestHandler<GetConversationsQuery, IReadOnlyList<ConversationDto>>
{
    private readonly IAppDbContext _db;

    public GetConversationsHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ConversationDto>> Handle(GetConversationsQuery request, CancellationToken ct)
    {
        var me = request.UserId;

        // Sonlandırılmış eşleşmelerin sohbeti listede KALIR (salt okunur). Listeden
        // düşseydi kullanıcı kendi geçmişine ulaşamazdı ve okunmamış mesajlar da
        // erişilemez hâle gelirdi — rozet sayacı sıfırlanamayan bir kalıntı bırakırdı.
        var conversations = await (
                from c in _db.Conversations.AsNoTracking()
                join m in _db.Matches.AsNoTracking() on c.MatchId equals m.Id
                where (m.Status == MatchStatus.Accepted || m.Status == MatchStatus.Closed) &&
                      (m.InitiatorUserId == me || m.ResponderUserId == me)
                select new
                {
                    ConversationId = c.Id,
                    MatchId = m.Id,
                    OtherUserId = m.InitiatorUserId == me ? m.ResponderUserId : m.InitiatorUserId,
                    c.LastMessageAtUtc,
                    IsClosed = m.Status == MatchStatus.Closed
                })
            .ToListAsync(ct);

        if (conversations.Count == 0)
        {
            return [];
        }

        var conversationIds = conversations.Select(c => c.ConversationId).ToList();

        // Partial index (ReadAtUtc IS NULL AND IsDeleted = FALSE) tam bu sorgu içindir;
        // WHERE koşulları index filtresini birebir içermelidir.
        var unreadCounts = await _db.Messages.AsNoTracking()
            .Where(msg => conversationIds.Contains(msg.ConversationId) &&
                          msg.ReadAtUtc == null && !msg.IsDeleted && msg.SenderUserId != me)
            .GroupBy(msg => msg.ConversationId)
            .Select(g => new { ConversationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ConversationId, x => x.Count, ct);

        var otherIds = conversations.Select(c => c.OtherUserId).Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => otherIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        return conversations
            .Select(c => new ConversationDto(
                c.ConversationId,
                c.MatchId,
                c.OtherUserId,
                names.GetValueOrDefault(c.OtherUserId, "?"),
                c.LastMessageAtUtc,
                unreadCounts.GetValueOrDefault(c.ConversationId),
                c.IsClosed))
            .OrderByDescending(c => c.LastMessageAtUtc ?? DateTime.MinValue)
            .ToList();
    }
}

public sealed record GetMessagesQuery(Guid ConversationId, Guid UserId, int Page = 1, int PageSize = 50)
    : IRequest<IReadOnlyList<MessageDto>>;

public sealed class GetMessagesHandler : IRequestHandler<GetMessagesQuery, IReadOnlyList<MessageDto>>
{
    private readonly IAppDbContext _db;

    public GetMessagesHandler(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<MessageDto>> Handle(GetMessagesQuery request, CancellationToken ct)
    {
        await ConversationAccess.GetForReadAsync(_db, request.ConversationId, request.UserId, ct);

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        // (ConversationId, CreatedAtUtc) index'i ile sayfalı geçmiş; istemci ters çevirir.
        return await _db.Messages.AsNoTracking()
            .Where(m => m.ConversationId == request.ConversationId && !m.IsDeleted)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MessageDto(m.Id, m.ConversationId, m.SenderUserId, m.Content, m.CreatedAtUtc))
            .ToListAsync(ct);
    }
}

/// <summary>Karşı tarafın mesajlarını okundu işaretler; okunmamış rozeti sıfırlanır.</summary>
public sealed record MarkConversationReadCommand(Guid ConversationId, Guid UserId) : IRequest<int>;

public sealed class MarkConversationReadHandler : IRequestHandler<MarkConversationReadCommand, int>
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public MarkConversationReadHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<int> Handle(MarkConversationReadCommand request, CancellationToken ct)
    {
        // OKUMA erişimi yeterli — hatta şart: sonlandırılmış sohbette de okunmamış mesaj
        // kalabilir ve yazma kuralı uygulansaydı o rozet kalıcı olarak sıfırlanamazdı.
        await ConversationAccess.GetForReadAsync(_db, request.ConversationId, request.UserId, ct);

        var now = _clock.UtcNow;

        // Message'ta concurrency token yok → ExecuteUpdate güvenli ve tek round-trip.
        return await _db.Messages
            .Where(m => m.ConversationId == request.ConversationId &&
                        m.SenderUserId != request.UserId &&
                        m.ReadAtUtc == null && !m.IsDeleted)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadAtUtc, now), ct);
    }
}
