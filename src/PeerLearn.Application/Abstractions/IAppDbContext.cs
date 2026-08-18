using System.Data;
using Microsoft.EntityFrameworkCore;
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
    DbSet<UserPreference> UserPreferences { get; }

    DbSet<PortfolioEntry> PortfolioEntries { get; }
    DbSet<Match> Matches { get; }

    DbSet<Conversation> Conversations { get; }
    DbSet<Message> Messages { get; }

    DbSet<LessonSession> LessonSessions { get; }
    DbSet<SessionProof> SessionProofs { get; }
    DbSet<SessionReview> SessionReviews { get; }
    DbSet<SessionReviewTag> SessionReviewTags { get; }
    DbSet<SweepFailure> SweepFailures { get; }

    DbSet<Badge> Badges { get; }
    DbSet<UserBadge> UserBadges { get; }
    DbSet<UserSubjectBadge> UserSubjectBadges { get; }
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
}
