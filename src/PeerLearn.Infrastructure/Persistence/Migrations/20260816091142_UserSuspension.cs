using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserSuspension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SuspendedUntilUtc",
                schema: "identity",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SuspendedUntilUtc",
                schema: "identity",
                table: "Users");
        }
    }
}
