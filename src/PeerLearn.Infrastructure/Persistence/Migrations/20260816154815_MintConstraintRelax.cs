using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MintConstraintRelax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditTransactions_TransferLegs",
                schema: "economy",
                table: "CreditTransactions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditTransactions_TransferLegs",
                schema: "economy",
                table: "CreditTransactions",
                sql: "\"Type\" NOT IN ('LessonEarning', 'LessonSpending') OR (\"CounterpartyUserId\" IS NOT NULL AND \"RelatedSessionId\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditTransactions_TransferLegs",
                schema: "economy",
                table: "CreditTransactions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditTransactions_TransferLegs",
                schema: "economy",
                table: "CreditTransactions",
                sql: "\"Type\" NOT IN ('LessonEarning', 'LessonSpending') OR (\"CorrelationId\" IS NOT NULL AND \"CounterpartyUserId\" IS NOT NULL AND \"RelatedSessionId\" IS NOT NULL)");
        }
    }
}
