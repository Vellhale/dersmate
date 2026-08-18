using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TeacherCandidateDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentContentType",
                schema: "identity",
                table: "TeacherCandidateProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentStorageKey",
                schema: "identity",
                table: "TeacherCandidateProfiles",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DocumentUploadedAtUtc",
                schema: "identity",
                table: "TeacherCandidateProfiles",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocumentContentType",
                schema: "identity",
                table: "TeacherCandidateProfiles");

            migrationBuilder.DropColumn(
                name: "DocumentStorageKey",
                schema: "identity",
                table: "TeacherCandidateProfiles");

            migrationBuilder.DropColumn(
                name: "DocumentUploadedAtUtc",
                schema: "identity",
                table: "TeacherCandidateProfiles");
        }
    }
}
