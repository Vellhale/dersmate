using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PeerLearn.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Çakışan ders saatlerini VERİTABANI seviyesinde engeller.
    ///
    /// NEDEN GEREKLİ: BookSession yalnızca ÖĞRENCİ cüzdanının kilidini alır. İki FARKLI
    /// öğrenci aynı eğitmenin aynı saatine eşzamanlı rezervasyon yaparsa kilit anahtarları
    /// farklıdır, eğitmenin hiçbir satırı yazılmadığı için xmin de devreye girmez ve
    /// ReadCommitted altındaki çakışma sorgusu karşı tarafın commit EDİLMEMİŞ kaydını
    /// göremez — iki rezervasyon da geçer ve eğitmen tek saatte iki derse kilitlenir.
    /// (Bu senaryo eşzamanlılık testiyle fiilen üretildi: tools/e2e-concurrency.ps1 §9.)
    ///
    /// Kilitle çözülemez: anahtarlar meşru şekilde farklıdır. Tek doğru çözüm, kesişimi
    /// atomik olarak reddeden bir EXCLUDE kısıtıdır.
    /// </summary>
    public partial class PreventOverlappingSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,");

            // Yalnızca AKTİF dersler kapsanır; iptal/süresi dolan/tamamlanan dersler
            // geçmişte kalan kayıtlardır ve yeni rezervasyonu engellememelidir.
            migrationBuilder.Sql("""
                ALTER TABLE scheduling."LessonSessions"
                ADD CONSTRAINT "EX_LessonSessions_TutorNoOverlap"
                EXCLUDE USING gist (
                    "TutorUserId" WITH =,
                    tstzrange("ScheduledStartUtc", "ScheduledEndUtc") WITH &&
                ) WHERE ("Status" IN ('Booked', 'AwaitingApproval'));
                """);

            migrationBuilder.Sql("""
                ALTER TABLE scheduling."LessonSessions"
                ADD CONSTRAINT "EX_LessonSessions_StudentNoOverlap"
                EXCLUDE USING gist (
                    "StudentUserId" WITH =,
                    tstzrange("ScheduledStartUtc", "ScheduledEndUtc") WITH &&
                ) WHERE ("Status" IN ('Booked', 'AwaitingApproval'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE scheduling."LessonSessions"
                DROP CONSTRAINT IF EXISTS "EX_LessonSessions_StudentNoOverlap";
                """);
            migrationBuilder.Sql("""
                ALTER TABLE scheduling."LessonSessions"
                DROP CONSTRAINT IF EXISTS "EX_LessonSessions_TutorNoOverlap";
                """);

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,");
        }
    }
}
