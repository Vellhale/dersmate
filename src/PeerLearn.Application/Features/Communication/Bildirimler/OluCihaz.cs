using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;

namespace PeerLearn.Application.Features.Communication.Bildirimler;

/// <summary>
/// Expo'nun "ölü" dediği (DeviceNotRegistered, başka projenin token'ı) cihaz satırlarının
/// silinmesi: satır, o kanıttan SONRA yeniden kaydedilmediyse.
/// </summary>
/// <remarks>
/// ─── NEDEN "Id İLE SİL" YETMİYOR ────────────────────────────────────────────
/// Kanıt ile silme arasında zaman var: makbuz 15 dakika ile 24 saat sonra soruluyor. iOS'ta
/// uygulamayı silip birkaç saat içinde yeniden kuran kullanıcının Expo token'ı AYNI kalabilir:
/// expo-notifications kurulum kimliğini Keychain'de tutuyor (ServerRegistrationModule.swift),
/// Keychain yeniden kurulumda silinmiyor ve token isteği bu kimlikle yapılıyor. (Token'ın
/// aynı dönmesi Expo sunucusunun davranışı; cihazda ölçülmedi.) Yeni kurulumun PUT'u
/// <c>ON CONFLICT ("Token")</c> ile AYNI satırı (aynı Id) günceller; eski biletin gecikmiş
/// DeviceNotRegistered makbuzu sonra gelip bu yeni ve geçerli kaydı silerdi. Kullanıcı
/// uygulamayı yeniden açana kadar hiç bildirim almaz, sunucuda iz kalmazdı.
///
/// Bu yüzden silme, çağıranın verdiği sınırla koşullu: <c>LastSeenAtUtc &lt;= sınır</c>.
/// Makbuzda sınır biletin yazıldığı an (gönderimden hemen sonra), dağıtıcıda bağlamda okunan
/// son görülme. Gerçekten ölü cihaz yeniden kaydolmadığı için yine silinir; yalnızca arada
/// yeniden kaydolan korunur. Korunan satır gerçekten ölüyse bir sonraki gönderim onu yeniden
/// yakalar.
///
/// LastSeenAtUtc kayıt ucunda uygulama saatiyle yazılıyor, sınırlar da uygulama saatinden;
/// iki sunucu kopyası arasındaki saat farkı yalnızca "yeniden kaydolma" penceresini o kadar
/// kaydırır — kanıttan saniyeler sonra kaydolmuş bir cihaz, farkın yönüne göre silinebilir
/// ya da bir tur daha korunabilir. İkisi de kalıcı hasar değil.
/// </remarks>
public static class OluCihaz
{
    /// <summary>
    /// <paramref name="cihazlar"/>: cihaz Id → sınır. Satır yoksa ya da sınırdan sonra
    /// yenilendiyse 0 satır, hata değil. Dönüş: silinen satır.
    /// </summary>
    public static async Task<int> SilAsync(IAppDbContext db, IReadOnlyDictionary<Guid, DateTime> cihazlar, CancellationToken ct)
    {
        if (cihazlar.Count == 0)
        {
            return 0;
        }

        var idler = cihazlar.Keys.ToArray();
        // Npgsql timestamptz'ye yalnızca Kind=Utc yazar (CLAUDE.md: ham SQL'e giden her DateTime).
        var sinirlar = idler.Select(id => DateTime.SpecifyKind(cihazlar[id], DateTimeKind.Utc)).ToArray();

        return await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM comms."PushDevices" AS d
            USING unnest({idler}, {sinirlar}) AS v(id, sinir)
            WHERE d."Id" = v.id AND d."LastSeenAtUtc" <= v.sinir
            """, ct);
    }
}
