using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveEscrowMechanics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            /*
              ÖNCE VERİ, SONRA ŞEMA — sıra zorunlu.

              Kaldırılan kolonlara BAĞLI satırlar var: 9 hold tüketimi, 4 iade ve bir adet
              LessonSpending hareketi. Şema önce değiştirilseydi CreditTransactionId NOT NULL
              olurken hold satırları onu boş taşıdığı için göç patlardı.

              DEĞİŞMEZLER KORUNUYOR (üçü de sonda sınanıyor):
                lot   : InitialAmount − RemainingAmount == SUM(tüketim)
                cüzdan: AvailableBalance == SUM(lot.RemainingAmount)
                defter: SUM(cüzdan) == SUM(hareket)

              GERİYE DÖNÜK İADE. Eski ekonomide öğrenciden düşülen puanlar vardı; "ders almak
              ücretsizdir" kuralı bugün geçerli olduğuna göre o düşümler bugünün kurallarına
              göre hiç olmamalıydı. Bu yüzden silinen harcama satırlarının karşılığı lota ve
              cüzdana GERİ VERİLİYOR — sessizce yok sayılsaydı kullanıcı bir puan kaybederdi.
            */
            migrationBuilder.Sql(@"
                -- 1. Hold'a bağlı tüketim izleri (tüketim + iade) gider.
                DELETE FROM economy.""CreditLotConsumptions"" WHERE ""CreditHoldId"" IS NOT NULL;

                -- 2. Öğrenci harcama hareketleri gider (artık üretilmiyor, geçmişte kaldı).
                DELETE FROM economy.""CreditTransactions"" WHERE ""Type"" = 'LessonSpending';

                -- 3. Lotlar kalan tüketimlerine göre yeniden hesaplanır (iade burada gerçekleşir).
                UPDATE economy.""CreditLots"" l
                SET ""RemainingAmount"" = l.""InitialAmount"" - COALESCE((
                        SELECT SUM(k.""Amount"") FROM economy.""CreditLotConsumptions"" k
                        WHERE k.""CreditLotId"" = l.""Id""), 0);

                -- 4. Cüzdan bakiyesi lot toplamına eşitlenir, bloke sıfırlanır.
                UPDATE economy.""Wallets"" w
                SET ""AvailableBalance"" = COALESCE((
                        SELECT SUM(l.""RemainingAmount"") FROM economy.""CreditLots"" l
                        WHERE l.""WalletId"" = w.""Id""), 0),
                    ""LockedBalance"" = 0;
            ");

            migrationBuilder.Sql(@"
                DO $$
                DECLARE lot int; cuzdan int; defter int;
                BEGIN
                  SELECT COUNT(*) INTO lot FROM economy.""CreditLots"" l
                    WHERE l.""InitialAmount"" - l.""RemainingAmount"" <> COALESCE((
                      SELECT SUM(k.""Amount"") FROM economy.""CreditLotConsumptions"" k
                      WHERE k.""CreditLotId"" = l.""Id""), 0);
                  IF lot > 0 THEN RAISE EXCEPTION 'IPTAL: % lotta tuketim toplami tutmuyor', lot; END IF;

                  SELECT COUNT(*) INTO cuzdan FROM economy.""Wallets"" w
                    WHERE w.""AvailableBalance"" <> COALESCE((
                      SELECT SUM(l.""RemainingAmount"") FROM economy.""CreditLots"" l
                      WHERE l.""WalletId"" = w.""Id""), 0);
                  IF cuzdan > 0 THEN RAISE EXCEPTION 'IPTAL: % cuzdan lot toplamiyla uyusmuyor', cuzdan; END IF;

                  SELECT (SELECT COALESCE(SUM(""AvailableBalance""),0) FROM economy.""Wallets"")
                       - (SELECT COALESCE(SUM(""Amount""),0) FROM economy.""CreditTransactions"") INTO defter;
                  IF defter <> 0 THEN RAISE EXCEPTION 'IPTAL: defter sapmasi %', defter; END IF;
                END $$;
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_CreditLotConsumptions_CreditHolds_CreditHoldId",
                schema: "economy",
                table: "CreditLotConsumptions");

            migrationBuilder.DropTable(
                name: "CreditHolds",
                schema: "economy");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Wallets_LockedBalance",
                schema: "economy",
                table: "Wallets");

            migrationBuilder.DropIndex(
                name: "IX_CreditTransactions_RelatedSessionId_Type",
                schema: "economy",
                table: "CreditTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditTransactions_TransferLegs",
                schema: "economy",
                table: "CreditTransactions");

            migrationBuilder.DropIndex(
                name: "IX_CreditLotConsumptions_CreditHoldId_CreditLotId_IsReversal",
                schema: "economy",
                table: "CreditLotConsumptions");

            migrationBuilder.DropIndex(
                name: "IX_CreditLotConsumptions_CreditTransactionId_CreditLotId",
                schema: "economy",
                table: "CreditLotConsumptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditLotConsumptions_ReversalRequiresHold",
                schema: "economy",
                table: "CreditLotConsumptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditLotConsumptions_SingleParent",
                schema: "economy",
                table: "CreditLotConsumptions");

            migrationBuilder.DropColumn(
                name: "LockedBalance",
                schema: "economy",
                table: "Wallets");

            migrationBuilder.DropColumn(
                name: "CreditHoldId",
                schema: "economy",
                table: "CreditLotConsumptions");

            migrationBuilder.DropColumn(
                name: "IsReversal",
                schema: "economy",
                table: "CreditLotConsumptions");

            migrationBuilder.AlterColumn<Guid>(
                name: "CreditTransactionId",
                schema: "economy",
                table: "CreditLotConsumptions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditTransactions_RelatedSessionId_Type",
                schema: "economy",
                table: "CreditTransactions",
                columns: new[] { "RelatedSessionId", "Type" },
                unique: true,
                filter: "\"Type\" = 'LessonEarning'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditTransactions_TransferLegs",
                schema: "economy",
                table: "CreditTransactions",
                sql: "\"Type\" <> 'LessonEarning' OR (\"CounterpartyUserId\" IS NOT NULL AND \"RelatedSessionId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLotConsumptions_CreditTransactionId_CreditLotId",
                schema: "economy",
                table: "CreditLotConsumptions",
                columns: new[] { "CreditTransactionId", "CreditLotId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CreditTransactions_RelatedSessionId_Type",
                schema: "economy",
                table: "CreditTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditTransactions_TransferLegs",
                schema: "economy",
                table: "CreditTransactions");

            migrationBuilder.DropIndex(
                name: "IX_CreditLotConsumptions_CreditTransactionId_CreditLotId",
                schema: "economy",
                table: "CreditLotConsumptions");

            migrationBuilder.AddColumn<int>(
                name: "LockedBalance",
                schema: "economy",
                table: "Wallets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<Guid>(
                name: "CreditTransactionId",
                schema: "economy",
                table: "CreditLotConsumptions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "CreditHoldId",
                schema: "economy",
                table: "CreditLotConsumptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReversal",
                schema: "economy",
                table: "CreditLotConsumptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CreditHolds",
                schema: "economy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    WalletId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditHolds", x => x.Id);
                    table.CheckConstraint("CK_CreditHolds_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_CreditHolds_LessonSessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "scheduling",
                        principalTable: "LessonSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditHolds_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalSchema: "economy",
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Wallets_LockedBalance",
                schema: "economy",
                table: "Wallets",
                sql: "\"LockedBalance\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_CreditTransactions_RelatedSessionId_Type",
                schema: "economy",
                table: "CreditTransactions",
                columns: new[] { "RelatedSessionId", "Type" },
                unique: true,
                filter: "\"Type\" IN ('LessonSpending', 'LessonEarning')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditTransactions_TransferLegs",
                schema: "economy",
                table: "CreditTransactions",
                sql: "\"Type\" NOT IN ('LessonEarning', 'LessonSpending') OR (\"CounterpartyUserId\" IS NOT NULL AND \"RelatedSessionId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLotConsumptions_CreditHoldId_CreditLotId_IsReversal",
                schema: "economy",
                table: "CreditLotConsumptions",
                columns: new[] { "CreditHoldId", "CreditLotId", "IsReversal" },
                unique: true,
                filter: "\"CreditHoldId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CreditLotConsumptions_CreditTransactionId_CreditLotId",
                schema: "economy",
                table: "CreditLotConsumptions",
                columns: new[] { "CreditTransactionId", "CreditLotId" },
                unique: true,
                filter: "\"CreditTransactionId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditLotConsumptions_ReversalRequiresHold",
                schema: "economy",
                table: "CreditLotConsumptions",
                sql: "NOT \"IsReversal\" OR \"CreditHoldId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditLotConsumptions_SingleParent",
                schema: "economy",
                table: "CreditLotConsumptions",
                sql: "num_nonnulls(\"CreditHoldId\", \"CreditTransactionId\") = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CreditHolds_SessionId",
                schema: "economy",
                table: "CreditHolds",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditHolds_WalletId_Status",
                schema: "economy",
                table: "CreditHolds",
                columns: new[] { "WalletId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_CreditLotConsumptions_CreditHolds_CreditHoldId",
                schema: "economy",
                table: "CreditLotConsumptions",
                column: "CreditHoldId",
                principalSchema: "economy",
                principalTable: "CreditHolds",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
