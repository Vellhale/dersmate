using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EpostaDogrulamaKodu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EmailVerificationAttempts",
                schema: "identity",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerificationCodeExpiresAtUtc",
                schema: "identity",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailVerificationCodeHash",
                schema: "identity",
                table: "Users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerificationCodeSentAtUtc",
                schema: "identity",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailVerificationAttempts",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailVerificationCodeExpiresAtUtc",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailVerificationCodeHash",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailVerificationCodeSentAtUtc",
                schema: "identity",
                table: "Users");
        }
    }
}
