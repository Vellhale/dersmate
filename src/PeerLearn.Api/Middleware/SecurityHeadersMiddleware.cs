namespace PeerLearn.Api.Middleware;

/// <summary>
/// Her yanıta güvenlik başlıklarını ekler. Bu API TLS'i kendisi sonlandırmadığı gibi
/// arayüzü de SERVİS ETMEZ (SPA'yı nginx, kendi kaynağından — www — sunuyor). Yani buradaki
/// başlıklar yalnızca API kaynağını (JSON + kullanıcı dosyaları) kapsar; SPA'nın CSP'si
/// nginx'te ayrı yaşar (tools/ornek-nginx.conf, arayüz bloğu).
///
/// Neden middleware ve neden BURADA: başlıklar hata yanıtlarına, 401/403/429'lara ve
/// dosya yanıtlarına da inmeli. Bu yüzden boru hattının başında (UseForwardedHeaders'tan
/// hemen sonra) kayıtlı — kendisinden sonraki her şeyi sarar. Başlıkları _next'ten ÖNCE
/// yazıyoruz: yanıt henüz başlamadığı için aşağıdaki hiçbir katman (ExceptionHandling dahil,
/// o da yanıtı temizlemiyor) bunları düşürmez.
///
/// CSP burada BİLEREK kilitli: API bir belge (script/stil yükleyen HTML) döndürmüyor,
/// dolayısıyla 'none' hiçbir akışı kırmaz ama kötü niyetli/yanlış tür yüklenmiş bir dosya
/// tarayıcıda render edilse bile script çalıştıramaz. Swagger UI (yalnızca geliştirmede)
/// bu middleware'in ÜSTÜNDE kayıtlı (Program.cs'te önce gelir, kendi yolunu sonlandırır),
/// bu yüzden buradan hiç geçmez ve CSP onu etkilemez.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        // MIME tür tahminini kapat: yanlış/kötü niyetli Content-Type ile HTML/script'e
        // "koklayarak" yükseltme yapılmasın.
        headers["X-Content-Type-Options"] = "nosniff";

        // Clickjacking: API yanıtları hiçbir çerçeveye gömülmesin.
        headers["X-Frame-Options"] = "DENY";

        // Kaynaklar arası isteklerde tam URL (yol + sorgu) referrer olarak sızmasın.
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // API kaynağı hiçbir alt kaynak yüklemez ve çerçevelenmez; belge tabanlı saldırı
        // yüzeyini tamamen kapatır. Ayrıntı için sınıf özetine bakın.
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";

        return _next(context);
    }
}
