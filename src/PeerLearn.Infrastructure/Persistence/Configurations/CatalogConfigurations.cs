using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeerLearn.Domain.Catalog;

namespace PeerLearn.Infrastructure.Persistence.Configurations;

public sealed class EducationCategoryConfiguration : IEntityTypeConfiguration<EducationCategory>
{
    public void Configure(EntityTypeBuilder<EducationCategory> builder)
    {
        builder.ToTable("EducationCategories", "catalog");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(120).IsRequired();

        // Slug tüm ağaçta benzersizdir; URL ve seed verisi için doğal anahtar görevi görür.
        builder.HasIndex(x => x.Slug).IsUnique();

        builder.HasOne(x => x.ParentCategory)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SubjectConfiguration : IEntityTypeConfiguration<Subject>
{
    public void Configure(EntityTypeBuilder<Subject> builder)
    {
        builder.ToTable("Subjects", "catalog");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();

        builder.HasIndex(x => new { x.CategoryId, x.Name }).IsUnique();

        // Branş METİN olarak saklanır (proje geneli kural). NULL serbest: sekiz branşın
        // dışında kalmış eski katalog satırları bu alanı boş bırakır ve rozet motoru
        // onları saymaz — ama satır silinmediği için geçmiş ders kayıtları kırılmaz.
        builder.Property(x => x.Branch).HasConversion<string>().HasMaxLength(20);

        // Rozet motoru "bu eğitmenin tamamladığı dersler hangi branşa düşüyor" diye sorar;
        // yol Topic → Subject → Branch. Filtreli index, sorgunun WHERE'i "Branch IS NOT NULL"
        // içerdiği için kullanılabilir (bkz. docs/ASAMA-1-MIMARI.md partial index kuralı).
        builder.HasIndex(x => x.Branch).HasFilter("\"Branch\" IS NOT NULL");

        builder.HasOne(x => x.Category)
            .WithMany(x => x.Subjects)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TopicConfiguration : IEntityTypeConfiguration<Topic>
{
    public void Configure(EntityTypeBuilder<Topic> builder)
    {
        builder.ToTable("Topics", "catalog");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();

        // Konu adı, ders içinde (alt konu seviyesinden bağımsız) benzersizdir.
        builder.HasIndex(x => new { x.SubjectId, x.Name }).IsUnique();

        builder.HasOne(x => x.Subject)
            .WithMany(x => x.Topics)
            .HasForeignKey(x => x.SubjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ParentTopic)
            .WithMany(x => x.SubTopics)
            .HasForeignKey(x => x.ParentTopicId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
