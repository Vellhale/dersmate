using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScaleAndRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ContentDeletedAtUtc",
                schema: "scheduling",
                table: "SessionProofs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAtUtc",
                schema: "matchmaking",
                table: "Matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClosedByUserId",
                schema: "matchmaking",
                table: "Matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SweepFailures",
                schema: "scheduling",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    FailureCount = table.Column<int>(type: "integer", nullable: false),
                    LastFailedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SweepFailures", x => x.Id);
                    table.CheckConstraint("CK_SweepFailures_Count", "\"FailureCount\" > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionProofs_Retention",
                schema: "scheduling",
                table: "SessionProofs",
                column: "CreatedAtUtc",
                filter: "\"ContentDeletedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_PendingAge",
                schema: "matchmaking",
                table: "Matches",
                column: "CreatedAtUtc",
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_SweepFailures_NextAttemptAtUtc",
                schema: "scheduling",
                table: "SweepFailures",
                column: "NextAttemptAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SweepFailures_RecordType_RecordId",
                schema: "scheduling",
                table: "SweepFailures",
                columns: new[] { "RecordType", "RecordId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SweepFailures",
                schema: "scheduling");

            migrationBuilder.DropIndex(
                name: "IX_SessionProofs_Retention",
                schema: "scheduling",
                table: "SessionProofs");

            migrationBuilder.DropIndex(
                name: "IX_Matches_PendingAge",
                schema: "matchmaking",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "ContentDeletedAtUtc",
                schema: "scheduling",
                table: "SessionProofs");

            migrationBuilder.DropColumn(
                name: "ClosedAtUtc",
                schema: "matchmaking",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "ClosedByUserId",
                schema: "matchmaking",
                table: "Matches");
        }
    }
}
