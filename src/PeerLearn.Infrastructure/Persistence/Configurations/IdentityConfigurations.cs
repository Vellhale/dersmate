using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", "identity", t =>
        {
            t.HasCheckConstraint("CK_Users_AverageRating", "\"AverageRating\" BETWEEN 0 AND 5");
            t.HasCheckConstraint("CK_Users_RatingCount", "\"RatingCount\" >= 0");

            // Unvan sayacı yalnızca ARTAN bir büyüklük; negatife düşmesi, bir yerde
            // yanlışlıkla geri alım yapıldığının işareti olur ve sessiz kalmamalı.
            t.HasCheckConstraint("CK_Users_TotalEarnedCredits", "\"TotalEarnedCredits\" >= 0");
        });

        builder.HasKey(x => x.Id);

        // citext: "Ali@x.com" ile "ali@x.com" aynı hesaptır; unique index bunu DB seviyesinde garanti eder.
        builder.Property(x => x.Email).HasColumnType("citext").IsRequired();
        builder.HasIndex(x => x.Email).IsUnique();

        builder.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();

        builder.Property(x => x.PhoneNumber).HasMaxLength(20);
        builder.HasIndex(x => x.PhoneNumber)
            .IsUnique()
            .HasFilter("\"PhoneNumber\" IS NOT NULL");

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);

        // Hesaplanan özellik (Role'den türer) — kolon olarak YAZILMAZ, aksi halde
        // ikinci bir doğruluk kaynağı doğardı.
        builder.Ignore(x => x.CanModerate);

        // Yönetim panelindeki "personel" listesi; öğrenciler dışarıda kaldığı için
        // kısmi index milyonlarca satırlık tabloda birkaç satıra iner.
        builder.HasIndex(x => x.Role).HasFilter("\"Role\" <> 'Student'");

        /* Sürüm bir TARİH DİZGESİ ("2026-08-27"), tarih tipi değil: yasal metnin kimliği
           bu ve gelecekte "2027-01-15-b" gibi bir düzeltme sürümü gerekebilir. 20 karakter
           o pay için. */
        builder.Property(x => x.TermsVersion).HasMaxLength(20);

        builder.Property(x => x.Bio).HasMaxLength(1000);
        builder.Property(x => x.AvatarUrl).HasMaxLength(500);
        builder.Property(x => x.AverageRating).HasPrecision(3, 2);

        // Aynı kullanıcıya eşzamanlı yazmalarda (ör. rating güncelleme + statü değişimi) son yazan kazanmasın.
        // uint + IsRowVersion() → Npgsql bunu PostgreSQL'in xmin sistem kolonuna eşler.
        builder.Property(x => x.Version).IsRowVersion();
    }
}

public sealed class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>
{
    public void Configure(EntityTypeBuilder<UserPreference> builder)
    {
        builder.ToTable("UserPreferences", "identity", t =>
        {
            // Rıza verilmişse ne zaman verildiği MUTLAKA bilinmeli — kanıt yükümlülüğü
            // tarihsiz bir onayla karşılanamaz. DB seviyesinde zorlanıyor ki eksik yazan
            // bir kod yolu sessizce geçemesin.
            t.HasCheckConstraint(
                "CK_UserPreferences_ConsentTimestamped",
                "(\"AnalyticsConsent\" = 'NotAsked' AND \"FunctionalConsent\" = 'NotAsked') " +
                "OR (\"ConsentUpdatedAtUtc\" IS NOT NULL AND \"ConsentVersion\" IS NOT NULL)");

            t.HasCheckConstraint("CK_UserPreferences_OnboardingStep", "\"OnboardingLastStep\" >= 0");
        });

        builder.HasKey(x => x.Id);

        // Kullanıcı başına TEK satır.
        builder.HasIndex(x => x.UserId).IsUnique();

        builder.Property(x => x.AnalyticsConsent).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.FunctionalConsent).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.ConsentVersion).HasMaxLength(40);
        builder.Property(x => x.ConsentIpHash).HasMaxLength(64);

        builder.Property(x => x.Version).IsRowVersion();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class UserDeviceConfiguration : IEntityTypeConfiguration<UserDevice>
{
    public void Configure(EntityTypeBuilder<UserDevice> builder)
    {
        builder.ToTable("UserDevices", "identity");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.HwidHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.UserAgent).HasMaxLength(500);

        builder.HasIndex(x => new { x.UserId, x.HwidHash }).IsUnique();

        // Ban kontrolü: giriş sırasında "bu HWID hangi hesaplarda görüldü?" sorgusu.
        builder.HasIndex(x => x.HwidHash);

        builder.HasOne(x => x.User)
            .WithMany(x => x.Devices)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
