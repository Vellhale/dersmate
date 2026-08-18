using PeerLearn.Domain.Common;

namespace PeerLearn.Domain.Moderation;

/// <summary>
/// Cihaz kimliği (HWID) ban listesi. Kayıt/giriş sırasında istemci parmak izinin hash'i
/// bu tabloyla karşılaştırılır; eşleşme varsa yeni hesap açılışı da engellenir (ban evasion önlemi).
/// ExpiresAtUtc null ise ban kalıcıdır. Süresi dolan banı background job IsActive=false yapar;
/// böylece ban geçmişi (denetim izi) silinmeden aynı cihaz gerekirse yeniden banlanabilir.
/// </summary>
public class HwidBan : BaseEntity
{
    public string HwidHash { get; set; } = null!;

    /// <summary>Aktif ban kontrolü bu bayrak üzerinden yapılır; tarih bazlı filtre index'lenemez.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Bana neden olan kullanıcı (biliniyorsa).</summary>
    public Guid? RelatedUserId { get; set; }

    public string Reason { get; set; } = null!;
    public Guid? BannedByAdminId { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
}
