using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BildirimAltyapisi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MessagePushThrottles",
                schema: "comms",
                columns: table => new
                {
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastSentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessagePushThrottles", x => new { x.RecipientUserId, x.ConversationId });
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                schema: "comms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Messages = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Requests = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    LessonApproval = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    LessonPlan = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    DisclosureShownAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PromptDeferCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    PromptDeferredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                    table.CheckConstraint("CK_NotificationPreferences_PromptDeferCount", "\"PromptDeferCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_NotificationPreferences_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                schema: "comms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DedupeKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    OlayDamgasiUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LeaseOwner = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.CheckConstraint("CK_Notifications_Attempts", "\"Attempts\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "PushDevices",
                schema: "comms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Platform = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    HwidHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    KapaliKanallar = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'"),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PushDevices_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PushTickets",
                schema: "comms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PushDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushTickets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_KullaniciCihaz",
                schema: "identity",
                table: "RefreshTokens",
                columns: new[] { "UserId", "DeviceHwidHash", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonSessions_OnayBekleyen",
                schema: "scheduling",
                table: "LessonSessions",
                column: "CompletionRequestedAtUtc",
                filter: "\"Status\" = 'AwaitingApproval'");

            migrationBuilder.CreateIndex(
                name: "IX_LessonSessions_YaklasanDers",
                schema: "scheduling",
                table: "LessonSessions",
                column: "ScheduledStartUtc",
                filter: "\"Status\" = 'Booked'");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_UserId",
                schema: "comms",
                table: "NotificationPreferences",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Bekleyen",
                schema: "comms",
                table: "Notifications",
                column: "DueAtUtc",
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CreatedAtUtc",
                schema: "comms",
                table: "Notifications",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_ConversationId",
                schema: "comms",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "ConversationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_DedupeKey",
                schema: "comms",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "DedupeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_Type_ActorUserId",
                schema: "comms",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "Type", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PushDevices_Token",
                schema: "comms",
                table: "PushDevices",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushDevices_UserId_HwidHash",
                schema: "comms",
                table: "PushDevices",
                columns: new[] { "UserId", "HwidHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushTickets_CreatedAtUtc",
                schema: "comms",
                table: "PushTickets",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PushTickets_TicketId",
                schema: "comms",
                table: "PushTickets",
                column: "TicketId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessagePushThrottles",
                schema: "comms");

            migrationBuilder.DropTable(
                name: "NotificationPreferences",
                schema: "comms");

            migrationBuilder.DropTable(
                name: "Notifications",
                schema: "comms");

            migrationBuilder.DropTable(
                name: "PushDevices",
                schema: "comms");

            migrationBuilder.DropTable(
                name: "PushTickets",
                schema: "comms");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_KullaniciCihaz",
                schema: "identity",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_LessonSessions_OnayBekleyen",
                schema: "scheduling",
                table: "LessonSessions");

            migrationBuilder.DropIndex(
                name: "IX_LessonSessions_YaklasanDers",
                schema: "scheduling",
                table: "LessonSessions");
        }
    }
}
