using System.Security.Cryptography;
using System.Text;

namespace PeerLearn.Application.Features.Community;

/// <summary>
/// Avatar'ın DIŞARIYA verilen adresi. Ham depo anahtarı (<c>yyyy/MM/guid.ext</c>) hiçbir
/// yanıta girmez: depo düzeni (yol biçimi, sağlayıcı) dışarıya sızmasın diye görsel
/// yalnızca yetkili uçtan (<c>GET api/users/{id}/avatar</c>) okunur — <c>GetAvatar</c>'ın
/// tasarımı zaten budur; profil yanıtı ve yükleme yanıtı da aynı kurala uyar.
///
/// Dönen yol o ucu işaret eder; sonundaki sürüm damgası, avatar değişince istemci/CDN
/// önbelleğinin eski fotoğrafı göstermesini engeller. Damga, anahtarın TEK YÖNLÜ hash'idir
/// (anahtarın kendisi değil) — geri çözülüp depo düzenine varılamaz.
/// </summary>
public static class AvatarErisim
{
    /// <summary>
    /// Avatar yoksa <c>null</c>; varsa <c>/api/users/{id}/avatar?v={damga}</c>.
    /// </summary>
    public static string? YolFor(Guid userId, string? depoAnahtari)
    {
        if (string.IsNullOrEmpty(depoAnahtari))
        {
            return null;
        }

        return $"/api/users/{userId}/avatar?v={SurumDamgasi(depoAnahtari)}";
    }

    private static string SurumDamgasi(string depoAnahtari)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(depoAnahtari));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
