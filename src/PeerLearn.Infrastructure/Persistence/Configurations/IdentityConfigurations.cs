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
        /* SHA-256 hex = tam 64 karakter. Sabit uzunluk olduğu için sınır da tam:
           daha genişi, yanlışlıkla ham kodun yazılmasını fark ettirmezdi. */
        builder.Property(x => x.EmailVerificationCodeHash).HasMaxLength(64);

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

public sealed class UserBlockConfiguration : IEntityTypeConfiguration<UserBlock>
{
    public void Configure(EntityTypeBuilder<UserBlock> builder)
    {
        builder.ToTable("UserBlocks", "identity", t =>
            /* Matches tablosundaki CK_Matches_DifferentUsers ile aynı kalıp: kendini
               engellemek anlamsız ve bir yerde kimlik karıştığının işareti olur. */
            t.HasCheckConstraint("CK_UserBlocks_DifferentUsers", "\"BlockerUserId\" <> \"BlockedUserId\""));

        builder.HasKey(x => x.Id);

        /* Aynı çift için ikinci bir satır olmasın. FİLTRESİZ tekil — Matches'taki
           kısmi tekillikten farkı bilinçli: orada "bekleyen" bir durum var ve kayıt
           yaşam döngüsü boyunca değişiyor; burada satırın kendisi durumdur, engel
           kaldırılınca satır SİLİNİYOR. Yani "aktif engel" diye ayrı bir bayrak yok
           ve kısmi filtreye gerek de yok.

           ⚠️ Bayrak yerine silme seçildi çünkü engel kaldırıldıktan sonra o kaydı
           tutmanın hiçbir işlevi yok ve kişisel veriyi gereksiz saklamak olurdu. */
        builder.HasIndex(x => new { x.BlockerUserId, x.BlockedUserId }).IsUnique();

        /* "Beni kim engelledi" sorgusu için TERS yönde index. Kontroller çift yönlü
           olduğu için bu sorgu her istek gönderiminde koşuyor; tersi olmadan
           BlockedUserId üzerinden arama tabloyu tarardı. */
        builder.HasIndex(x => x.BlockedUserId);

        builder.Property(x => x.Note).HasMaxLength(500);

        /* Restrict, Cascade DEĞİL — Matches ile aynı gerekçe: hesap silme bu üründe
           satır silmiyor, anonimleştiriyor. Cascade yazmak var olmayan bir güvence
           hissi verirdi. Engel kayıtları hesap silindiğinde DeleteAccount akışında
           elle temizleniyor. */
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.BlockerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.BlockedUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens", "identity", t =>
        {
            // Süresi kendinden önce dolan bir token, bir yerde saatin ya da ömür
            // hesabının bozulduğunun işareti olur ve sessiz kalmamalı.
            t.HasCheckConstraint(
                "CK_RefreshTokens_Expiry",
                "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
        });

        builder.HasKey(x => x.Id);

        /* SHA-256 hex = tam 64 karakter; sınır da tam bu. Daha genişi, yanlışlıkla HAM
           token'ın yazılmasını fark ettirmezdi — kolon sessizce kabul ederdi. */
        builder.Property(x => x.TokenHash)
            .HasMaxLength(RefreshTokenRules.HashLength)
            .IsRequired();

        /* FİLTRESİZ tekil index: doğrulama sorgusunun TEK girişi bu. Kısmi yapılmadı
           çünkü iptal edilmiş satırlar da aranabilmeli — yeniden kullanım tespiti tam
           olarak "iptal edilmiş bir token yeniden sunuldu mu" sorusuna dayanıyor.
           Kısmi olsaydı iptal edilmiş satır index'te bulunmaz, hırsızlık sessizce
           "geçersiz token" olarak görünürdü. */
        builder.HasIndex(x => x.TokenHash).IsUnique();

        /* "Bu kullanıcının aktif token'ları" sorgusu — toplu iptalde ve temizlikte
           kullanılıyor.

           ⚠️ Filtre yalnızca RevokedAtUtc üzerinde. Süre koşulu (ExpiresAtUtc > now())
           kısmi index'e KONULAMAZ: now() immutable değil, PostgreSQL reddeder. Süre
           kontrolü uygulama katmanında kalıyor.

           ⚠️ Bu index'in kullanılması için sorgunun WHERE'i filtreyi BİREBİR içermeli:
           `RevokedAtUtc == null`. Başka bir ifadeyle yazılırsa index sessizce devreden
           çıkar; sonuç doğru döner, tablo taranır. */
        builder.HasIndex(x => new { x.UserId, x.ExpiresAtUtc })
            .HasFilter("\"RevokedAtUtc\" IS NULL")
            .HasDatabaseName("IX_RefreshTokens_AktifKullanici");

        builder.Property(x => x.RevokeReason).HasConversion<string>().HasMaxLength(20);

        // UserDevices.HwidHash ile AYNI uzunluk — aynı değerin kopyası, ayrışmasın.
        builder.Property(x => x.DeviceHwidHash).HasMaxLength(128);

        /* Cascade YAZILDI ama ona GÜVENİLMİYOR: hesap silme bu üründe satır SİLMİYOR,
           anonimleştiriyor (Status=Deleted). Yani DeleteAccount akışında token iptali
           ELLE yapılmak zorunda — aksi halde silinmiş hesabın yenileme token'ı çalışmaya
           devam eder ve taze erişim token'ı üretir. Cascade yalnızca gerçek bir satır
           silmede (ör. test temizliği) devreye girer. */
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
