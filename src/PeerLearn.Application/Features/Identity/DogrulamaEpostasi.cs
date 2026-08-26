using PeerLearn.Application.Common;

namespace PeerLearn.Application.Features.Identity;

/// <summary>
/// Doğrulama e-postasının gövdesi — TEK yerde.
///
/// NEDEN AYRI SINIF: aynı metni iki handler gönderiyor (ilk kayıt ve yeniden gönderim).
/// Metin ikisinde ayrı ayrı yazılıydı ve bağlantı eklenirken birine eklenip diğerine
/// eklenmemesi, "yeniden gönder"e basan kullanıcının elinde çıplak token kalması demekti —
/// yani hatanın görüneceği yer, hatanın yapıldığı yerden başkası olurdu.
///
/// ⚠️ BAĞLANTININ YOLU ARAYÜZLE EŞLEŞMEK ZORUNDA: /dogrula?token=... rotası
/// frontend/src/App.jsx'te tanımlı ve VerifyEmail.jsx sorgu dizesinden `token` okuyor.
/// Rota değişirse burası da değişmeli; ikisi arasında derleyici bağı yok.
/// </summary>
public static class DogrulamaEpostasi
{
    public const string Yol = "/dogrula";

    public static string Konu(bool yenidenGonderim) =>
        yenidenGonderim
            ? $"{Branding.ProductName} e-posta doğrulama (yeniden gönderim)"
            : $"{Branding.ProductName} e-posta doğrulama";

    /// <param name="genelAdres">
    /// EmailOptions.PublicWebUrl. Boşsa (geliştirme) gövde çıplak token taşır —
    /// e-posta "Log" sağlayıcısıyla konsola yazıldığı için bağlantı zaten tıklanmıyor.
    /// </param>
    public static string Govde(string token, string genelAdres)
    {
        if (string.IsNullOrWhiteSpace(genelAdres))
        {
            return $"Doğrulama token'ınız: {token}";
        }

        // TrimEnd('/'): ayar "https://dersmate.com/" diye verilirse çift eğik çizgi
        // oluşuyor ve bazı e-posta istemcileri bağlantıyı orada kesiyor.
        var taban = genelAdres.TrimEnd('/');
        var baglanti = $"{taban}{Yol}?token={Uri.EscapeDataString(token)}";

        return $"""
                Merhaba,

                {Branding.ProductName} hesabını doğrulamak için aşağıdaki bağlantıya tıkla:

                {baglanti}

                Bağlantı tıklanmıyorsa adres çubuğuna kopyalayabilirsin.
                Bu isteği sen yapmadıysan bu e-postayı yok sayabilirsin.
                """;
    }
}
