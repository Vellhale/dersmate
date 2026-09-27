using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeerLearn.Domain.Communication;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Infrastructure.Persistence.Configurations;

/*
  PUSH BİLDİRİM ALTYAPISI (comms şeması). Mimari: docs/ASAMA-2-BACKEND.md.

  Bu dosyadaki tablolara yazan yolların bir kısmı HAM SQL (INSERT … ON CONFLICT, FOR UPDATE
  SKIP LOCKED, koşullu toplu UPDATE). Sonuç: burada yazılan DB VARSAYILANLARI süs değil,
  o yolların doğruluğu onlara dayanıyor. Bir varsayılanı kaldırmadan önce ham SQL'leri ara.
*/

public sealed class PushDeviceConfiguration : IEntityTypeConfiguration<PushDevice>
{
    public void Configure(EntityTypeBuilder<PushDevice> builder)
    {
        builder.ToTable("PushDevices", "comms");

        builder.HasKey(x => x.Id);

        // ExponentPushToken[…]. 200, kayıt ucunun doğrulama sınırıyla aynı değer.
        builder.Property(x => x.Token).HasMaxLength(200).IsRequired();

        builder.Property(x => x.Platform).HasConversion<string>().HasMaxLength(10);

        // RefreshTokens.DeviceHwidHash ile AYNI uzunluk — oturum bağı ikisinin eşitliğine dayanıyor.
        builder.Property(x => x.HwidHash).HasMaxLength(128).IsRequired();

        /* text[]. DB varsayılanı '{}' kayıt ucunun upsert'i için: kolon hiç verilmese de
           NULL olmasın. ValueGeneratedNever: EF kendi eklemelerinde değeri HER ZAMAN
           gönderir (C# başlangıç değeri boş dizi); varsayılan yalnızca ham SQL içindir. */
        builder.Property(x => x.KapaliKanallar)
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'")
            .ValueGeneratedNever()
            .IsRequired();

        /* Token tekil: aynı telefonda hesap değişince satır yeni kullanıcıya TAŞINIR
           (upsert ON CONFLICT ("Token")). Tekil olmasaydı eski hesap o telefona
           bildirim almaya devam ederdi. */
        builder.HasIndex(x => x.Token).IsUnique();

        /* Kullanıcı-cihaz başına tek token. Uygulama yeniden kurulunca token değişir ama
           HWID aynı kalır; kayıt ucu eski token'ı aynı transaction'da siler. Tekillik
           yarışı kapatan son hat — advisory kilit ilk hat. */
        builder.HasIndex(x => new { x.UserId, x.HwidHash }).IsUnique();

        /* Cascade yazıldı ama ona GÜVENİLMİYOR: hesap silme Users satırını silmiyor,
           anonimleştiriyor. Silme her akışta ELLE (bkz. PushDevice sınıf yorumu).
           Cascade yalnızca gerçek satır silmede (test temizliği) devreye girer. */
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications", "comms", t =>
            // Negatif deneme sayısı, bekleme hesabında negatif gecikme üretirdi.
            t.HasCheckConstraint("CK_Notifications_Attempts", "\"Attempts\" >= 0"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.DedupeKey).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(40);

        // Ham INSERT'ler (hatırlatma kuyruklaması) sayacı vermeyebilir.
        builder.Property(x => x.Attempts).HasDefaultValue(0).ValueGeneratedNever();

        // Yalnızca MASKELENMİŞ metin (tam token asla).
        builder.Property(x => x.LastError).HasMaxLength(300);

        /* Mükerrere karşı SON hat ve hatırlatmaların sahiplenmesi (SweepFailure emsali):
           iki sunucu kopyası ya da yeniden başlatma aynı hatırlatmayı ikinci kez
           YAZAMAZ — INSERT … ON CONFLICT ("RecipientUserId","DedupeKey") DO NOTHING.
           Olay satırlarında da (mesaj, istek) aynı olayın iki kez kuyruğa girmesini keser. */
        builder.HasIndex(x => new { x.RecipientUserId, x.DedupeKey }).IsUnique();

        /* Dağıtıcının sahiplenme sorgusu. Kısmi: yalnızca bekleyen satırlar girer, işlenen
           satır index'ten düşer — tablo büyüse de index kuyruk kadar kalır.

           ⚠️ Sorgunun WHERE'i `"Status" = 'Pending'` koşulunu BİREBİR içermeli; yoksa
           index sessizce devreden çıkar (sonuç doğru döner, tablo taranır). */
        builder.HasIndex(x => x.DueAtUtc)
            .HasFilter("\"Status\" = 'Pending'")
            .HasDatabaseName("IX_Notifications_Bekleyen");

        // Mesaj birleştirme: aynı (alıcı, sohbet) için bekleyen satırlardan yalnızca en yenisi gider.
        builder.HasIndex(x => new { x.RecipientUserId, x.ConversationId });

        // Çift düzeyinde istek freni (7 gün) ve test ucu sayacı (10 dakikada 3).
        builder.HasIndex(x => new { x.RecipientUserId, x.Type, x.ActorUserId });

        // Yaş temizliği (30 gün).
        builder.HasIndex(x => x.CreatedAtUtc);

        // FK YOK — gerekçe Notification sınıf yorumunda (SweepFailure deseni).
    }
}

