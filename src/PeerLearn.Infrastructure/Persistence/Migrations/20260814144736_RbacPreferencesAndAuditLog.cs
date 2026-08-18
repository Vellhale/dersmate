using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RbacPreferencesAndAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /*
              SIRA ÖNEMLİ — scaffold edilen hâli elle düzeltildi. EF varsayılan olarak önce
              IsAdmin'i DÜŞÜRÜP sonra Role'ü BOŞ DİZE varsayılanıyla ekliyordu. İki ayrı hata:
                (1) Veri kaybı: mevcut adminler (admin@demo.dev dahil) sessizce Student olur,
                    kimse yönetim paneline giremezdi.
                (2) Geçersiz değer: Role = '' hiçbir enum adına karşılık gelmez, EF ilk okumada
                    patlardı.
              Doğrusu: önce kolonu geçerli varsayılanla ekle, veriyi TAŞI, sonra eskisini düşür.
            */
            migrationBuilder.AddColumn<string>(
                name: "Role",
                schema: "identity",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Student");

            migrationBuilder.Sql(
                """
                UPDATE identity."Users" SET "Role" = 'Admin' WHERE "IsAdmin" = TRUE;
                """);

            migrationBuilder.DropColumn(
                name: "IsAdmin",
                schema: "identity",
                table: "Users");

            migrationBuilder.CreateTable(
                name: "AdminActionLogs",
                schema: "moderation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorRole = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminActionLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminActionLogs_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserPreferences",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OnboardingCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    OnboardingCompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OnboardingLastStep = table.Column<int>(type: "integer", nullable: false),
                    OnboardingSuppressed = table.Column<bool>(type: "boolean", nullable: false),
                    AnalyticsConsent = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FunctionalConsent = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConsentVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ConsentUpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsentIpHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPreferences", x => x.Id);
                    table.CheckConstraint("CK_UserPreferences_ConsentTimestamped", "(\"AnalyticsConsent\" = 'NotAsked' AND \"FunctionalConsent\" = 'NotAsked') OR (\"ConsentUpdatedAtUtc\" IS NOT NULL AND \"ConsentVersion\" IS NOT NULL)");
                    table.CheckConstraint("CK_UserPreferences_OnboardingStep", "\"OnboardingLastStep\" >= 0");
                    table.ForeignKey(
                        name: "FK_UserPreferences_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Role",
                schema: "identity",
                table: "Users",
                column: "Role",
                filter: "\"Role\" <> 'Student'");

            migrationBuilder.CreateIndex(
                name: "IX_AdminActionLogs_ActorUserId_CreatedAtUtc",
                schema: "moderation",
                table: "AdminActionLogs",
                columns: new[] { "ActorUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminActionLogs_CreatedAtUtc",
                schema: "moderation",
                table: "AdminActionLogs",
                column: "CreatedAtUtc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_AdminActionLogs_TargetType_TargetId",
                schema: "moderation",
                table: "AdminActionLogs",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserPreferences_UserId",
                schema: "identity",
                table: "UserPreferences",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminActionLogs",
                schema: "moderation");

            migrationBuilder.DropTable(
                name: "UserPreferences",
                schema: "identity");

            migrationBuilder.DropIndex(
                name: "IX_Users_Role",
                schema: "identity",
                table: "Users");

            // Geri alışta da veri taşınır. Moderator → IsAdmin=true: eski modelde ara yetki
            // yoktu ve moderatörün yönetim erişimini geri alışta kaybetmesi, sessizce yetki
            // düşürmek olurdu. Kayıp bilgi (Admin/Moderator ayrımı) geri alışın doğasında var.
            migrationBuilder.AddColumn<bool>(
                name: "IsAdmin",
                schema: "identity",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE identity."Users" SET "IsAdmin" = TRUE WHERE "Role" IN ('Admin', 'Moderator');
                """);

            migrationBuilder.DropColumn(
                name: "Role",
                schema: "identity",
                table: "Users");
        }
    }
}
