using PeerLearn.Application.Common;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Features.Identity;

/// <summary>
/// Doğrulama e-postasının gövdesi — TEK yerde.
///
/// NEDEN AYRI SINIF: aynı metni iki handler gönderiyor (ilk kayıt ve yeniden gönderim).
/// Metin ikisinde ayrı ayrı yazılıydı ve bir düzeltme birine uygulanıp diğerine
/// uygulanmadığında, hatanın görüneceği yer hatanın yapıldığı yerden başkası oluyordu.
/// </summary>
/// <remarks>
/// ─── BAĞLANTI KALDIRILDI, KOD GELDİ (2026-09-02) ─────────────────────────────
/// Eskiden gövde bir doğrulama BAĞLANTISI taşıyordu (<c>/dogrula?token=…</c>) ve o
/// yüzden burası <c>PublicWebUrl</c> ayarına bağımlıydı: ayar boşsa e-posta çıplak bir
/// JWT taşıyor, kullanıcı 300 karakterlik bir dizeyi elle kopyalamak zorunda kalıyordu
/// (mobilde pratikte imkânsız).
///
/// Kod bu bağımlılığı tümüyle kaldırdı: gövdenin artık alan adına ihtiyacı YOK, yani
/// yanlış yapılandırılmış bir <c>PublicWebUrl</c> doğrulamayı bozamıyor.
/// </remarks>
public static class DogrulamaEpostasi
{
    /// <summary>
    /// Arayüzdeki doğrulama sayfasının yolu.
    /// </summary>
    /// <remarks>
    /// Artık e-postaya KONMUYOR (kod var, bağlantı yok) ama sabit duruyor: kullanıcı
    /// kayıt ekranından ayrılmışsa arayüz onu buraya yönlendiriyor ve iki yerde ayrı
    /// yazılmış bir yol, biri değişince sessizce kırılırdı.
    /// </remarks>
    public const string Yol = "/dogrula";

    public static string Konu(bool yenidenGonderim) =>
        yenidenGonderim
            ? $"{Branding.ProductName} doğrulama kodu (yeniden gönderim)"
            : $"{Branding.ProductName} doğrulama kodu";

    /// <summary>
    /// Kodu taşıyan gövde. Alan adı ya da başka bir ayar GEREKTİRMİYOR.
    /// </summary>
    /// <remarks>
    /// Kod KENDİ SATIRINDA ve etrafında boşlukla: posta istemcileri bitişik metindeki
    /// sayıyı telefon numarası sanıp bağlantıya çeviriyor, dokunmatik ekranda kodu
    /// seçmeye çalışan kullanıcı arama ekranına düşüyordu.
    ///
    /// Süre gövdede YAZILI: "kod çalışmıyor" diye destek yazan kullanıcıların çoğu
    /// süresi dolmuş kodu deniyor. Süreyi söylemek o mesajların çoğunu önlüyor.
    /// </remarks>
    public static string Govde(string kod) =>
        $"""
         Merhaba,

         {Branding.ProductName} hesabını doğrulamak için bu kodu gir:

             {kod}

         Kod {EmailVerificationRules.ValidityMinutes} dakika geçerli.
         Bu isteği sen yapmadıysan bu e-postayı yok sayabilirsin.
         """;
}
