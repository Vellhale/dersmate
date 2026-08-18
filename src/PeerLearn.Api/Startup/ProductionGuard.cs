using PeerLearn.Application.Options;

namespace PeerLearn.Api.Startup;

/// <summary>
/// Üretim ayar kapısı: geliştirme değerleriyle canlıya çıkmayı İMKÂNSIZ kılar.
///
/// NEDEN "UYAR" DEĞİL "ÇÖKERT": projede tek bir appsettings.json var ve içindeki değerler
/// geliştirme için seçilmiş (paylaşılan JWT anahtarı, DB parolası, doğrulama token'ının
/// yanıtta dönmesi, CORS'un yalnızca localhost'a açık olması). Bunlardan biri üretime
/// sızarsa sonucu sessizdir: uygulama sorunsuz çalışır, güvenlik yoktur. Sessiz bir risk
/// yerine AÇILIŞTA duran bir süreç yeğdir — hata mesajı neyin yanlış olduğunu ve nasıl
/// düzeltileceğini söyler.
///
/// Değerler ASP.NET'in standart kaynaklarından gelir; en pratiği ortam değişkenleridir:
///   Jwt__Key=...            ConnectionStrings__Postgres=...     Cors__Origins__0=https://...
///   Jwt__ExposeVerificationTokenInResponse=false                Email__Provider=Smtp
/// </summary>
public static class ProductionGuard
{
    /// <summary>Depoya işlenmiş geliştirme değerleri — üretimde görülürse süreç durur.</summary>
    private const string DevJwtKeyMarker = "DEV-ONLY-KEY";
    private const string DevDbPasswordMarker = "PeerLearnDev2026";
    private const int MinJwtKeyLength = 32;

    public static void EnsureSafeForProduction(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        var sorunlar = new List<string>();

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Contains(DevJwtKeyMarker, StringComparison.Ordinal))
        {
            sorunlar.Add("Jwt:Key hâlâ depodaki geliştirme anahtarı. Bu anahtarla üretilen her " +
                         "token'ı kaynak koda erişen herkes üretebilir. Ortam değişkeni: Jwt__Key");
        }
        else if (jwt.Key.Length < MinJwtKeyLength)
        {
            sorunlar.Add($"Jwt:Key en az {MinJwtKeyLength} karakter olmalı (şu an {jwt.Key.Length}).");
        }

        if (jwt.ExposeVerificationTokenInResponse)
        {
            sorunlar.Add("Jwt:ExposeVerificationTokenInResponse=true — doğrulama token'ı kayıt " +
                         "yanıtında dönüyor. Bu açıkken e-posta SAHİPLİĞİ hiç kanıtlanmaz: " +
                         "herkes başkasının adresiyle kayıt olup hesabı doğrulayabilir.");
        }

        var postgres = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(postgres))
        {
            sorunlar.Add("ConnectionStrings:Postgres boş.");
        }
        else if (postgres.Contains(DevDbPasswordMarker, StringComparison.Ordinal))
        {
            sorunlar.Add("ConnectionStrings:Postgres depodaki geliştirme parolasını içeriyor.");
        }

        var corsOrigins = configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
        if (corsOrigins.Length == 0)
        {
            sorunlar.Add("Cors:Origins boş — arayüz API'ye hiç erişemez.");
        }
        else if (corsOrigins.Any(o => o.Contains("localhost", StringComparison.OrdinalIgnoreCase)))
        {
            sorunlar.Add("Cors:Origins hâlâ localhost içeriyor; üretim alan adını yazın.");
        }

        // E-posta gönderilemiyorsa kayıt akışı üretimde TAMAMEN tıkanır: doğrulama token'ı
        // yalnızca e-postayla gidiyor (yukarıdaki kural gereği yanıtta dönmüyor).
        var emailProvider = configuration["Email:Provider"];
        if (string.IsNullOrWhiteSpace(emailProvider) ||
            emailProvider.Equals("Log", StringComparison.OrdinalIgnoreCase))
        {
            sorunlar.Add("Email:Provider ayarlanmamış (ya da 'Log'). Gerçek gönderici olmadan " +
                         "hiçbir kullanıcı hesabını doğrulayamaz. Ortam değişkeni: Email__Provider=Smtp");
        }

        if (sorunlar.Count == 0)
        {
            return;
        }

        var mesaj = "ÜRETİM AYARLARI GÜVENLİ DEĞİL — uygulama başlatılmadı:" +
                    Environment.NewLine +
                    string.Join(Environment.NewLine, sorunlar.Select((s, i) => $"  {i + 1}. {s}"));

        throw new InvalidOperationException(mesaj);
    }
}
