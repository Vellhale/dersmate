namespace PeerLearn.Domain.Common;

public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Tüm zaman alanları UTC tutulur; Npgsql bunları "timestamptz" olarak eşler.</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
