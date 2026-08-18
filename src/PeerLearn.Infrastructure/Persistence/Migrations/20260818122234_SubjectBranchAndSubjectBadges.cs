using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SubjectBranchAndSubjectBadges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Branch",
                schema: "catalog",
                table: "Subjects",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserSubjectBadges",
                schema: "community",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Branch = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EarnedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MinutesAtAward = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSubjectBadges", x => x.Id);
                    table.CheckConstraint("CK_UserSubjectBadges_MinutesAtAward", "\"MinutesAtAward\" >= 0");
                    table.ForeignKey(
                        name: "FK_UserSubjectBadges_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_Branch",
                schema: "catalog",
                table: "Subjects",
                column: "Branch",
                filter: "\"Branch\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubjectBadges_UserId_Branch_Level",
                schema: "community",
                table: "UserSubjectBadges",
                columns: new[] { "UserId", "Branch", "Level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSubjectBadges_UserId_EarnedAtUtc",
                schema: "community",
                table: "UserSubjectBadges",
                columns: new[] { "UserId", "EarnedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserSubjectBadges",
                schema: "community");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_Branch",
                schema: "catalog",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "Branch",
                schema: "catalog",
                table: "Subjects");
        }
    }
}
