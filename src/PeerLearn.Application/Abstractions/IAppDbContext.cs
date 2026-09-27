using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using PeerLearn.Domain.Catalog;
using PeerLearn.Domain.Communication;
using PeerLearn.Domain.Community;
using PeerLearn.Domain.Economy;
using PeerLearn.Domain.Identity;
using PeerLearn.Domain.Matchmaking;
using PeerLearn.Domain.Moderation;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Application.Abstractions;

/// <summary>
/// Application katmanının veritabanı kapısı. Clean Architecture gereği Application,
/// Infrastructure'a değil bu soyutlamaya bağımlıdır; PeerLearnDbContext bunu uygular.
/// </summary>
public interface IAppDbContext
{
    DbSet<EducationCategory> EducationCategories { get; }
    DbSet<Subject> Subjects { get; }
    DbSet<Topic> Topics { get; }

    DbSet<User> Users { get; }
    DbSet<UserDevice> UserDevices { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<UserBlock> UserBlocks { get; }
    DbSet<UserPreference> UserPreferences { get; }

    DbSet<PortfolioEntry> PortfolioEntries { get; }
    DbSet<Match> Matches { get; }

    DbSet<Conversation> Conversations { get; }
    DbSet<Message> Messages { get; }

    // Push bildirimleri (2026-09-25). Beşi de comms şemasında; ayrıntı: Domain/Communication.
    DbSet<PushDevice> PushDevices { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<PushTicket> PushTickets { get; }
    DbSet<NotificationPreference> NotificationPreferences { get; }
    DbSet<MessagePushThrottle> MessagePushThrottles { get; }

    DbSet<LessonSession> LessonSessions { get; }
    DbSet<SessionProof> SessionProofs { get; }
    DbSet<SessionReview> SessionReviews { get; }
    DbSet<SessionReviewTag> SessionReviewTags { get; }
    DbSet<SweepFailure> SweepFailures { get; }

    DbSet<Badge> Badges { get; }
    DbSet<UserBadge> UserBadges { get; }
    DbSet<UserSubjectBadge> UserSubjectBadges { get; }

    // Forum (2026-08-27). Üçü de community şemasında; ayrıntı: Domain/Community/Forum.cs
    DbSet<CommunityPost> CommunityPosts { get; }
    DbSet<CommunityComment> CommunityComments { get; }
    DbSet<CommunityVote> CommunityVotes { get; }
    DbSet<TeacherCandidateProfile> TeacherCandidateProfiles { get; }

    DbSet<Wallet> Wallets { get; }
    DbSet<CreditLot> CreditLots { get; }
    DbSet<CreditTransaction> CreditTransactions { get; }
    DbSet<CreditLotConsumption> CreditLotConsumptions { get; }

    DbSet<Dispute> Disputes { get; }
    DbSet<Report> Reports { get; }
    DbSet<HwidBan> HwidBans { get; }
    DbSet<UserSanction> UserSanctions { get; }
    DbSet<AdminActionLog> AdminActionLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Kredi transferi gibi çok tablolu atomik işlemler için (varsayılan: ReadCommitted).</summary>
    Task<IDbContextTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);

    /// <summary>Optimistic concurrency çakışmasında retry öncesi izlenen entity'leri sıfırlar.</summary>
    void ClearChangeTracker();

    /// <summary>
    /// Ham SQL kapısı (<c>ExecuteSqlInterpolatedAsync</c>, <c>SqlQuery</c>). DbContext'in
    /// kendi özelliği; PeerLearnDbContext ayrıca bir şey uygulamıyor.
    /// </summary>
    /// <remarks>
    /// NEDEN AÇILDI (2026-09-25, push bildirimleri): LINQ'in ifade edemediği ama doğruluğun
    /// ona dayandığı üç kalıp var ve üçü de tek ifadede ATOMİK olmak zorunda:
    /// <c>INSERT … ON CONFLICT DO NOTHING / DO UPDATE</c> (hatırlatma sahiplenmesi, tek
    /// sütunluk tercih yazımı, mesaj kısma yuvası), <c>FOR UPDATE SKIP LOCKED</c> (iki sunucu
    /// kopyasının aynı satırı sahiplenmemesi) ve <c>pg_advisory_xact_lock</c> (cihaz kaydı).
    /// "Oku, sonra yaz" biçiminde LINQ'e çevrilen her biri iki kopya arasında yarış açardı.
    ///
    /// ⚠️ Yalnızca interpolasyonlu biçimleri kullan (<c>…Interpolated…</c> / <c>SqlQuery</c>
    /// FormattableString): parametreler bağlanır. <c>ExecuteSqlRaw</c>'a kullanıcı girdisi
    /// birleştirmek SQL enjeksiyonu demektir. Kolon adı gibi tanımlayıcılar parametre
    /// olamaz; onları yalnızca sabit bir beyaz listeden seç (ör. BildirimKanallari.TercihSutunu).
    /// </remarks>
    DatabaseFacade Database { get; }
}
