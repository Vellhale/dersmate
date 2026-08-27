using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PeerLearn.Application.Abstractions;
using PeerLearn.Domain.Catalog;
using PeerLearn.Domain.Communication;
using PeerLearn.Domain.Community;
using PeerLearn.Domain.Economy;
using PeerLearn.Domain.Identity;
using PeerLearn.Domain.Matchmaking;
using PeerLearn.Domain.Moderation;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Infrastructure.Persistence;

/// <summary>
/// Tek PostgreSQL veritabanı, modül başına ayrı şema:
/// catalog | identity | matchmaking | comms | scheduling | economy | moderation.
/// Modüler monolitten mikroservise geçişte her şema kendi veritabanına taşınabilir.
/// </summary>
public sealed class PeerLearnDbContext : DbContext, IAppDbContext
{
    public PeerLearnDbContext(DbContextOptions<PeerLearnDbContext> options) : base(options)
    {
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
        => Database.BeginTransactionAsync(isolationLevel, cancellationToken);

    public void ClearChangeTracker() => ChangeTracker.Clear();

    // Catalog (Ders Kataloğu)
    public DbSet<EducationCategory> EducationCategories => Set<EducationCategory>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Topic> Topics => Set<Topic>();

    // Identity (Kullanıcı)
    public DbSet<User> Users => Set<User>();
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<TeacherCandidateProfile> TeacherCandidateProfiles => Set<TeacherCandidateProfile>();

    // Matchmaking (Eşleştirme)
    public DbSet<PortfolioEntry> PortfolioEntries => Set<PortfolioEntry>();
    public DbSet<Match> Matches => Set<Match>();

    // Communication (İletişim)
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();

    // Scheduling (Ders Oturumları + Kanıt)
    public DbSet<LessonSession> LessonSessions => Set<LessonSession>();
    public DbSet<SessionProof> SessionProofs => Set<SessionProof>();
    public DbSet<SessionReview> SessionReviews => Set<SessionReview>();
    public DbSet<SessionReviewTag> SessionReviewTags => Set<SessionReviewTag>();
    public DbSet<SweepFailure> SweepFailures => Set<SweepFailure>();

    // Community (Rozetler)
    public DbSet<Badge> Badges => Set<Badge>();
    public DbSet<UserBadge> UserBadges => Set<UserBadge>();
    public DbSet<UserSubjectBadge> UserSubjectBadges => Set<UserSubjectBadge>();
    public DbSet<CommunityPost> CommunityPosts => Set<CommunityPost>();
    public DbSet<CommunityComment> CommunityComments => Set<CommunityComment>();
    public DbSet<CommunityVote> CommunityVotes => Set<CommunityVote>();

    // Economy (Sıfır Toplamlı Kredi Ekonomisi)
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<CreditLot> CreditLots => Set<CreditLot>();
    public DbSet<CreditTransaction> CreditTransactions => Set<CreditTransaction>();
    public DbSet<CreditLotConsumption> CreditLotConsumptions => Set<CreditLotConsumption>();

    // Moderation (İtiraz + Yaptırım)
    public DbSet<Dispute> Disputes => Set<Dispute>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<HwidBan> HwidBans => Set<HwidBan>();
    public DbSet<UserSanction> UserSanctions => Set<UserSanction>();
    public DbSet<AdminActionLog> AdminActionLogs => Set<AdminActionLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // citext: e-posta benzersizliğini büyük/küçük harf duyarsız kılar.
        modelBuilder.HasPostgresExtension("citext");

        // btree_gist: LessonSessions üzerindeki çakışan-zaman EXCLUDE kısıtı için gerekli
        // (eşitlik + aralık kesişimini aynı index'te birleştirir). Kısıtın kendisi EF ile
        // ifade edilemediği için migration'da ham SQL olarak tanımlanır.
        modelBuilder.HasPostgresExtension("btree_gist");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PeerLearnDbContext).Assembly);
    }
}
