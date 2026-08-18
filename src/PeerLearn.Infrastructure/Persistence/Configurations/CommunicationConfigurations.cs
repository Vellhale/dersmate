using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PeerLearn.Domain.Communication;
using PeerLearn.Domain.Identity;
using PeerLearn.Domain.Matchmaking;

namespace PeerLearn.Infrastructure.Persistence.Configurations;

public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations", "comms");

        builder.HasKey(x => x.Id);

        // Bir eşleşmenin tek sohbeti olur.
        builder.HasIndex(x => x.MatchId).IsUnique();

        builder.HasOne<Match>().WithOne().HasForeignKey<Conversation>(x => x.MatchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages", "comms");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Content).HasMaxLength(2000).IsRequired();

        // Sohbet geçmişi sayfalaması: konuşma + zaman sıralı.
        builder.HasIndex(x => new { x.ConversationId, x.CreatedAtUtc });

        // Okunmamış mesaj rozeti: yalnız okunmamış satırları tutan, okundukça küçülen partial index.
        // Rozet sorgusunun WHERE'i bu filter koşulunu birebir içermelidir.
        builder.HasIndex(x => new { x.ConversationId, x.SenderUserId })
            .HasFilter("\"ReadAtUtc\" IS NULL AND \"IsDeleted\" = FALSE");

        builder.HasOne(x => x.Conversation)
            .WithMany(x => x.Messages)
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>().WithMany().HasForeignKey(x => x.SenderUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