public sealed class PushTicketConfiguration : IEntityTypeConfiguration<PushTicket>
{
    public void Configure(EntityTypeBuilder<PushTicket> builder)
    {
        builder.ToTable("PushTickets", "comms");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TicketId).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.TicketId).IsUnique();

        // Makbuz işi "15 dakikadan eski biletler" ve "24 saatten eski biletler" diye tarar.
        builder.HasIndex(x => x.CreatedAtUtc);

        // FK BİLEREK YOK — gerekçe PushTicket sınıf yorumunda.
    }
}

public sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("NotificationPreferences", "comms", t =>
            t.HasCheckConstraint("CK_NotificationPreferences_PromptDeferCount", "\"PromptDeferCount\" >= 0"));

        builder.HasKey(x => x.Id);

        // Kullanıcı başına TEK satır; tek sütunluk upsert'in ON CONFLICT hedefi.
        builder.HasIndex(x => x.UserId).IsUnique();

        /* ⚠️ DÖRT ANAHTARIN VARSAYILANI TRUE ve bu DOĞRULUK için gerekli: tek sütunluk
           upsert satırı ilk kez yaratırken yalnızca bir anahtarı verir, diğer üçü DB
           varsayılanından gelir. false olsaydı bir anahtarı değiştirmek diğer üçünü
           sessizce kapatırdı.

           ValueGeneratedNever: EF'in kendi eklemeleri değeri HER ZAMAN gönderir. Aksi hâlde
           EF, CLR varsayılanı olan false'u "değer verilmedi" sayar, kolonu INSERT'ten çıkarır
           ve DB true yazardı — kullanıcı kapattığı anahtarı açık bulurdu. */
        builder.Property(x => x.Messages).HasDefaultValue(true).ValueGeneratedNever();
        builder.Property(x => x.Requests).HasDefaultValue(true).ValueGeneratedNever();
        builder.Property(x => x.LessonApproval).HasDefaultValue(true).ValueGeneratedNever();
        builder.Property(x => x.LessonPlan).HasDefaultValue(true).ValueGeneratedNever();

        builder.Property(x => x.PromptDeferCount).HasDefaultValue(0).ValueGeneratedNever();

        // xmin YOK — bilerek. Gerekçe NotificationPreference sınıf yorumunda.

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MessagePushThrottleConfiguration : IEntityTypeConfiguration<MessagePushThrottle>
{
    public void Configure(EntityTypeBuilder<MessagePushThrottle> builder)
    {
        builder.ToTable("MessagePushThrottles", "comms");

        // Doğal anahtar: yuva ifadesinin ON CONFLICT hedefi.
        builder.HasKey(x => new { x.RecipientUserId, x.ConversationId });

        // FK yok: hesap silme ve günlük temizlik satırları kaldırır.
    }
}
