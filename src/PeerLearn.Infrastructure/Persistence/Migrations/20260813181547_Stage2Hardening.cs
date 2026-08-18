using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage2Hardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDuplicateHash",
                schema: "scheduling",
                table: "SessionProofs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "economy",
                table: "CreditHolds",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDuplicateHash",
                schema: "scheduling",
                table: "SessionProofs");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "economy",
                table: "CreditHolds");
        }
    }
}
