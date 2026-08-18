using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using PeerLearn.Application.Options;

namespace PeerLearn.Api.Startup;

/// <summary>
/// İstek hızı sınırlama.
///
/// NEDEN İKİ AYRI SINIR: kimlik uçları (kayıt/giriş/doğrulama yeniden gönderimi) ile
/// sıradan okuma uçlarının kötüye kullanım profili aynı değil. Girişe saniyede yüz deneme
/// yapmak parola denemesi demektir; ders listesini sık çekmek yalnızca gürültüdür. Tek bir
/// genel sınır, ya parolayı korumaz ya da normal kullanımı boğar.
///
/// PENCERE YERİNE SABİT PENCERE (FixedWindow): anlaşılması ve ayarlanması en kolay olan.
/// Kayan pencerenin ek doğruluğu bu üründe bir şey kazandırmıyor.
///
/// SINIRLAR AYARDAN GELİR ve varsayılanları ÜRETİM için seçilmiştir; geliştirme
/// appsettings'i bilerek yükseltir — uçtan uca testler saniyeler içinde yüzlerce kayıt
/// isteği atıyor ve üretim sınırıyla koşarlarsa test altyapısı yanlış yere kırmızı verir.
/// </summary>
public static class RateLimiting
{
    /// <summary>Kimlik uçlarına uygulanan politika adı (controller'da [EnableRateLimiting]).</summary>
    public const string AuthPolicy = "auth";

    public static IServiceCollection AddPeerLearnRateLimiter(
        this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()
                      ?? new RateLimitOptions();

        services.AddRateLimiter(limiter =>
        {
            // Sınıra takılan istemci 429 alır — varsayılan 503 değil: 429 "yavaşla" demektir
            // ve istemci tarafında yeniden deneme kararı buna göre verilir.
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.AddFixedWindowLimiter(AuthPolicy, o =>
            {
                o.PermitLimit = options.AuthPerMinute;
                o.Window = TimeSpan.FromMinutes(1);
                o.QueueLimit = options.QueueLimit;
                o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            /*
              Genel sınır IP başına bölümlenir. Kimlik doğrulanmış kullanıcıyı anahtar
              yapmak daha adil olurdu ama saldırı zaten giriş YAPMADAN geliyor; kullanıcıya
              göre bölümlemek korunması gereken yüzeyi korumasız bırakırdı.

              Sağlık uçları sınır DIŞI: yük dengeleyicinin yoklaması, kullanıcı trafiğiyle
              aynı kovaya girip 429 almamalı — aksi halde yoğunlukta sağlıklı instance
              "ölü" sayılıp havuzdan düşerdi.
            */
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (context.Request.Path.StartsWithSegments("/health"))
                {
                    return RateLimitPartition.GetNoLimiter("health");
                }

                var key = context.Connection.RemoteIpAddress?.ToString() ?? "bilinmeyen";

                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.GlobalPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = options.QueueLimit
                });
            });

            limiter.OnRejected = async (context, ct) =>
            {
                // Gövde, uygulamanın geri kalanıyla AYNI hata biçiminde: istemci tek bir
                // ayrıştırma yolu kullanabilsin (bkz. lib/api.js ApiError).
                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    title = "RATE_LIMITED",
                    status = StatusCodes.Status429TooManyRequests,
                    detail = "Çok fazla istek gönderdiniz. Lütfen biraz bekleyip tekrar deneyin."
                }, ct);
            };
        });

        return services;
    }
}
