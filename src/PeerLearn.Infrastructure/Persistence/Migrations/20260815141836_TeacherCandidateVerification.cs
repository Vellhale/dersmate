using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TeacherCandidateVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAtUtc",
                schema: "identity",
                table: "TeacherCandidateProfiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RejectedByAdminId",
                schema: "identity",
                table: "TeacherCandidateProfiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                schema: "identity",
                table: "TeacherCandidateProfiles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeacherCandidateProfiles_DeclaredAtUtc",
                schema: "identity",
                table: "TeacherCandidateProfiles",
                column: "DeclaredAtUtc",
                filter: "\"VerifiedAtUtc\" IS NULL AND \"RejectedAtUtc\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TeacherCandidate_ReviewState",
                schema: "identity",
                table: "TeacherCandidateProfiles",
                sql: "\"VerifiedAtUtc\" IS NULL OR \"RejectedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TeacherCandidateProfiles_DeclaredAtUtc",
                schema: "identity",
                table: "TeacherCandidateProfiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TeacherCandidate_ReviewState",
                schema: "identity",
                table: "TeacherCandidateProfiles");

            migrationBuilder.DropColumn(
                name: "RejectedAtUtc",
                schema: "identity",
                table: "TeacherCandidateProfiles");

            migrationBuilder.DropColumn(
                name: "RejectedByAdminId",
                schema: "identity",
                table: "TeacherCandidateProfiles");

            migrationBuilder.DropColumn(
                name: "ReviewNote",
                schema: "identity",
                table: "TeacherCandidateProfiles");
        }
    }
}
