using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeerLearn.Domain.Catalog;
using PeerLearn.Domain.Identity;
using PeerLearn.Domain.Matchmaking;

namespace PeerLearn.Infrastructure.Persistence.Configurations;

public sealed class PortfolioEntryConfiguration : IEntityTypeConfiguration<PortfolioEntry>
{
    public void Configure(EntityTypeBuilder<PortfolioEntry> builder)
    {
        builder.ToTable("PortfolioEntries", "matchmaking", t =>
            t.HasCheckConstraint("CK_PortfolioEntries_Level", "\"SelfAssessedLevel\" BETWEEN 1 AND 5"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.Note).HasMaxLength(500);

        // Aynı kullanıcı aynı konuyu aynı yönde iki kez ekleyemez.
        builder.HasIndex(x => new { x.UserId, x.TopicId, x.Direction }).IsUnique();

        // Çapraz eşleşme sorgusunun ana index'i: "X konusunu Offer/Seek eden aktif kullanıcılar".
        builder.HasIndex(x => new { x.TopicId, x.Direction })
            .HasFilter("\"IsActive\" = TRUE");

        // Modüller arası gezinme (navigation) yok; FK bütünlüğü DB seviyesinde korunur.
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Topic>().WithMany().HasForeignKey(x => x.TopicId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MatchConfiguration : IEntityTypeConfiguration<Match>
{
    public void Configure(EntityTypeBuilder<Match> builder)
    {
        builder.ToTable("Matches", "matchmaking", t =>
            t.HasCheckConstraint("CK_Matches_DifferentUsers", "\"InitiatorUserId\" <> \"ResponderUserId\""));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);

        // Aynı kişiye aynı konu için ikinci bir bekleyen istek engellenir (spam önlemi).
        builder.HasIndex(x => new { x.InitiatorUserId, x.ResponderUserId, x.RequestedTopicId })
            .IsUnique()
            .HasFilter("\"Status\" = 'Pending'");

        /*
          KONUSUZ İSTEKLER İÇİN AYRI BİR TEKİLLİK İNDEKSİ — üstteki index onları KAPSAMAZ.

          PostgreSQL'de UNIQUE kısıtı NULL'ları birbirinden FARKLI sayar. Yani
          (A, B, NULL) satırı istendiği kadar tekrarlanabilir: üniversite ağı isteği
          konusuz olduğu için yukarıdaki spam freni orada sessizce devre dışı kalıyor ve
          bir kullanıcı aynı kişiye sınırsız bekleyen istek gönderebiliyordu.

          Bu ikinci index tam olarak o boşluğu kapatıyor: konu YOKKEN çift başına tek bir
          bekleyen istek. Filtre iki koşulu birden taşıyor çünkü kısmi index yalnızca
          sorgunun WHERE'i koşulu BİREBİR içerdiğinde kullanılır (bkz. CLAUDE.md).

          Uygulama katmanı da aynı kontrolü yapıyor (MatchRequests). Bu index son savunma
          hattı: iki paralel istek arasındaki yarışı kapatan şey o kontrol değil, burası.
        */
        builder.HasIndex(x => new { x.InitiatorUserId, x.ResponderUserId })
            .IsUnique()
            .HasFilter("\"Status\" = 'Pending' AND \"RequestedTopicId\" IS NULL")
            .HasDatabaseName("UX_Matches_PendingKonusuz");

        // "Bana gelen bekleyen istekler" ekranı.
        builder.HasIndex(x => new { x.ResponderUserId, x.Status });

        // Sohbet listesi ve "gönderdiğim istekler" ekranının initiator bacağı.
        // Yukarıdaki Pending-filtreli partial index Status='Accepted' sorgularında KULLANILAMAZ;
        // ayrıca EF, FK kolonuyla başlayan bir index görünce otomatik FK index'i de üretmez.
        builder.HasIndex(x => new { x.InitiatorUserId, x.Status });

        // Süpürücünün "yanıtsız kalmış istekler" taraması. Kısmi index: yalnızca Pending
        // satırlar girer ve istek yanıtlandığı anda index'ten düşer — yani index, tablo
        // büyüse de bekleyen istek sayısı kadar kalır.
        builder.HasIndex(x => x.CreatedAtUtc)
            .HasFilter("\"Status\" = 'Pending'")
            .HasDatabaseName("IX_Matches_PendingAge");

        builder.HasOne<User>().WithMany().HasForeignKey(x => x.InitiatorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ResponderUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Topic>().WithMany().HasForeignKey(x => x.RequestedTopicId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Topic>().WithMany().HasForeignKey(x => x.OfferedTopicId).OnDelete(DeleteBehavior.Restrict);
    }
}
