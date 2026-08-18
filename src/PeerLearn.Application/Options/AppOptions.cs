namespace PeerLearn.Application.Options;

public sealed class EconomyOptions
{
    public const string SectionName = "Economy";

    /// <summary>E-posta doğrulaması sonrası verilen tek seferlik kredi.</summary>
    public int WelcomeCreditAmount { get; set; } = 1;

    /// <summary>Hoş geldin kredisinin ömrü (istifi önlemek için kısa tutulur).</summary>
    public int WelcomeCreditValidityDays { get; set; } = 14;

    /// <summary>Ders anlatarak kazanılan kredinin ömrü — iş kuralı: 30 gün.</summary>
    public int EarnedCreditValidityDays { get; set; } = 30;

    /// <summary>Öğrenci bu süre içinde onay/itiraz vermezse ders otomatik onaylanır.</summary>
    public int AutoApproveHours { get; set; } = 48;

    /// <summary>Ders bitiminden sonra eğitmen hiç tamamlama istemezse rezervasyonun düşürülme süresi.</summary>
    public int BookingExpireGraceDays { get; set; } = 7;

    /// <summary>Cüzdan kilidi bekleme üst sınırı.</summary>
    public int LockTimeoutSeconds { get; set; } = 10;
}

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = null!;

    /*
      ⚠️ "PeerLearn" BURADA BİLİNÇLİ OLARAK KALIYOR — kullanıcıya görünen ürün adı
      dersmate olsa bile (bkz. docs/DEVAM-EDILECEK.md, F4).

      Issuer/Audience token DOĞRULAMASINA girer: dağıtılmış her erişim token'ı bu iki
      değerle imzalanmıştır ve doğrulayıcı birebir eşleşme arar. Değiştirmek, o anda
      geçerli TÜM oturumları anında geçersiz kılar — herkes tek seferde dışarı atılır.
      Bunlar bir marka dizesi değil, protokol tanımlayıcısıdır.
    */
    public string Issuer { get; set; } = "PeerLearn";
    public string Audience { get; set; } = "PeerLearn";
    public int AccessTokenMinutes { get; set; } = 120;
    public int EmailVerifyTokenHours { get; set; } = 24;

    /// <summary>
    /// true → doğrulama token'ı register YANITINDA da döner (yalnızca geliştirme kolaylığı!).
    /// Production'da false kalmalı: aksi halde e-posta sahipliği hiç kanıtlanmadan hesap
    /// doğrulanabilir. Varsayılan güvenli taraftadır (false).
    /// </summary>
    public bool ExposeVerificationTokenInResponse { get; set; }
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>"Log" (geliştirme, konsola yazar) veya "Smtp" (gerçek gönderim).</summary>
    public string Provider { get; set; } = "Log";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = "noreply@peerlearn.local";
}

/// <summary>
/// İstek hızı sınırları. Varsayılanlar ÜRETİM için güvenli seçildi; geliştirme
/// appsettings'i bunları bilerek yükseltir (uçtan uca testler saniyeler içinde yüzlerce
/// kayıt/giriş isteği atıyor).
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    /// <summary>Kimlik uçları (kayıt/giriş/yeniden gönderim) — IP başına, dakikada.</summary>
    public int AuthPerMinute { get; set; } = 10;

    /// <summary>Diğer tüm uçlar — IP başına, dakikada.</summary>
    public int GlobalPerMinute { get; set; } = 300;

    /// <summary>Sınıra takılan istek kuyruğa alınmaz; 0 = anında 429.</summary>
    public int QueueLimit { get; set; }
}
