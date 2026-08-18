using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeerLearn.Domain.Identity;
using PeerLearn.Domain.Moderation;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Infrastructure.Persistence.Configurations;

public sealed class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.ToTable("Reports", "moderation");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Reason).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.AdminNote).HasMaxLength(1000);

        // Yönetim kuyruğu: açık şikayetler, en yenisi üstte.
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc }).IsDescending(false, true);

        // "Bu kullanıcı hakkında kaç şikayet var" — yaptırım kararının dayanağı.
        builder.HasIndex(x => x.ReportedUserId);

        /*
          AYNI KİŞİYİ AYNI DERS İÇİN TEKRAR ŞİKAYET ETMEK ENGELLİ.
          Kısmi unique: SessionId dolu olan şikayetlerde (şikayetçi, ders) çifti tektir.
          Ders dışı şikayetlerde (SessionId NULL) sınır yok — aynı kişi hakkında farklı
          olaylar için ayrı ayrı şikayet açılabilmeli.
        */
        builder.HasIndex(x => new { x.ReporterUserId, x.SessionId })
            .IsUnique()
            .HasFilter("\"SessionId\" IS NOT NULL");

        // Restrict: şikayet kaydı, tarafların hesabı silinse bile DURMALI (denetim izi).
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ReporterUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ReportedUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LessonSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DisputeConfiguration : IEntityTypeConfiguration<Dispute>
{
    public void Configure(EntityTypeBuilder<Dispute> builder)
    {
        builder.ToTable("Disputes", "moderation");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Reason).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.TutorStatement).HasMaxLength(2000);
        builder.Property(x => x.ResolutionNote).HasMaxLength(2000);

        // Bir ders için aynı anda tek açık itiraz.
        builder.HasIndex(x => x.SessionId)
            .IsUnique()
            .HasFilter("\"Status\" IN ('Open', 'UnderReview')");

        // Admin paneli kuyruğu.
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc });

        builder.HasOne<LessonSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.RaisedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AdminActionLogConfiguration : IEntityTypeConfiguration<AdminActionLog>
{
    public void Configure(EntityTypeBuilder<AdminActionLog> builder)
    {
        builder.ToTable("AdminActionLogs", "moderation");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Action).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.ActorRole).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.TargetType).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Summary).HasMaxLength(500).IsRequired();

        // jsonb (json değil): sorgulanabilir ve indekslenebilir; ayrıca yazarken doğrulanır.
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");

        // Panelin iki ana görünümü: zaman akışı ve "şu kayda ne yapıldı?" geçmişi.
        builder.HasIndex(x => x.CreatedAtUtc).IsDescending();
        builder.HasIndex(x => new { x.TargetType, x.TargetId });
        builder.HasIndex(x => new { x.ActorUserId, x.CreatedAtUtc });

        builder.Property(x => x.IdempotencyKey).HasMaxLength(64);

        /*
          TEKİLLİK KISITI — son savunma, ilk savunma değil.

          Handler zaten "anahtar var mı" diye bakıyor; ama bakma ile yazma arasında bir
          boşluk var ve aynı anahtarla gelen iki eşzamanlı istek ikisini de boş görebilir.
          Cüzdan kilidi çoğu durumda serileştirir, Redis erişilemezse serileştirmez.
          Bu index olmadan tekillik "genelde çalışan" bir şey olurdu.

          KISMİ (filtreli): anahtarsız satırlar — ban, rol değişikliği, iş tetikleme —
          çoğunlukta ve hepsi NULL. Filtresiz unique index tek bir NULL'a izin verse
          işlemlerin tamamı kırılırdı; PostgreSQL çoklu NULL'a izin verse de index
          gereksiz yere şişerdi.

          AKTÖRLE BİRLİKTE: anahtarı istemci üretiyor. Global tekillik olsaydı, iki
          yöneticinin (hatalı bir istemci yüzünden) aynı anahtarı kullanması birinin
          işlemini diğerinin tekrarı sayardı.
        */
        builder.HasIndex(x => new { x.ActorUserId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");

        // Restrict: denetim izi, işlemi yapan kullanıcı silinse bile DURMALI.
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class HwidBanConfiguration : IEntityTypeConfiguration<HwidBan>
{
    public void Configure(EntityTypeBuilder<HwidBan> builder)
    {
        builder.ToTable("HwidBans", "moderation");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.HwidHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();

        // Kayıt/giriş sırasındaki aktif ban kontrolünün ana index'i.
        // Global unique DEĞİL: süresi dolan ban IsActive=false kalır (denetim izi) ve aynı cihaz
        // yeniden banlanabilir; unique'lik yalnızca aktif banlar için geçerlidir.
        // Not: "ExpiresAtUtc > now()" gibi bir filtre index'lenemez (immutable değil), bayrak şart.
        builder.HasIndex(x => x.HwidHash)
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE");
    }
}

public sealed class UserSanctionConfiguration : IEntityTypeConfiguration<UserSanction>
{
    public void Configure(EntityTypeBuilder<UserSanction> builder)
    {
        builder.ToTable("UserSanctions", "moderation");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();

        builder.HasIndex(x => new { x.UserId, x.CreatedAtUtc });

        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
