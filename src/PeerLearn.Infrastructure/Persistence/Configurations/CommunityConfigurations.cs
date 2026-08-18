using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeerLearn.Domain.Community;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Infrastructure.Persistence.Configurations;

public sealed class BadgeConfiguration : IEntityTypeConfiguration<Badge>
{
    public void Configure(EntityTypeBuilder<Badge> builder)
    {
        builder.ToTable("Badges", "community");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasConversion<string>().HasMaxLength(40);
        builder.Property(x => x.Name).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Emoji).HasMaxLength(8).IsRequired();

        // Kural motoru rozeti koda göre bulur; iki kayıt aynı kodu taşıyamaz.
        builder.HasIndex(x => x.Code).IsUnique();
    }
}

public sealed class UserBadgeConfiguration : IEntityTypeConfiguration<UserBadge>
{
    public void Configure(EntityTypeBuilder<UserBadge> builder)
    {
        builder.ToTable("UserBadges", "community");

        builder.HasKey(x => x.Id);

        // Aynı rozet bir kullanıcıya iki kez verilemez. Kural motoru idempotent olmalı
        // ama son savunma DB'de: eşzamanlı iki değerlendirme aynı rozeti tetikleyebilir.
        builder.HasIndex(x => new { x.UserId, x.BadgeId }).IsUnique();

        // Profil vitrini: kullanıcının rozetleri kazanım sırasına göre okunur.
        builder.HasIndex(x => new { x.UserId, x.EarnedAtUtc });

        builder.HasOne(x => x.Badge)
            .WithMany()
            .HasForeignKey(x => x.BadgeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TeacherCandidateProfileConfiguration : IEntityTypeConfiguration<TeacherCandidateProfile>
{
    public void Configure(EntityTypeBuilder<TeacherCandidateProfile> builder)
    {
        builder.ToTable("TeacherCandidateProfiles", "identity", t =>
        {
            t.HasCheckConstraint("CK_TeacherCandidate_GradeYear",
                "\"GradeYear\" IS NULL OR \"GradeYear\" BETWEEN 1 AND 6");

            // Doğrulanmış ve reddedilmiş AYNI ANDA olamaz. Handler zaten karşıtını
            // temizliyor; bu kısıt, ileride başka bir yol (elle SQL, yeni bir komut)
            // ikisini birden yazarsa "hem doğrulanmış hem reddedilmiş" bir kaydın
            // sessizce oluşmasını engelleyen son savunma.
            t.HasCheckConstraint("CK_TeacherCandidate_ReviewState",
                "\"VerifiedAtUtc\" IS NULL OR \"RejectedAtUtc\" IS NULL");
        });

        builder.HasKey(x => x.Id);

        // Depo anahtarı ve MIME türü; dosyanın kendisi IProofStorage'da.
        builder.Property(x => x.DocumentStorageKey).HasMaxLength(256);
        builder.Property(x => x.DocumentContentType).HasMaxLength(100);

        // Kullanıcı başına tek beyan.
        builder.HasIndex(x => x.UserId).IsUnique();

        builder.Property(x => x.University).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Faculty).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Department).HasMaxLength(150).IsRequired();
        builder.Property(x => x.ReviewNote).HasMaxLength(500);

        // Hakem kuyruğu: yalnızca karar bekleyenler, en eski beyan başta.
        // Kısmi index — tablonun tamamı değil, kuyruğa girenler indekslenir.
        builder.HasIndex(x => x.DeclaredAtUtc)
            .HasFilter("\"VerifiedAtUtc\" IS NULL AND \"RejectedAtUtc\" IS NULL");

        // Türetilmiş; kolon değil.
        builder.Ignore(x => x.IsVerified);
        builder.Ignore(x => x.IsRejected);
        builder.Ignore(x => x.IsPendingReview);
        builder.Ignore(x => x.ReviewStatus);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class UserSubjectBadgeConfiguration : IEntityTypeConfiguration<UserSubjectBadge>
{
    public void Configure(EntityTypeBuilder<UserSubjectBadge> builder)
    {
        builder.ToTable("UserSubjectBadges", "community", t =>
            // Rozet, o an ölçülen süreyle birlikte kaydedilir; negatif süre bir hesap
            // hatasının sessizce diske yazılması demektir.
            t.HasCheckConstraint("CK_UserSubjectBadges_MinutesAtAward", "\"MinutesAtAward\" >= 0"));

        builder.HasKey(x => x.Id);

        // Branş ve seviye METİN olarak saklanır (proje geneli kural: enum'a üye eklemek
        // göç gerektirmesin, partial index filtreleri okunabilir literal'lerle çalışsın).
        builder.Property(x => x.Branch).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Level).HasConversion<string>().HasMaxLength(20).IsRequired();

        // ÇİFTE ROZETE KARŞI SON SAVUNMA. Motor idempotent ama iki ders onayı aynı anda
        // biterse ikisi de aynı eşiği aşmış görüp aynı rozeti yazmaya kalkabilir.
        builder.HasIndex(x => new { x.UserId, x.Branch, x.Level }).IsUnique();

        // Profil okuması: kullanıcının tüm branş rozetleri, kazanım sırasıyla.
        builder.HasIndex(x => new { x.UserId, x.EarnedAtUtc });

        // Cascade: kullanıcı silinirse rozeti de gider. UserBadges ile aynı davranış —
        // katalog satırı değil, kullanıcıya ait türetilmiş kayıt.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
