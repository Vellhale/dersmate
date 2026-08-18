using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeerLearn.Domain.Economy;
using PeerLearn.Domain.Identity;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Infrastructure.Persistence.Configurations;

public sealed class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("Wallets", "economy", t =>
        {
            // Negatif bakiye hiçbir kod hatasında oluşamaz — son savunma hattı DB'dir.
            t.HasCheckConstraint("CK_Wallets_AvailableBalance", "\"AvailableBalance\" >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.UserId).IsUnique();

        // Eşzamanlı harcama/kazanç yazmalarına karşı optimistic concurrency (PostgreSQL xmin).
        // Kredi transferi ayrıca Redis distributed lock + tek DB transaction ile korunur (AŞAMA 2).
        builder.Property(x => x.Version).IsRowVersion();

        builder.HasOne<User>().WithOne().HasForeignKey<Wallet>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CreditLotConfiguration : IEntityTypeConfiguration<CreditLot>
{
    public void Configure(EntityTypeBuilder<CreditLot> builder)
    {
        builder.ToTable("CreditLots", "economy", t =>
        {
            t.HasCheckConstraint("CK_CreditLots_InitialAmount", "\"InitialAmount\" > 0");
            t.HasCheckConstraint("CK_CreditLots_RemainingAmount",
                "\"RemainingAmount\" >= 0 AND \"RemainingAmount\" <= \"InitialAmount\"");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);

        // FIFO tüketim: cüzdanın aktif lotları vade sırasıyla çekilir.
        builder.HasIndex(x => new { x.WalletId, x.ExpiresAtUtc })
            .HasFilter("\"RemainingAmount\" > 0");

        // Vade dolumu job'ı: tüm cüzdanlarda süresi dolan aktif lotlar.
        builder.HasIndex(x => x.ExpiresAtUtc)
            .HasFilter("\"RemainingAmount\" > 0");

        // Filtresiz FK index'i: lot GEÇMİŞİ sorguları (tükenmiş/vadesi dolmuş dahil) için.
        // Yukarıdaki partial index, RemainingAmount > 0 koşulunu taşımayan sorgularda kullanılamaz.
        builder.HasIndex(x => x.WalletId, "IX_CreditLots_WalletId");

        // Cüzdan başına en fazla BİR hoş geldin lotu — çift grant, uygulama kodundan
        // bağımsız olarak DB'de unique violation ile patlar (tek mint noktası garantisi).
        builder.HasIndex(x => x.WalletId, "UX_CreditLots_OneWelcomeBonusPerWallet")
            .IsUnique()
            .HasFilter("\"Source\" = 'WelcomeBonus'");

        // Aynı ders için ikinci bir kazanç lotu açılamaz (çifte capture'a karşı ikinci savunma hattı).
        builder.HasIndex(x => x.SourceSessionId)
            .IsUnique()
            .HasFilter("\"Source\" = 'LessonEarning'");

        builder.HasOne<Wallet>().WithMany().HasForeignKey(x => x.WalletId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LessonSession>().WithMany().HasForeignKey(x => x.SourceSessionId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.Version).IsRowVersion();
    }
}

public sealed class CreditTransactionConfiguration : IEntityTypeConfiguration<CreditTransaction>
{
    public void Configure(EntityTypeBuilder<CreditTransaction> builder)
    {
        builder.ToTable("CreditTransactions", "economy", t =>
        {
            t.HasCheckConstraint("CK_CreditTransactions_Amount", "\"Amount\" <> 0");

            /*
              DERS HAREKETİ EKSİK METADATA İLE YAZILAMAZ.

              CorrelationId ŞARTI KALDIRILDI. Eski modelde ders kazancı iki bacaklı bir
              transferdi ve CorrelationId iki bacağı eşleştiriyordu; mutabakat sorgusu
              (GROUP BY CorrelationId, SUM = 0) ona dayanıyordu. Yeni modelde basım TEK
              bacaklı — eşleştirilecek ikinci bacak yok, dolayısıyla zorunlu bir korelasyon
              da yok.

              Kısıt bu yüzden gevşetildi ama KALDIRILMADI: karşı taraf ve ders bilgisi hâlâ
              zorunlu, çünkü "bu puan hangi dersten, kiminle" sorusu denetimin tek dayanağı.

              NOT: bu kısıt gevşetilmeseydi HER BASIM check violation ile düşerdi — kısıt
              CorrelationId isterken basım onu yazmıyordu. Şema ile kod arasındaki bu
              sessiz çelişki, testlerin ilk koşumunda tüm onay akışını 500'e düşürürdü.
            */
            t.HasCheckConstraint("CK_CreditTransactions_TransferLegs",
                "\"Type\" <> 'LessonEarning' OR " +
                "(\"CounterpartyUserId\" IS NOT NULL AND \"RelatedSessionId\" IS NOT NULL)");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);

        // Cüzdan ekstresi: son hareketler üstte.
        builder.HasIndex(x => new { x.WalletId, x.CreatedAtUtc }).IsDescending(false, true);

        // Mutabakat sorgusu: SUM(Amount) GROUP BY CorrelationId = 0 olmalı.
        builder.HasIndex(x => x.CorrelationId);

        // Çifte basım engeli: bir ders için en fazla BİR kazanç satırı. Durum geçişi guard'ı
        // hangi sebeple atlanırsa atlansın ikinci basım INSERT anında patlar.
        builder.HasIndex(x => new { x.RelatedSessionId, x.Type })
            .IsUnique()
            .HasFilter("\"Type\" = 'LessonEarning'");

        builder.HasOne<Wallet>().WithMany().HasForeignKey(x => x.WalletId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LessonSession>().WithMany().HasForeignKey(x => x.RelatedSessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CreditLotConsumptionConfiguration : IEntityTypeConfiguration<CreditLotConsumption>
{
    public void Configure(EntityTypeBuilder<CreditLotConsumption> builder)
    {
        builder.ToTable("CreditLotConsumptions", "economy", t =>
        {
            t.HasCheckConstraint("CK_CreditLotConsumptions_Amount", "\"Amount\" > 0");

        });

        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.CreditLotId);

        // Bir hareket, bir lotu en fazla bir kez tüketir (crash/retry karşısında idempotency).
        builder.HasIndex(x => new { x.CreditTransactionId, x.CreditLotId }).IsUnique();

        builder.HasOne<CreditLot>().WithMany().HasForeignKey(x => x.CreditLotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CreditTransaction>().WithMany().HasForeignKey(x => x.CreditTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
