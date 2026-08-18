using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PointsEconomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LessonSessions_Duration",
                schema: "scheduling",
                table: "LessonSessions");

            migrationBuilder.AddColumn<int>(
                name: "TotalEarnedCredits",
                schema: "identity",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsVolunteer",
                schema: "scheduling",
                table: "LessonSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<DateTime>(
                name: "ExpiresAtUtc",
                schema: "economy",
                table: "CreditLots",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_TotalEarnedCredits",
                schema: "identity",
                table: "Users",
                sql: "\"TotalEarnedCredits\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LessonSessions_Duration",
                schema: "scheduling",
                table: "LessonSessions",
                sql: "\"DurationMinutes\" IN (30, 60)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LessonSessions_VolunteerNoReward",
                schema: "scheduling",
                table: "LessonSessions",
                sql: "NOT \"IsVolunteer\" OR \"CreditCost\" = 0");

            // ================= VERİ GÖÇÜ =================
            // Şema değişikliği tek başına yetmez: aşağıdaki üç adım yapılmazsa sistem
            // sessizce yanlış davranır (unvanlar sıfırlanır, gönüllülük kaybolur,
            // escrow'daki krediler sonsuza kadar bloke kalır).

            /*
              1) UNVAN SAYACININ GERİYE DÖNÜK DOLDURULMASI.

              TotalEarnedCredits 0 varsayılanıyla eklendi. Doldurulmazsa geçmişte ders
              anlatmış HERKES sıfır puanla "🌱 Çırak" olur — kazanılmış unvanların toplu
              olarak silinmesi demek. Kaynak, defterdeki LessonEarning hareketleri:
              cüzdan bakiyesi değil, çünkü bakiye vade yakımıyla azalmış olabilir ve
              unvan geriye gitmemeli.
            */
            migrationBuilder.Sql(@"
                UPDATE identity.""Users"" u
                SET ""TotalEarnedCredits"" = k.toplam
                FROM (
                    SELECT w.""UserId"" AS user_id, SUM(t.""Amount"")::int AS toplam
                    FROM economy.""CreditTransactions"" t
                    JOIN economy.""Wallets"" w ON w.""Id"" = t.""WalletId""
                    WHERE t.""Type"" = 'LessonEarning'
                    GROUP BY w.""UserId""
                ) k
                WHERE u.""Id"" = k.user_id;
            ");

            /*
              2) GÖNÜLLÜLÜK BAYRAĞININ AYRIŞTIRILMASI.

              Eski modelde "gönüllü" ile "0 kredi" aynı şeydi ve gönüllülük CreditCost'tan
              çıkarılıyordu. Bayrak doldurulmazsa gönüllü ders vermiş eğitmenlerin sayacı
              (öğretmen adaylığı incelemesinde kullanılıyor) sıfırlanır ve verilmiş emek
              görünmez olur.
            */
            migrationBuilder.Sql(@"
                UPDATE scheduling.""LessonSessions""
                SET ""IsVolunteer"" = TRUE
                WHERE ""CreditCost"" = 0;
            ");

            /*
              3) AÇIK ESCROW'LARIN ÇÖZÜLMESİ — bu adım atlanırsa sessiz ve kalıcı hasar.

              Escrow yazan/çözen tüm kod yolları kaldırıldı. Hâlihazırda Status='Active'
              olan hold satırlarını bundan sonra çözecek HİÇBİR ŞEY yok: o öğrencilerin
              LockedBalance'ı sonsuza kadar sıfırdan büyük kalır, lotlarındaki kredi geri
              dönmez ve Wallet değişmezi (LockedBalance == aktif hold toplamı) kalıcı
              olarak ihlal edilmiş olur. Ne hata, ne log, ne test bunu gösterir.

              Aşağıdaki üç ifade, silinen ReleaseHoldAsync'in tam olarak yaptığı işi toplu
              hâlde yapar: ters tüketim satırları, lot iadesi, cüzdan düzeltmesi.
              SIRA ÖNEMLİ: lotlar iade edilmeden hold kapatılırsa kredi buharlaşır.
            */
            migrationBuilder.Sql(@"
                -- 3a) Ters tüketim satırları (denetim izi korunur, mevcut satırlar değişmez).
                INSERT INTO economy.""CreditLotConsumptions""
                    (""Id"", ""CreditLotId"", ""CreditHoldId"", ""Amount"", ""IsReversal"", ""CreatedAtUtc"")
                SELECT gen_random_uuid(), c.""CreditLotId"", c.""CreditHoldId"", c.""Amount"", TRUE, now()
                FROM economy.""CreditLotConsumptions"" c
                JOIN economy.""CreditHolds"" h ON h.""Id"" = c.""CreditHoldId""
                WHERE h.""Status"" = 'Active' AND NOT c.""IsReversal"";

                -- 3b) Lotlara iade.
                UPDATE economy.""CreditLots"" l
                SET ""RemainingAmount"" = l.""RemainingAmount"" + i.iade
                FROM (
                    SELECT c.""CreditLotId"" AS lot_id, SUM(c.""Amount"")::int AS iade
                    FROM economy.""CreditLotConsumptions"" c
                    JOIN economy.""CreditHolds"" h ON h.""Id"" = c.""CreditHoldId""
                    WHERE h.""Status"" = 'Active' AND NOT c.""IsReversal""
                    GROUP BY c.""CreditLotId""
                ) i
                WHERE l.""Id"" = i.lot_id;

                -- 3c) Cüzdanda Locked -> Available ve hold'un kapatılması.
                UPDATE economy.""Wallets"" w
                SET ""LockedBalance"" = w.""LockedBalance"" - b.tutar,
                    ""AvailableBalance"" = w.""AvailableBalance"" + b.tutar,
                    ""UpdatedAtUtc"" = now()
                FROM (
                    SELECT h.""WalletId"" AS wallet_id, SUM(h.""Amount"")::int AS tutar
                    FROM economy.""CreditHolds"" h
                    WHERE h.""Status"" = 'Active'
                    GROUP BY h.""WalletId""
                ) b
                WHERE w.""Id"" = b.wallet_id;

                UPDATE economy.""CreditHolds""
                SET ""Status"" = 'Released', ""ResolvedAtUtc"" = now()
                WHERE ""Status"" = 'Active';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_TotalEarnedCredits",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LessonSessions_Duration",
                schema: "scheduling",
                table: "LessonSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LessonSessions_VolunteerNoReward",
                schema: "scheduling",
                table: "LessonSessions");

            migrationBuilder.DropColumn(
                name: "TotalEarnedCredits",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsVolunteer",
                schema: "scheduling",
                table: "LessonSessions");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ExpiresAtUtc",
                schema: "economy",
                table: "CreditLots",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_LessonSessions_Duration",
                schema: "scheduling",
                table: "LessonSessions",
                sql: "\"DurationMinutes\" BETWEEN 30 AND 180");
        }
    }
}
