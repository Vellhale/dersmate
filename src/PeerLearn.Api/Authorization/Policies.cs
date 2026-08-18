using Microsoft.AspNetCore.Authorization;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Api.Authorization;

/// <summary>
/// RBAC politikaları tek yerde. Controller'lara ham rol dizesi yazmak yerine bu sabitler
/// kullanılır: yetki sınırı değişirse tek dosya güncellenir ve "hangi uç kime açık"
/// sorusunun cevabı tek ekranda görünür.
/// </summary>
public static class Policies
{
    /// <summary>İtiraz kuyruğunu görme ve karara bağlama. Admin + Moderator.</summary>
    public const string CanModerate = "policy:moderate";

    /// <summary>
    /// Kalıcı ban, HWID ban, rol atama, ekonomi metrikleri. YALNIZCA Admin.
    /// Moderatörden ayrı tutulmasının gerekçesi Domain/Identity/Enums.cs'te.
    /// </summary>
    public const string AdminOnly = "policy:admin";

    public static AuthorizationOptions AddPeerLearnPolicies(this AuthorizationOptions options)
    {
        var moderator = UserRole.Moderator.ToString().ToLowerInvariant();
        var admin = UserRole.Admin.ToString().ToLowerInvariant();

        // Admin token'ı "moderator" claim'ini de taşır (bkz. JwtTokenService), bu yüzden
        // burada tek rol yeterli — hiyerarşi token üretiminde bir kez çözülür.
        options.AddPolicy(CanModerate, p => p.RequireRole(moderator));
        options.AddPolicy(AdminOnly, p => p.RequireRole(admin));

        return options;
    }
}
