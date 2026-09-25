using System.Text.RegularExpressions;

namespace PeerLearn.Application.Features.Communication.Bildirimler;

/// <summary>
/// Expo push token'ının biçim kuralı ve günlüğe yazılabilecek maskeli hâli. Kayıt ucu,
/// forget ucu ve gönderici (günlük / LastError) aynı tanımı kullanır.
/// </summary>
/// <remarks>
/// ─── NEDEN BİÇİM SINANIYOR ──────────────────────────────────────────────────
/// Token kayıttan sonra Expo'ya olduğu gibi gönderiliyor. Biçimi tutmayan bir değer
/// (başka bir sağlayıcının ham FCM token'ı, boş köşeli parantez, 10 KB'lık dizge) ya kayıtta
/// reddedilir ya da ilk partide Expo'nun istek düzeyi hatasına yol açıp o partideki
/// herkesin bildirimini geciktirir. Kayıtta reddetmek ucuz olan.
///
/// ─── NEDEN MASKE ────────────────────────────────────────────────────────────
/// Token, o telefona bildirim göndermenin tek anahtarı: günlüğe tam yazılırsa günlüğü
/// okuyabilen herkes o kişiye bildirim gönderebilir (Enhanced Security kapalıyken yalnızca
/// token yeter). Teşhis için son altı karakter yetiyor: aynı cihazın satırlarını
/// ilişkilendirir, token'ı yeniden kurmaya yetmez.
/// </remarks>
public static class PushTokenKurali
{
    /// <summary>Kolon sınırı (PushDevices.Token varchar(200)) ile aynı.</summary>
    public const int EnFazlaUzunluk = 200;

    /// <summary>Maskede görünen son karakter sayısı.</summary>
    public const int GorunenKarakter = 6;

    /// <summary>
    /// <c>ExponentPushToken[…]</c> ya da eski <c>ExpoPushToken[…]</c>. İç kısım Expo'nun
    /// base64url benzeri alfabesi; boşluk, tırnak ve köşeli parantez içeremez.
    /// </summary>
    private static readonly Regex Desen = new(
        @"^Expo(nent)?PushToken\[[A-Za-z0-9_-]+\]$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    /// <summary>Biçim ve uzunluk tutuyor mu? Boşluklar kırpılmış değer beklenir.</summary>
    public static bool Gecerli(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > EnFazlaUzunluk)
        {
            return false;
        }

        try
        {
            return Desen.IsMatch(token);
        }
        catch (RegexMatchTimeoutException)
        {
            // Desen doğrusal, zaman aşımı beklenmiyor; olursa güvenli yön: reddet.
            return false;
        }
    }

    /// <summary>
    /// Günlüğe yazılabilir hâl: <c>ExponentPushToken[…a1b2c3]</c>. Biçim tutmuyorsa yalnızca
    /// son altı karakter; kısa bir değerde o da gösterilmez (tamamını ele verirdi).
    /// </summary>
    public static string Maskele(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return "(yok)";
        }

        var ac = token.IndexOf('[', StringComparison.Ordinal);
        if (ac > 0 && token.EndsWith(']'))
        {
            var ic = token[(ac + 1)..^1];
            return ic.Length > GorunenKarakter * 2
                ? $"{token[..(ac + 1)]}…{ic[^GorunenKarakter..]}]"
                : $"{token[..(ac + 1)]}…]";
        }

        return token.Length > GorunenKarakter * 2 ? "…" + token[^GorunenKarakter..] : "…";
    }
}
