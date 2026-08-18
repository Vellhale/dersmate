using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowZeroCreditSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LessonSessions_CreditCost",
                schema: "scheduling",
                table: "LessonSessions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LessonSessions_CreditCost",
                schema: "scheduling",
                table: "LessonSessions",
                sql: "\"CreditCost\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LessonSessions_CreditCost",
                schema: "scheduling",
                table: "LessonSessions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LessonSessions_CreditCost",
                schema: "scheduling",
                table: "LessonSessions",
                sql: "\"CreditCost\" > 0");
        }
    }
}
