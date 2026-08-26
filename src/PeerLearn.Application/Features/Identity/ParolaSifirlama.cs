using System.Security.Cryptography;
using System.Text;
using PeerLearn.Application.Common;

namespace PeerLearn.Application.Features.Identity;

/// <summary>
/// Parola sıfırlama akışının ORTAK parçaları: token amacı, ömrü ve e-posta gövdesi.
///
/// ─── TOKEN NEDEN TEK KULLANIMLIK ────────────────────────────────────────────────
/// Purpose token'lar durumsuz (DB tablosu yok, JWT imzalı). Düz kullanımda bu şu
/// açığı bırakırdı: sıfırlama bağlantısı, parola DEĞİŞTİKTEN SONRA da ömrü dolana
/// kadar geçerli kalır. Postasına bir kez erişen biri (ortak bilgisayar, ele geçmiş
/// e-posta, iletilmiş mektup) saatler sonra aynı bağlantıyla parolayı yeniden
/// değiştirebilirdi.
///
/// Çözüm ŞEMA DEĞİŞTİRMEDEN: token'ın "purpose" alanı, kullanıcının O ANKİ parola
/// hash'inin damgasını taşıyor. Parola değişince hash de değişir (ASP.NET
/// PasswordHasher her hash'te rastgele tuz üretir — aynı parolayı yeniden koysanız
/// bile damga başkalaşır), damga değişince eski token'ın purpose'u artık eşleşmez ve
/// ValidatePurposeToken null döner. Yani bağlantı KULLANILDIĞI ANDA ölür.
///
/// Damga hash'in KENDİSİ değil, SHA-256'sının ilk 16 karakteri: parola hash'i token'ın
/// içinde okunabilir biçimde dolaşmamalı (JWT gövdesi yalnızca base64, şifreli değil).
///
/// ⚠️ BİLİNEN SINIR: parola değişince AÇIK OTURUMLAR düşmüyor. Erişim token'ları
/// durumsuz ve 2 saat ömürlü; "her yerden çıkış yap" için kullanıcı başına bir
/// token sürümü (User tablosunda bir sayaç) gerekir. Hesabı ele geçirilen kullanıcı
/// için pratikte anlamı: saldırganın açık oturumu en fazla 2 saat daha yaşar.
/// Yaptırım yolu (ban/askı) AccountStatusMiddleware ile ANINDA etkili, o ayrı.
/// ─────────────────────────────────────────────────────────────────────────────────
/// </summary>
public static class ParolaSifirlama
{
    /// <summary>Arayüzdeki rota — frontend/src/App.jsx ile eşleşmek ZORUNDA.</summary>
    public const string Yol = "/sifre-sifirla";

    /// <summary>
    /// 1 saat. Doğrulama token'ından (24 saat) bilerek KISA: doğrulama e-postası
    /// kullanıcının kendi isteğiyle başlattığı bir akışın parçası, sıfırlama ise
    /// hesabı ele geçirmenin en kısa yolu. Kısa ömür, postaya sonradan erişen birinin
    /// penceresini daraltıyor.
    /// </summary>
    public static TimeSpan Omur => TimeSpan.FromHours(1);

    /// <summary>
    /// Token amacı: sabit önek + parola hash'inin damgası. Damga sayesinde token,
    /// üretildiği andaki parolaya bağlı kalıyor (bkz. sınıf açıklaması).
    /// </summary>
    public static string Purpose(string parolaHash)
    {
        var damga = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parolaHash)));
        return $"password-reset:{damga[..16]}";
    }

    public static string Konu() => $"{Branding.ProductName} parola sıfırlama";

    /// <param name="genelAdres">
    /// EmailOptions.PublicWebUrl. Boşsa (geliştirme) gövde çıplak token taşır;
    /// e-posta "Log" sağlayıcısıyla konsola yazıldığı için bağlantı zaten tıklanmıyor.
    /// </param>
    public static string Govde(string token, string genelAdres)
    {
        if (string.IsNullOrWhiteSpace(genelAdres))
        {
            return $"Parola sıfırlama token'ınız: {token}";
        }

        var taban = genelAdres.TrimEnd('/');
        var baglanti = $"{taban}{Yol}?token={Uri.EscapeDataString(token)}";

        return $"""
                Merhaba,

                {Branding.ProductName} hesabının parolasını sıfırlamak için aşağıdaki
                bağlantıya tıkla:

                {baglanti}

                Bağlantı 1 saat geçerli ve yalnızca BİR KEZ kullanılabilir.

                Bu isteği sen yapmadıysan hiçbir şey yapmana gerek yok: parolan
                değişmedi ve bu bağlantı kullanılmadığı sürece bir şey olmaz.
                """;
    }
}
