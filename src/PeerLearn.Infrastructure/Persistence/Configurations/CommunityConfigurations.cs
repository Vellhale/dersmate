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

/*
  ══════════════════════════════════════════════════════════════════════════════
  FORUM YAPILANDIRMALARI (2026-08-27).

  Üç tablo da `community` şemasında: rozetlerle aynı modül, aynı sınır.
  ══════════════════════════════════════════════════════════════════════════════
*/

public sealed class CommunityPostConfiguration : IEntityTypeConfiguration<CommunityPost>
{
    public void Configure(EntityTypeBuilder<CommunityPost> builder)
    {
        builder.ToTable("Posts", "community", t =>
        {
            // Sayaçlar denormalize; negatif bir sayaç, oy yolunda bir hesap hatasının
            // sessizce diske yazılması demektir. Son savunma hattı burada.
            t.HasCheckConstraint("CK_Posts_Counters", "\"UpvoteCount\" >= 0 AND \"DownvoteCount\" >= 0 " +
                                                      "AND \"CommentCount\" >= 0 AND \"ReportCount\" >= 0");
        });

        builder.HasKey(x => x.Id);

        // Enum'lar METİN (CLAUDE.md): üye eklemek göç gerektirmesin, kısmi index
        // filtreleri okunabilir literal'lerle yazılabilsin.
        builder.Property(x => x.Tag).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(x => x.Title).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(2000).IsRequired();

        builder.Property(x => x.Version).IsRowVersion();

        /*
          AKIŞIN ANA SORGUSU: görünür gönderiler, tarihe göre.

          KISMİ index (HasFilter): akış yalnızca Visible içeriği listeliyor ve
          kaldırılmış/incelemedeki satırlar zamanla birikiyor. Ama CLAUDE.md'nin
          uyardığı tuzak burada geçerli: filtreli index, sorgunun WHERE'i o koşulu
          BİREBİR içermedikçe kullanılmaz. Akış sorgusu bu yüzden
          `Status == ForumContentStatus.Visible` yazmak ZORUNDA — "Status != Removed"
          gibi bir yazım index'i sessizce devre dışı bırakır.
        */
        builder.HasIndex(x => x.CreatedAtUtc)
            .HasFilter("\"Status\" = 'Visible'")
            .HasDatabaseName("IX_Posts_GorunurTarih");

        // Etiket filtresi + tarih: filtre şeridinin sorgusu.
        builder.HasIndex(x => new { x.Tag, x.CreatedAtUtc })
            .HasFilter("\"Status\" = 'Visible'")
            .HasDatabaseName("IX_Posts_GorunurEtiketTarih");

        // Kullanıcının günlük gönderi tavanı sayımı (ForumRules.DailyPostLimit).
        builder.HasIndex(x => new { x.AuthorUserId, x.CreatedAtUtc });

        // Yazar silinirse gönderileri de gider: kullanıcıya ait türetilmiş içerik.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.AuthorUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CommunityCommentConfiguration : IEntityTypeConfiguration<CommunityComment>
{
    public void Configure(EntityTypeBuilder<CommunityComment> builder)
    {
        builder.ToTable("Comments", "community", t =>
        {
            t.HasCheckConstraint("CK_Comments_Counters", "\"UpvoteCount\" >= 0 AND \"DownvoteCount\" >= 0 " +
                                                         "AND \"ReportCount\" >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Version).IsRowVersion();

        // Bir gönderinin yorumları, yazılma sırasıyla.
        builder.HasIndex(x => new { x.PostId, x.CreatedAtUtc })
            .HasFilter("\"Status\" = 'Visible'")
            .HasDatabaseName("IX_Comments_GorunurGonderiTarih");

        builder.HasOne<CommunityPost>()
            .WithMany()
            .HasForeignKey(x => x.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.AuthorUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CommunityVoteConfiguration : IEntityTypeConfiguration<CommunityVote>
{
    public void Configure(EntityTypeBuilder<CommunityVote> builder)
    {
        builder.ToTable("Votes", "community", t =>
        {
            // Oy yalnızca +1 ya da -1. Sıfır YAZILMAZ: oyu geri almak satırı silmektir.
            t.HasCheckConstraint("CK_Votes_Value", "\"Value\" IN (-1, 1)");

            // PostId ve CommentId'den TAM OLARAK biri dolu olmalı. Bu kısıt olmadan
            // ikisi de NULL olan (hiçbir şeye ait olmayan) ya da ikisi de dolu olan
            // (iki içeriğe birden sayılan) bir oy satırı yazılabilirdi.
            t.HasCheckConstraint("CK_Votes_TekHedef",
                "(\"PostId\" IS NOT NULL AND \"CommentId\" IS NULL) OR " +
                "(\"PostId\" IS NULL AND \"CommentId\" IS NOT NULL)");
        });

        builder.HasKey(x => x.Id);

        /*
          ⚠️ İKİ AYRI KISMİ UNIQUE — tek bir (UserId, PostId, CommentId) UNIQUE'i DEĞİL.

          PostgreSQL'de UNIQUE kısıtında NULL'lar birbirinden AYRI sayılır. Düz bir
          (UserId, PostId) UNIQUE'i, PostId'si NULL olan yorum oylarını hiç
          kısıtlamazdı: aynı kullanıcı aynı yoruma sınırsız oy satırı yazabilir ve
          sayacı istediği kadar şişirebilirdi.

          HasFilter ile her iki hedef türü ayrı ayrı kilitleniyor. Bu, uygulama
          katmanındaki "önce mevcut oyu ara" kontrolünün yerine geçmiyor; onun
          kapatamadığı yarışı kapatıyor (CLAUDE.md: kısmi index son savunma hattıdır).
        */
        builder.HasIndex(x => new { x.UserId, x.PostId })
            .IsUnique()
            .HasFilter("\"PostId\" IS NOT NULL")
            .HasDatabaseName("UX_Votes_KullaniciGonderi");

        builder.HasIndex(x => new { x.UserId, x.CommentId })
            .IsUnique()
            .HasFilter("\"CommentId\" IS NOT NULL")
            .HasDatabaseName("UX_Votes_KullaniciYorum");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<CommunityPost>()
            .WithMany()
            .HasForeignKey(x => x.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<CommunityComment>()
            .WithMany()
            .HasForeignKey(x => x.CommentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
