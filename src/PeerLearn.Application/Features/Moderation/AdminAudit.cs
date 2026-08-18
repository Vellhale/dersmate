using System.Text.Json;
using PeerLearn.Application.Abstractions;
using PeerLearn.Domain.Identity;
using PeerLearn.Domain.Moderation;

namespace PeerLearn.Application.Features.Moderation;

/// <summary>
/// Denetim izi yazımı için tek giriş noktası.
///
/// TASARIM KARARI — neden ayrı bir servis/arka plan kuyruğu DEĞİL: kayıt, işlemin kendisiyle
/// AYNI transaction içinde eklenir. Ayrı yazılsaydı iki hata mümkün olurdu:
///   • Karar commit olur, log yazılamaz  → iz bırakmadan kredi hareket etmiş olur.
///   • Log yazılır, karar geri alınır    → olmamış bir karar kayıtlara geçer.
/// Bu yüzden yalnızca DbSet'e ekler; SaveChanges'i çağıran handler'ın kendisidir.
/// </summary>
public static class AdminAudit
{
    public static void Record(
        IAppDbContext db,
        Guid actorUserId,
        UserRole actorRole,
        AdminActionType action,
        string targetType,
        Guid? targetId,
        string summary,
        object? metadata = null,
        string? idempotencyKey = null)
    {
        db.AdminActionLogs.Add(new AdminActionLog
        {
            ActorUserId = actorUserId,
            ActorRole = actorRole,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Summary = Truncate(summary, 500),
            MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata),
            IdempotencyKey = idempotencyKey
        });
    }

    /// <summary>
    /// Özet 500 karakterle sınırlı (DB kısıtı). Uzun bir gerekçe yüzünden KARARIN kendisi
    /// kaydedilemez hâle gelmesin diye kesiliyor — denetim izi kaybetmektense kırpmak yeğ.
    /// </summary>
    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..(max - 1)] + "…";
}
