using System.Security.Claims;

namespace PeerLearn.Api.Controllers;

public static class CurrentUser
{
    /// <summary>JWT 'sub' claim'i (JwtBearer varsayılan eşlemesiyle NameIdentifier'a taşınır).</summary>
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal.FindFirstValue("sub")
                  ?? throw new InvalidOperationException("Token'da kullanıcı kimliği yok.");

        return Guid.Parse(sub);
    }

    /// <summary>
    /// Kimlik yoksa/bozuksa null döner. Middleware için: orada eksik claim bir programlama
    /// hatası değil, gelen isteğin özelliğidir — istisna fırlatmak 500 üretirdi.
    /// </summary>
    public static Guid? GetUserIdOrNull(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal.FindFirstValue("sub");

        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
