using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Identity;

/// <summary>
/// Kullanıcının giriş yaptığı cihazların parmak izi kaydı. HwidHash, ban kontrolünde
/// moderation.HwidBans tablosuyla karşılaştırılır (ham HWID değil, SHA-256 hash'i saklanır).
/// </summary>
public class UserDevice : BaseEntity
{
    public Guid UserId { get; set; }
    public string HwidHash { get; set; } = null!;
    public string? UserAgent { get; set; }
    public DateTime FirstSeenAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}
