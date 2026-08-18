using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Moderation;

/// <summary>
/// Kullanıcıya uygulanan yaptırımların denetim kaydı. Aktif ban durumu User.Status üzerinde tutulur;
/// bu tablo "neden, ne zaman, kim tarafından" sorularının cevabını saklar.
/// </summary>
public class UserSanction : BaseEntity
{
    public Guid UserId { get; set; }
    public SanctionType Type { get; set; }
    public string Reason { get; set; } = null!;
    public Guid? IssuedByAdminId { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
}
