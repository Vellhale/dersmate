using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DisputeTutorStatement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TutorStatement",
                schema: "moderation",
                table: "Disputes",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TutorStatementAtUtc",
                schema: "moderation",
                table: "Disputes",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TutorStatement",
                schema: "moderation",
                table: "Disputes");

            migrationBuilder.DropColumn(
                name: "TutorStatementAtUtc",
                schema: "moderation",
                table: "Disputes");
        }
    }
}
