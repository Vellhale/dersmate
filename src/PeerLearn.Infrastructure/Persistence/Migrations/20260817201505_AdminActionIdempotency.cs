using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdminActionIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                schema: "moderation",
                table: "AdminActionLogs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdminActionLogs_ActorUserId_IdempotencyKey",
                schema: "moderation",
                table: "AdminActionLogs",
                columns: new[] { "ActorUserId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AdminActionLogs_ActorUserId_IdempotencyKey",
                schema: "moderation",
                table: "AdminActionLogs");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                schema: "moderation",
                table: "AdminActionLogs");
        }
    }
}
