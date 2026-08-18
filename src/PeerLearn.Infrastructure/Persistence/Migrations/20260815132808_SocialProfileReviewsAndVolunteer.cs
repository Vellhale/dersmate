using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SocialProfileReviewsAndVolunteer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SessionReviews_RevieweeUserId",
                schema: "scheduling",
                table: "SessionReviews");

            migrationBuilder.EnsureSchema(
                name: "community");

            migrationBuilder.AddColumn<string>(
                name: "Department",
                schema: "identity",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TaughtMinutes",
                schema: "identity",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TaughtSessionCount",
                schema: "identity",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "University",
                schema: "identity",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PunctualityScore",
                schema: "scheduling",
                table: "SessionReviews",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TeachingScore",
                schema: "scheduling",
                table: "SessionReviews",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "WasVolunteerSession",
                schema: "scheduling",
                table: "SessionReviews",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsVolunteer",
                schema: "matchmaking",
                table: "PortfolioEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Badges",
                schema: "community",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Emoji = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Badges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SessionReviewTags",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tag = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionReviewTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionReviewTags_SessionReviews_ReviewId",
                        column: x => x.ReviewId,
                        principalSchema: "scheduling",
                        principalTable: "SessionReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeacherCandidateProfiles",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    University = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Faculty = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Department = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    GradeYear = table.Column<int>(type: "integer", nullable: true),
                    HasPedagogicalCertificate = table.Column<bool>(type: "boolean", nullable: false),
                    DeclaredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifiedByAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherCandidateProfiles", x => x.Id);
                    table.CheckConstraint("CK_TeacherCandidate_GradeYear", "\"GradeYear\" IS NULL OR \"GradeYear\" BETWEEN 1 AND 6");
                    table.ForeignKey(
                        name: "FK_TeacherCandidateProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserBadges",
                schema: "community",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BadgeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EarnedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsFeatured = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBadges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserBadges_Badges_BadgeId",
                        column: x => x.BadgeId,
                        principalSchema: "community",
                        principalTable: "Badges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserBadges_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionReviews_RevieweeUserId_CreatedAtUtc",
                schema: "scheduling",
                table: "SessionReviews",
                columns: new[] { "RevieweeUserId", "CreatedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SessionReviews_NotSelf",
                schema: "scheduling",
                table: "SessionReviews",
                sql: "\"ReviewerUserId\" <> \"RevieweeUserId\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SessionReviews_PunctualityScore",
                schema: "scheduling",
                table: "SessionReviews",
                sql: "\"PunctualityScore\" BETWEEN 1 AND 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SessionReviews_TeachingScore",
                schema: "scheduling",
                table: "SessionReviews",
                sql: "\"TeachingScore\" BETWEEN 1 AND 5");

            migrationBuilder.CreateIndex(
                name: "IX_Badges_Code",
                schema: "community",
                table: "Badges",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionReviewTags_ReviewId_Tag",
                schema: "scheduling",
                table: "SessionReviewTags",
                columns: new[] { "ReviewId", "Tag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeacherCandidateProfiles_UserId",
                schema: "identity",
                table: "TeacherCandidateProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserBadges_BadgeId",
                schema: "community",
                table: "UserBadges",
                column: "BadgeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBadges_UserId_BadgeId",
                schema: "community",
                table: "UserBadges",
                columns: new[] { "UserId", "BadgeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserBadges_UserId_EarnedAtUtc",
                schema: "community",
                table: "UserBadges",
                columns: new[] { "UserId", "EarnedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionReviewTags",
                schema: "scheduling");

            migrationBuilder.DropTable(
                name: "TeacherCandidateProfiles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "UserBadges",
                schema: "community");

            migrationBuilder.DropTable(
                name: "Badges",
                schema: "community");

            migrationBuilder.DropIndex(
                name: "IX_SessionReviews_RevieweeUserId_CreatedAtUtc",
                schema: "scheduling",
                table: "SessionReviews");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SessionReviews_NotSelf",
                schema: "scheduling",
                table: "SessionReviews");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SessionReviews_PunctualityScore",
                schema: "scheduling",
                table: "SessionReviews");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SessionReviews_TeachingScore",
                schema: "scheduling",
                table: "SessionReviews");

            migrationBuilder.DropColumn(
                name: "Department",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TaughtMinutes",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TaughtSessionCount",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "University",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PunctualityScore",
                schema: "scheduling",
                table: "SessionReviews");

            migrationBuilder.DropColumn(
                name: "TeachingScore",
                schema: "scheduling",
                table: "SessionReviews");

            migrationBuilder.DropColumn(
                name: "WasVolunteerSession",
                schema: "scheduling",
                table: "SessionReviews");

            migrationBuilder.DropColumn(
                name: "IsVolunteer",
                schema: "matchmaking",
                table: "PortfolioEntries");

            migrationBuilder.CreateIndex(
                name: "IX_SessionReviews_RevieweeUserId",
                schema: "scheduling",
                table: "SessionReviews",
                column: "RevieweeUserId");
        }
    }
}
