using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using PeerLearn.Api.Controllers;   // GetUserIdOrNull — genel sınırın bölüm anahtarı
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

            /*
              ⚠️ AddFixedWindowLimiter DEĞİL, AddPolicy — ve bu fark üründe bir hataydı
              (2026-08-27'de canlıya çıkış denetiminde bulundu).

              `AddFixedWindowLimiter(AuthPolicy, o => ...)` aşırı yüklemesi isteğe hiç
              erişmiyor: politika adı başına TEK bir kova kuruyor. Yani sınır IP başına
              değil, TÜM SİTE için ortaktı. XML yorumu ve docs/URETIME-CIKIS.md "IP
              başına dakikada" diyordu; kod bunu yapmıyordu.

              Sonuç iki uçta da kötüydü: belgedeki güvenli değerle (10/dk) açılış günü
              onbirinci kullanıcı — kim olursa olsun — giriş yapamaz; geliştirme değeriyle
              (2000/dk) giriş ucu parola denemesine açık kalır. Bu tuzağı testler de
              yakalayamıyor: verify-production-guard.ps1 tek IP'den istek atıyor, tek
              kova ile IP başına kova aynı sonucu veriyor.

              AddPolicy her istek için bölüm anahtarını hesaplatıyor; aşağıdaki genel
              sınırın kalıbıyla aynı (o zaten doğru yazılmıştı).

              ⚠️ TERS VEKİL ARKASINDA RemoteIpAddress vekilin IP'sidir ve o durumda bu
              sınır yine tek kovaya döner. Program.cs'te UseForwardedHeaders çağrılmadan
              üretime çıkılırsa buradaki düzeltme etkisiz kalır — ikisi birlikte anlamlı.
            */
            limiter.AddPolicy(AuthPolicy, context =>
            {
                var key = context.Connection.RemoteIpAddress?.ToString() ?? "bilinmeyen";

                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.AuthPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = options.QueueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                });
            });

            /*
              ⛔ GENEL SINIR KİMLİK DOĞRULANMIŞSA KULLANICI BAŞINA (2026-09-05).

              Eskiden burada da anahtar IP idi ve buradaki yorum "saldırı zaten giriş
              YAPMADAN geliyor" diyerek bunu savunuyordu. O gerekçe KİMLİK POLİTİKASI için
              doğru (aşağıda değil, yukarıda; orası IP'de KALIYOR) ama genel sınır için
              yanlıştı: genel sınırın yönettiği trafik neredeyse tamamen GİRİŞ YAPMIŞ
              kullanıcıların olağan uygulama kullanımı.

              CGNAT bunu bir arızaya çeviriyordu. Mobil operatörler binlerce aboneyi tek
              genel IP'nin arkasına koyuyor. Maliyet karşılaştırması:

                giriş           kullanıcı başına ~2 saatte BİR istek
                uygulama açmak  kullanıcı hiçbir şeye dokunmadan ≥4 istek
                gezinme         dakikada 10-20 istek

              Yani tek bir CGNAT IP'sinde 300/dk tavanı ~15-30 EŞZAMANLI kullanıcıda
              doluyor; kimlik tavanı ise aynı kümede dakikada 0,2 girişe denk geliyor —
              iki mertebe uzak. Mağazaya çıkıldığında ilk 429 dalgası giriş ekranından
              DEĞİL, uygulama içi gezinmeden gelecekti ve teşhisi çok zor olurdu:
              kullanıcıların bir kısmı çalışıyor, bir kısmı çalışmıyor, sunucu sağlıklı.

              GÜVENLİK GERİLEMESİ YOK. Kendi kovasını almanın bedeli GEÇERLİ BİR JWT ve
              onu almak için kayıt + giriş gerekiyor; o yol da yukarıdaki kimlik
              politikasıyla IP başına 10/dk'ya kilitli. Kimliksiz her istek (token yok,
              süresi dolmuş, imza tutmuyor) eskisi gibi IP kovasına düşüyor.

              ⚠️ BU, Program.cs'te UseAuthentication'ın UseRateLimiter'dan ÖNCE
              çağrılmasına bağlı. Sıra ters olursa HttpContext.User burada boştur, her
              istek IP kovasına düşer ve düzeltme SESSİZCE etkisiz kalır — hata vermez,
              yalnızca eski davranışa döner.

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

                // Önek ŞART: kullanıcı kimliği ile IP aynı anahtar uzayına düşerse
                // (teoride) çakışabilirdi. Ayrıca günlükte hangi kovanın dolduğu okunur.
                var userId = context.User.GetUserIdOrNull();
                var key = userId is not null
                    ? $"u:{userId}"
                    : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "bilinmeyen"}";

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
