using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Başarı rozetlerinin emekliliği: FirstLesson, FiveStarTeacher, FastResponder,
    /// TenHoursTraded, VolunteerTutor. Yerlerini unvan sistemi aldı. 🌱 FutureTeacher
    /// KALIR — o bir başarı değil, öğretmen adaylığı beyanına bağlı bir kimlik işareti.
    /// </summary>
    /// <remarks>
    /// SIRA ZORUNLU: VERİ → KATALOG → (kodda) ENUM.
    ///
    /// <c>Badges.Code</c> veritabanında METİN olarak saklanıyor. Enum üyesi silinip veri
    /// yerinde bırakılsaydı, EF 'FirstLesson' dizesini karşılığı olmayan bir enum'a
    /// dönüştürmeye çalışır ve rozet okuyan HER sorgu patlardı. Bu göç, uygulama açılışında
    /// tohumlamadan ve ilk sorgudan ÖNCE koştuğu için (bkz. Program.cs) yeni kod bayat
    /// satırlarla hiç karşılaşmaz.
    ///
    /// Kodlar burada DİZE olarak yazılı, enum sabiti olarak değil — kasıtlı: bu satırların
    /// anlamı göçün yazıldığı andaki şemadır ve enum'un bugünkü hâline bağlanmamalıdır.
    /// (Enum sabiti kullansaydım, üyeler silindiği an bu göç derlenmezdi.)
    /// </remarks>
    public partial class RetireAchievementBadges : Migration
    {
        private const string EmekliKodlar =
            "'FirstLesson', 'FiveStarTeacher', 'FastResponder', 'TenHoursTraded', 'VolunteerTutor'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) VERİ: kullanıcılara verilmiş satırlar. Katalogtan ÖNCE — FK Restrict.
            migrationBuilder.Sql($"""
                DELETE FROM community."UserBadges" ub
                USING community."Badges" b
                WHERE ub."BadgeId" = b."Id" AND b."Code" IN ({EmekliKodlar});
                """);

            // 2) KATALOG
            migrationBuilder.Sql($"""
                DELETE FROM community."Badges" WHERE "Code" IN ({EmekliKodlar});
                """);

            // 3) Vitrin alanı: seçilecek tek rozet kalınca işlevsiz kaldı.
            migrationBuilder.DropColumn(
                name: "IsFeatured",
                schema: "community",
                table: "UserBadges");
        }

        /// <summary>
        /// GERİ ALMA EKSİKTİR VE ÖYLE OLMALI: kolon geri gelir, SİLİNEN ROZETLER GELMEZ.
        /// Kullanıcı-rozet satırları türetilmiş veridir (kural motoru mevcut veriden yeniden
        /// hesaplar), ama bu göçle birlikte o kurallar da koddan kalktığı için geri
        /// hesaplanacak bir şey yok. Sahte satır üretmektense kaybı açıkça bildirmek doğru.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFeatured",
                schema: "community",
                table: "UserBadges",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
