using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Options;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Infrastructure.Services;

/// <summary>
/// Erişim token'ı + tek amaçlı (purpose) token üretimi. Purpose token'lar
/// e-posta doğrulama gibi akışlarda DB tablosu gerektirmeyen, imzalı ve süreli
/// taşıyıcılardır; "purpose" claim'i yanlış bağlamda kullanılmalarını engeller.
/// </summary>
public sealed class JwtTokenService : ITokenService
{
    private const string PurposeClaim = "purpose";

    private readonly JwtOptions _options;
    private readonly string _purposeAudience;
    private readonly SigningCredentials _credentials;
    private readonly TokenValidationParameters _purposeValidation;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;

        // GÜVENLİK: purpose token'lar FARKLI audience taşır. Aksi halde e-posta doğrulama
        // token'ı [Authorize] endpoint'lerine Bearer olarak verilip erişim token'ı gibi
        // kullanılabilirdi (JwtBearer yalnızca issuer/audience/imza/süre doğrular).
        _purposeAudience = _options.Audience + ".purpose";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        _purposeValidation = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _purposeAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    }

    public string CreateAccessToken(User user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.DisplayName),
            new(JwtRegisteredClaimNames.Email, user.Email)
        };

        /*
          RBAC: rol claim'i enum adının küçük harflisi ("admin" | "moderator" | "student").
          Admin için "admin" değeri BİLEREK korundu — mevcut [Authorize(Roles = "admin")]
          uçları ve e2e betikleri kırılmasın diye.

          Rol HİYERARŞİSİ burada AÇILARAK yazılır: Admin, moderatörün yapabildiği her şeyi
          yapabilir, o yüzden "moderator" claim'ini de taşır. Alternatif (her policy'de
          "admin veya moderator" listesi yazmak) tek bir yerde unutulduğunda sessizce
          admin'i kendi panelinden kilitlerdi.
        */
        claims.Add(new Claim(ClaimTypes.Role, user.Role.ToString().ToLowerInvariant()));

        if (user.Role == UserRole.Admin)
        {
            claims.Add(new Claim(ClaimTypes.Role, UserRole.Moderator.ToString().ToLowerInvariant()));
        }

        return Write(claims, TimeSpan.FromMinutes(_options.AccessTokenMinutes), _options.Audience);
    }

    public string CreatePurposeToken(Guid userId, string purpose, TimeSpan lifetime)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(PurposeClaim, purpose)
        };

        return Write(claims, lifetime, _purposeAudience);
    }

    public Guid? ValidatePurposeToken(string token, string purpose)
    {
        var sonuc = Coz(token);
        if (sonuc is null || sonuc.Value.Purpose != purpose)
        {
            return null;
        }

        return sonuc.Value.UserId;
    }

    public (Guid UserId, string Purpose)? ValidatePurposeTokenByPrefix(string token, string purposePrefix)
    {
        var sonuc = Coz(token);
        if (sonuc is null || !sonuc.Value.Purpose.StartsWith(purposePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        return sonuc;
    }

    /// <summary>
    /// İmza + audience + süre doğrulaması ve claim okuma — iki genel metodun ORTAK
    /// gövdesi. Amaç KARŞILAŞTIRMASI bilerek burada değil, çağıranlarda: biri kesin
    /// eşitlik, diğeri önek istiyor ve doğrulamanın geri kalanı ikisinde de aynı.
    /// </summary>
    private (Guid UserId, string Purpose)? Coz(string token)
    {
        try
        {
            var principal = new JwtSecurityTokenHandler()
                .ValidateToken(token, _purposeValidation, out _);

            var purpose = principal.FindFirstValue(PurposeClaim);
            if (purpose is null)
            {
                return null;
            }

            // JwtBearer varsayılan claim eşlemesi 'sub'u NameIdentifier'a çevirir; ikisine de bak.
            var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                      ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

            return Guid.TryParse(sub, out var userId) ? (userId, purpose) : null;
        }
        catch (Exception)
        {
            return null; // İmza/süre/format hatası → geçersiz token.
        }
    }

    private string Write(IEnumerable<Claim> claims, TimeSpan lifetime, string audience)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: audience,
            claims: claims,
            notBefore: now,
            expires: now.Add(lifetime),
            signingCredentials: _credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
