using Microsoft.AspNetCore.Mvc;

namespace PeerLearn.Api.Controllers;

/// <summary>
/// Kullanıcının yüklediği dosyaları (ders kanıtı, öğrenci belgesi, avatar) servis eden
/// uçların ORTAK yolu. Tek yerde toplanmasının sebebi güvenlik: içerik türü yüklemede
/// doğrulansa da, tarayıcıya "bunu bir belge gibi çalıştırma" demek son savunma hattıdır.
///
/// <c>Content-Disposition: attachment</c> — dosyaya doğrudan gidilse bile tarayıcı onu
/// GÖRÜNTÜLEMEZ, indirir. Böylece içeriğine kullanıcının karar verdiği bir HTML/SVG, API
/// kaynağında (script çalıştırabildiği yerde) hiçbir zaman render edilmez; depolanmış XSS
/// bu noktada ölür. <c>X-Content-Type-Options: nosniff</c> zaten tüm yanıtlara
/// SecurityHeadersMiddleware tarafından ekleniyor; buradaki attachment onu tamamlar.
///
/// Arayüz bu uçları fetch + blob (object URL) ile tükettiği için attachment görüntülemeyi
/// BOZMAZ: blob URL'i Content-Disposition'a bakmaz. Yani savunma bedavaya geliyor.
///
/// Not: dosya adı bilerek verilmiyor. FileStreamResult, indirme adı boşken
/// Content-Disposition'ı KENDİSİ yazmaz; böylece buradaki başlık olduğu gibi kalır.
/// </summary>
public static class KullaniciDosyaServisi
{
    public static IActionResult KullaniciDosyasi(
        this ControllerBase controller, Stream content, string contentType)
    {
        controller.Response.Headers["Content-Disposition"] = "attachment";
        return controller.File(content, contentType);
    }

    /// <summary>Belge içeriği bellekte (byte[]) tutulan uçlar için aynı savunma.</summary>
    public static IActionResult KullaniciDosyasi(
        this ControllerBase controller, byte[] content, string contentType)
    {
        controller.Response.Headers["Content-Disposition"] = "attachment";
        return controller.File(content, contentType);
    }
}
