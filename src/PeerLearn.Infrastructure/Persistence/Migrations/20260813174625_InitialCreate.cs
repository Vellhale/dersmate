using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "comms");

            migrationBuilder.EnsureSchema(
                name: "economy");

            migrationBuilder.EnsureSchema(
                name: "moderation");

            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.EnsureSchema(
                name: "scheduling");

            migrationBuilder.EnsureSchema(
                name: "matchmaking");

            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "EducationCategories",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EducationCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EducationCategories_EducationCategories_ParentCategoryId",
                        column: x => x.ParentCategoryId,
                        principalSchema: "catalog",
                        principalTable: "EducationCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HwidBans",
                schema: "moderation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HwidHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RelatedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    BannedByAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HwidBans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "citext", nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    EmailVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PhoneVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    WelcomeCreditGrantedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Bio = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AvatarUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AverageRating = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false),
                    RatingCount = table.Column<int>(type: "integer", nullable: false),
                    LastLoginAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_AverageRating", "\"AverageRating\" BETWEEN 0 AND 5");
                    table.CheckConstraint("CK_Users_RatingCount", "\"RatingCount\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "Subjects",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subjects_EducationCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "catalog",
                        principalTable: "EducationCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserDevices",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    HwidHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserDevices_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserSanctions",
                schema: "moderation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IssuedByAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSanctions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSanctions_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Wallets",
                schema: "economy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AvailableBalance = table.Column<int>(type: "integer", nullable: false),
                    LockedBalance = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallets", x => x.Id);
                    table.CheckConstraint("CK_Wallets_AvailableBalance", "\"AvailableBalance\" >= 0");
                    table.CheckConstraint("CK_Wallets_LockedBalance", "\"LockedBalance\" >= 0");
                    table.ForeignKey(
                        name: "FK_Wallets_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Topics",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentTopicId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Topics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Topics_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalSchema: "catalog",
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Topics_Topics_ParentTopicId",
                        column: x => x.ParentTopicId,
                        principalSchema: "catalog",
                        principalTable: "Topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Matches",
                schema: "matchmaking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InitiatorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedTopicId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferedTopicId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RespondedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matches", x => x.Id);
                    table.CheckConstraint("CK_Matches_DifferentUsers", "\"InitiatorUserId\" <> \"ResponderUserId\"");
                    table.ForeignKey(
                        name: "FK_Matches_Topics_OfferedTopicId",
                        column: x => x.OfferedTopicId,
                        principalSchema: "catalog",
                        principalTable: "Topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Matches_Topics_RequestedTopicId",
                        column: x => x.RequestedTopicId,
                        principalSchema: "catalog",
                        principalTable: "Topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Matches_Users_InitiatorUserId",
                        column: x => x.InitiatorUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Matches_Users_ResponderUserId",
                        column: x => x.ResponderUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioEntries",
                schema: "matchmaking",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    SelfAssessedLevel = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioEntries", x => x.Id);
                    table.CheckConstraint("CK_PortfolioEntries_Level", "\"SelfAssessedLevel\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_PortfolioEntries_Topics_TopicId",
                        column: x => x.TopicId,
                        principalSchema: "catalog",
                        principalTable: "Topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PortfolioEntries_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Conversations",
                schema: "comms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastMessageAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Conversations_Matches_MatchId",
                        column: x => x.MatchId,
                        principalSchema: "matchmaking",
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LessonSessions",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    TutorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduledStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    ScheduledEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreditCost = table.Column<int>(type: "integer", nullable: false),
                    VerificationCode = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CompletionRequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonSessions", x => x.Id);
                    table.CheckConstraint("CK_LessonSessions_CreditCost", "\"CreditCost\" > 0");
                    table.CheckConstraint("CK_LessonSessions_DifferentUsers", "\"TutorUserId\" <> \"StudentUserId\"");
                    table.CheckConstraint("CK_LessonSessions_Duration", "\"DurationMinutes\" BETWEEN 30 AND 180");
                    table.CheckConstraint("CK_LessonSessions_EndAfterStart", "\"ScheduledEndUtc\" > \"ScheduledStartUtc\"");
                    table.ForeignKey(
                        name: "FK_LessonSessions_Matches_MatchId",
                        column: x => x.MatchId,
                        principalSchema: "matchmaking",
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LessonSessions_Topics_TopicId",
                        column: x => x.TopicId,
                        principalSchema: "catalog",
                        principalTable: "Topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LessonSessions_Users_StudentUserId",
                        column: x => x.StudentUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LessonSessions_Users_TutorUserId",
                        column: x => x.TutorUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                schema: "comms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "comms",
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Messages_Users_SenderUserId",
                        column: x => x.SenderUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreditHolds",
                schema: "economy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditHolds", x => x.Id);
                    table.CheckConstraint("CK_CreditHolds_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_CreditHolds_LessonSessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "scheduling",
                        principalTable: "LessonSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditHolds_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalSchema: "economy",
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreditLots",
                schema: "economy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    InitialAmount = table.Column<int>(type: "integer", nullable: false),
                    RemainingAmount = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SourceSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    EarnedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditLots", x => x.Id);
                    table.CheckConstraint("CK_CreditLots_InitialAmount", "\"InitialAmount\" > 0");
                    table.CheckConstraint("CK_CreditLots_RemainingAmount", "\"RemainingAmount\" >= 0 AND \"RemainingAmount\" <= \"InitialAmount\"");
                    table.ForeignKey(
                        name: "FK_CreditLots_LessonSessions_SourceSessionId",
                        column: x => x.SourceSessionId,
                        principalSchema: "scheduling",
                        principalTable: "LessonSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditLots_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalSchema: "economy",
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreditTransactions",
                schema: "economy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RelatedSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CounterpartyUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    BalanceAfter = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditTransactions", x => x.Id);
                    table.CheckConstraint("CK_CreditTransactions_Amount", "\"Amount\" <> 0");
                    table.CheckConstraint("CK_CreditTransactions_TransferLegs", "\"Type\" NOT IN ('LessonEarning', 'LessonSpending') OR (\"CorrelationId\" IS NOT NULL AND \"CounterpartyUserId\" IS NOT NULL AND \"RelatedSessionId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_CreditTransactions_LessonSessions_RelatedSessionId",
                        column: x => x.RelatedSessionId,
                        principalSchema: "scheduling",
                        principalTable: "LessonSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditTransactions_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalSchema: "economy",
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Disputes",
                schema: "moderation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RaisedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ResolvedByAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolutionNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Disputes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Disputes_LessonSessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "scheduling",
                        principalTable: "LessonSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Disputes_Users_RaisedByUserId",
                        column: x => x.RaisedByUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionProofs",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256Hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionProofs", x => x.Id);
                    table.CheckConstraint("CK_SessionProofs_FileSize", "\"FileSizeBytes\" > 0 AND \"FileSizeBytes\" <= 10485760");
                    table.ForeignKey(
                        name: "FK_SessionProofs_LessonSessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "scheduling",
                        principalTable: "LessonSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionProofs_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionReviews",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevieweeUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionReviews", x => x.Id);
                    table.CheckConstraint("CK_SessionReviews_Score", "\"Score\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_SessionReviews_LessonSessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "scheduling",
                        principalTable: "LessonSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionReviews_Users_RevieweeUserId",
                        column: x => x.RevieweeUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionReviews_Users_ReviewerUserId",
                        column: x => x.ReviewerUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreditLotConsumptions",
                schema: "economy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreditLotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    IsReversal = table.Column<bool>(type: "boolean", nullable: false),
                    CreditHoldId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreditTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditLotConsumptions", x => x.Id);
                    table.CheckConstraint("CK_CreditLotConsumptions_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_CreditLotConsumptions_ReversalRequiresHold", "NOT \"IsReversal\" OR \"CreditHoldId\" IS NOT NULL");
                    table.CheckConstraint("CK_CreditLotConsumptions_SingleParent", "num_nonnulls(\"CreditHoldId\", \"CreditTransactionId\") = 1");
                    table.ForeignKey(
                        name: "FK_CreditLotConsumptions_CreditHolds_CreditHoldId",
                        column: x => x.CreditHoldId,
                        principalSchema: "economy",
                        principalTable: "CreditHolds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditLotConsumptions_CreditLots_CreditLotId",
                        column: x => x.CreditLotId,
                        principalSchema: "economy",
                        principalTable: "CreditLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditLotConsumptions_CreditTransactions_CreditTransactionId",
                        column: x => x.CreditTransactionId,
                        principalSchema: "economy",
                        principalTable: "CreditTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_MatchId",
                schema: "comms",
                table: "Conversations",
                column: "MatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditHolds_SessionId",
                schema: "economy",
                table: "CreditHolds",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditHolds_WalletId_Status",
                schema: "economy",
                table: "CreditHolds",
                columns: new[] { "WalletId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditLotConsumptions_CreditHoldId_CreditLotId_IsReversal",
                schema: "economy",
                table: "CreditLotConsumptions",
                columns: new[] { "CreditHoldId", "CreditLotId", "IsReversal" },
                unique: true,
                filter: "\"CreditHoldId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLotConsumptions_CreditLotId",
                schema: "economy",
                table: "CreditLotConsumptions",
                column: "CreditLotId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLotConsumptions_CreditTransactionId_CreditLotId",
                schema: "economy",
                table: "CreditLotConsumptions",
                columns: new[] { "CreditTransactionId", "CreditLotId" },
                unique: true,
                filter: "\"CreditTransactionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLots_ExpiresAtUtc",
                schema: "economy",
                table: "CreditLots",
                column: "ExpiresAtUtc",
                filter: "\"RemainingAmount\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLots_SourceSessionId",
                schema: "economy",
                table: "CreditLots",
                column: "SourceSessionId",
                unique: true,
                filter: "\"Source\" = 'LessonEarning'");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLots_WalletId",
                schema: "economy",
                table: "CreditLots",
                column: "WalletId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLots_WalletId_ExpiresAtUtc",
                schema: "economy",
                table: "CreditLots",
                columns: new[] { "WalletId", "ExpiresAtUtc" },
                filter: "\"RemainingAmount\" > 0");

            migrationBuilder.CreateIndex(
                name: "UX_CreditLots_OneWelcomeBonusPerWallet",
                schema: "economy",
                table: "CreditLots",
                column: "WalletId",
                unique: true,
                filter: "\"Source\" = 'WelcomeBonus'");

            migrationBuilder.CreateIndex(
                name: "IX_CreditTransactions_CorrelationId",
                schema: "economy",
                table: "CreditTransactions",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditTransactions_RelatedSessionId_Type",
                schema: "economy",
                table: "CreditTransactions",
                columns: new[] { "RelatedSessionId", "Type" },
                unique: true,
                filter: "\"Type\" IN ('LessonSpending', 'LessonEarning')");

            migrationBuilder.CreateIndex(
                name: "IX_CreditTransactions_WalletId_CreatedAtUtc",
                schema: "economy",
                table: "CreditTransactions",
                columns: new[] { "WalletId", "CreatedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_RaisedByUserId",
                schema: "moderation",
                table: "Disputes",
                column: "RaisedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_SessionId",
                schema: "moderation",
                table: "Disputes",
                column: "SessionId",
                unique: true,
                filter: "\"Status\" IN ('Open', 'UnderReview')");

            migrationBuilder.CreateIndex(
                name: "IX_Disputes_Status_CreatedAtUtc",
                schema: "moderation",
                table: "Disputes",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EducationCategories_ParentCategoryId",
                schema: "catalog",
                table: "EducationCategories",
                column: "ParentCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_EducationCategories_Slug",
                schema: "catalog",
                table: "EducationCategories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HwidBans_HwidHash",
                schema: "moderation",
                table: "HwidBans",
                column: "HwidHash",
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_LessonSessions_MatchId",
                schema: "scheduling",
                table: "LessonSessions",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonSessions_Status_ScheduledEndUtc",
                schema: "scheduling",
                table: "LessonSessions",
                columns: new[] { "Status", "ScheduledEndUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonSessions_StudentUserId_ScheduledStartUtc",
                schema: "scheduling",
                table: "LessonSessions",
                columns: new[] { "StudentUserId", "ScheduledStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonSessions_TopicId",
                schema: "scheduling",
                table: "LessonSessions",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonSessions_TutorUserId_ScheduledStartUtc",
                schema: "scheduling",
                table: "LessonSessions",
                columns: new[] { "TutorUserId", "ScheduledStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonSessions_VerificationCode",
                schema: "scheduling",
                table: "LessonSessions",
                column: "VerificationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Matches_InitiatorUserId_ResponderUserId_RequestedTopicId",
                schema: "matchmaking",
                table: "Matches",
                columns: new[] { "InitiatorUserId", "ResponderUserId", "RequestedTopicId" },
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_InitiatorUserId_Status",
                schema: "matchmaking",
                table: "Matches",
                columns: new[] { "InitiatorUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_OfferedTopicId",
                schema: "matchmaking",
                table: "Matches",
                column: "OfferedTopicId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_RequestedTopicId",
                schema: "matchmaking",
                table: "Matches",
                column: "RequestedTopicId");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_ResponderUserId_Status",
                schema: "matchmaking",
                table: "Matches",
                columns: new[] { "ResponderUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId_CreatedAtUtc",
                schema: "comms",
                table: "Messages",
                columns: new[] { "ConversationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId_SenderUserId",
                schema: "comms",
                table: "Messages",
                columns: new[] { "ConversationId", "SenderUserId" },
                filter: "\"ReadAtUtc\" IS NULL AND \"IsDeleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SenderUserId",
                schema: "comms",
                table: "Messages",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioEntries_TopicId_Direction",
                schema: "matchmaking",
                table: "PortfolioEntries",
                columns: new[] { "TopicId", "Direction" },
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioEntries_UserId_TopicId_Direction",
                schema: "matchmaking",
                table: "PortfolioEntries",
                columns: new[] { "UserId", "TopicId", "Direction" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionProofs_SessionId",
                schema: "scheduling",
                table: "SessionProofs",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionProofs_Sha256Hash",
                schema: "scheduling",
                table: "SessionProofs",
                column: "Sha256Hash");

            migrationBuilder.CreateIndex(
                name: "IX_SessionProofs_UploadedByUserId",
                schema: "scheduling",
                table: "SessionProofs",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionReviews_RevieweeUserId",
                schema: "scheduling",
                table: "SessionReviews",
                column: "RevieweeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionReviews_ReviewerUserId",
                schema: "scheduling",
                table: "SessionReviews",
                column: "ReviewerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionReviews_SessionId_ReviewerUserId",
                schema: "scheduling",
                table: "SessionReviews",
                columns: new[] { "SessionId", "ReviewerUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_CategoryId_Name",
                schema: "catalog",
                table: "Subjects",
                columns: new[] { "CategoryId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Topics_ParentTopicId",
                schema: "catalog",
                table: "Topics",
                column: "ParentTopicId");

            migrationBuilder.CreateIndex(
                name: "IX_Topics_SubjectId_Name",
                schema: "catalog",
                table: "Topics",
                columns: new[] { "SubjectId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserDevices_HwidHash",
                schema: "identity",
                table: "UserDevices",
                column: "HwidHash");

            migrationBuilder.CreateIndex(
                name: "IX_UserDevices_UserId_HwidHash",
                schema: "identity",
                table: "UserDevices",
                columns: new[] { "UserId", "HwidHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                schema: "identity",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_PhoneNumber",
                schema: "identity",
                table: "Users",
                column: "PhoneNumber",
                unique: true,
                filter: "\"PhoneNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserSanctions_UserId_CreatedAtUtc",
                schema: "moderation",
                table: "UserSanctions",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_UserId",
                schema: "economy",
                table: "Wallets",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CreditLotConsumptions",
                schema: "economy");

            migrationBuilder.DropTable(
                name: "Disputes",
                schema: "moderation");

            migrationBuilder.DropTable(
                name: "HwidBans",
                schema: "moderation");

            migrationBuilder.DropTable(
                name: "Messages",
                schema: "comms");

            migrationBuilder.DropTable(
                name: "PortfolioEntries",
                schema: "matchmaking");

            migrationBuilder.DropTable(
                name: "SessionProofs",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "SessionReviews",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "UserDevices",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "UserSanctions",
                schema: "moderation");

            migrationBuilder.DropTable(
                name: "CreditHolds",
                schema: "economy");

            migrationBuilder.DropTable(
                name: "CreditLots",
                schema: "economy");

            migrationBuilder.DropTable(
                name: "CreditTransactions",
                schema: "economy");

            migrationBuilder.DropTable(
                name: "Conversations",
                schema: "comms");

            migrationBuilder.DropTable(
                name: "LessonSessions",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "Wallets",
                schema: "economy");

            migrationBuilder.DropTable(
                name: "Matches",
                schema: "matchmaking");

            migrationBuilder.DropTable(
                name: "Topics",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "Subjects",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "EducationCategories",
                schema: "catalog");
        }
    }
}
