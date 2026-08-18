using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeerLearn.Domain.Catalog;
using PeerLearn.Domain.Identity;
using PeerLearn.Domain.Matchmaking;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Infrastructure.Persistence.Configurations;

public sealed class LessonSessionConfiguration : IEntityTypeConfiguration<LessonSession>
{
    public void Configure(EntityTypeBuilder<LessonSession> builder)
    {
        builder.ToTable("LessonSessions", "scheduling", t =>
        {
            /*
              SÜRE BİR KÜME, ARALIK DEĞİL.

              Eski kısıt "BETWEEN 30 AND 180" idi ve 45 dakika gibi ara değerlere izin
              veriyordu. Ödül 30 dakikalık bloklar üzerinden hesaplandığı için ara değer
              yuvarlama kararı gerektirir; kümeye indirerek o kararı hiç doğmadan kaldırıyoruz.

              GÖÇ GÜVENLİĞİ: daraltmadan önce mevcut veri sayıldı — 887 dersin tamamı 60
              dakika. Aralık kısıtını daraltmak, dışarıda kalan tek satır olsa migration'ı
              düşürürdü.
            */
            t.HasCheckConstraint("CK_LessonSessions_Duration", "\"DurationMinutes\" IN (30, 60)");

            // 0'a İZİN VERİLİR: gönüllü derste eğitmene puan basılmaz.
            // Negatif yasak — negatif ödül, defterde ters yönlü bir basım demek olurdu.
            t.HasCheckConstraint("CK_LessonSessions_CreditCost", "\"CreditCost\" >= 0");

            // Gönüllü ders puan üretemez. Bayrak ile tutar birbirinden bağımsız yazıldığı
            // için ikisinin çelişmesi mümkün; çelişki sessizce yaşamasın diye DB'de yasak.
            t.HasCheckConstraint("CK_LessonSessions_VolunteerNoReward",
                "NOT \"IsVolunteer\" OR \"CreditCost\" = 0");
            t.HasCheckConstraint("CK_LessonSessions_DifferentUsers", "\"TutorUserId\" <> \"StudentUserId\"");
            t.HasCheckConstraint("CK_LessonSessions_EndAfterStart", "\"ScheduledEndUtc\" > \"ScheduledStartUtc\"");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.VerificationCode).HasMaxLength(12).IsRequired();
        builder.HasIndex(x => x.VerificationCode).IsUnique();

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.CancelReason).HasMaxLength(500);

        // Takvim ekranları.
        builder.HasIndex(x => new { x.TutorUserId, x.ScheduledStartUtc });
        builder.HasIndex(x => new { x.StudentUserId, x.ScheduledStartUtc });

        // Background job'lar: süresi geçen Booked dersler (Expired), onay bekleyenlerin otomatik onayı.
        builder.HasIndex(x => new { x.Status, x.ScheduledEndUtc });

        // Durum makinesi geçişlerinde (tamamla/onayla/iptal) çifte tetiklemeyi engeller.
        builder.Property(x => x.Version).IsRowVersion();

        builder.HasOne<Match>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.TutorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.StudentUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Topic>().WithMany().HasForeignKey(x => x.TopicId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SessionProofConfiguration : IEntityTypeConfiguration<SessionProof>
{
    public void Configure(EntityTypeBuilder<SessionProof> builder)
    {
        builder.ToTable("SessionProofs", "scheduling", t =>
            t.HasCheckConstraint("CK_SessionProofs_FileSize", "\"FileSizeBytes\" > 0 AND \"FileSizeBytes\" <= 10485760"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.StorageKey).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Sha256Hash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(x => x.SessionId);

        // Sahte kanıt avı: aynı hash'in birden çok derste kullanılması şüphelidir (unique DEĞİL, sinyal index'i).
        builder.HasIndex(x => x.Sha256Hash);

        // Bakım işi "içeriği hâlâ duran, yaşı geçmiş" kanıtları arıyor; kısmi index tam bu
        // sorgu içindir ve silinmiş kayıtlar (zamanla çoğunluk olacaklar) index'e hiç girmez.
        builder.HasIndex(x => x.CreatedAtUtc)
            .HasFilter("\"ContentDeletedAtUtc\" IS NULL")
            .HasDatabaseName("IX_SessionProofs_Retention");

        builder.HasOne<LessonSession>()
            .WithMany(x => x.Proofs)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SessionReviewConfiguration : IEntityTypeConfiguration<SessionReview>
{
    public void Configure(EntityTypeBuilder<SessionReview> builder)
    {
        builder.ToTable("SessionReviews", "scheduling", t =>
        {
            // Üç puanın da 1-5 olması DB'de zorlanır: puan ortalaması ürünün en görünür
            // sayısı ve aralık dışı tek bir satır onu kalıcı olarak bozar.
            t.HasCheckConstraint("CK_SessionReviews_Score", "\"Score\" BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_SessionReviews_TeachingScore", "\"TeachingScore\" BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_SessionReviews_PunctualityScore", "\"PunctualityScore\" BETWEEN 1 AND 5");

            // Kendi kendini puanlama engeli.
            t.HasCheckConstraint("CK_SessionReviews_NotSelf", "\"ReviewerUserId\" <> \"RevieweeUserId\"");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Comment).HasMaxLength(1000);

        // Her taraf bir dersi bir kez puanlar.
        builder.HasIndex(x => new { x.SessionId, x.ReviewerUserId }).IsUnique();

        // Profildeki yorum listesi: en yeniden eskiye.
        builder.HasIndex(x => new { x.RevieweeUserId, x.CreatedAtUtc }).IsDescending(false, true);

        builder.HasOne<LessonSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ReviewerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.RevieweeUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SweepFailureConfiguration : IEntityTypeConfiguration<SweepFailure>
{
    public void Configure(EntityTypeBuilder<SweepFailure> builder)
    {
        builder.ToTable("SweepFailures", "scheduling", t =>
            t.HasCheckConstraint("CK_SweepFailures_Count", "\"FailureCount\" > 0"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.RecordType).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.LastError).HasMaxLength(500).IsRequired();

        // Bir kayıt için TEK satır. İki instance aynı anda hata yazmaya çalışırsa
        // ikincisi burada düşer ve süpürme "bu tur ertelenemedi" deyip devam eder —
        // sayaç kaybı, mükerrer satırın yaratacağı belirsizlikten iyidir.
        builder.HasIndex(x => new { x.RecordType, x.RecordId }).IsUnique();

        // Süpürme sorgularının sol birleşimi bu index üzerinden gider.
        builder.HasIndex(x => x.NextAttemptAtUtc);

        // FK YOK: RecordId farklı tablolara (şimdilik yalnızca LessonSessions) işaret eder.
        // Kayıt elle silinse bile hata satırı yetim kalmaz — bakım işi yaşa göre temizler.
    }
}

public sealed class SessionReviewTagConfiguration : IEntityTypeConfiguration<SessionReviewTag>
{
    public void Configure(EntityTypeBuilder<SessionReviewTag> builder)
    {
        builder.ToTable("SessionReviewTags", "scheduling");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Tag).HasConversion<string>().HasMaxLength(40);

        // Aynı etiket bir yorumda iki kez sayılmasın (dağılım grafiğini bozar).
        builder.HasIndex(x => new { x.ReviewId, x.Tag }).IsUnique();

        builder.HasOne(x => x.Review)
            .WithMany(x => x.Tags)
            .HasForeignKey(x => x.ReviewId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
