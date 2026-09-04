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

    /// <summary>Kod içi güvenli varsayılanlar (RateLimitOptions). Üretimde bunların
    /// ÜSTÜNE çıkan bir değer, appsettings.json'daki geliştirme ayarının sızdığını gösterir.</summary>
    private const int GuvenliAuthPerMinute = 10;
    private const int GuvenliGlobalPerMinute = 300;

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

        /*
          REDIS — 2026-08-27'de eklendi ve eksikliği canlıya çıkış denetiminde bulundu.

          docs/URETIME-CIKIS.md bunu zorunlu tabloda listeliyordu ama kapı hiç bakmıyordu.
          Belgenin anlattığı zarif başarısızlık ("boş bırakılırsa süreç içine düşer") de
          gerçekleşmiyor: appsettings.json'da "localhost:6379" YAZILI, yani üretimde değer
          boş değil YANLIŞ olur. AbortOnConnectFail=false sayesinde uygulama açılır,
          /health yeşil yanar, ama kilit gerektiren her ekonomi işlemi (rezervasyon, ders
          onayı, puan basımı) bağlantı hatasıyla 500 döner. Sağlık ucu da yardım etmiyor:
          Redis Degraded olarak kayıtlı ve Degraded HTTP 200 demek.

          Bu yüzden kontrol "boş mu" değil, "hâlâ localhost mu": tek instance'ta bilerek
          Redis'siz çalışmak isteyen, bağlantı dizesini AÇIKÇA boşaltarak bunu beyan eder.
        */
        var redis = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis) &&
            redis.Contains("localhost", StringComparison.OrdinalIgnoreCase))
        {
            sorunlar.Add("ConnectionStrings:Redis hâlâ localhost. Üretimde uygulama AÇILIR ve " +
                         "/health yeşil yanar, ama kilit gerektiren her ekonomi işlemi 500 döner. " +
                         "Ortam değişkeni: ConnectionStrings__Redis. Tek instance'ta bilerek " +
                         "Redis'siz çalışacaksanız değeri açıkça BOŞ bırakın (süreç içi kilide düşer).");
        }

        // E-posta gönderilemiyorsa kayıt akışı üretimde TAMAMEN tıkanır: doğrulama token'ı
        // yalnızca e-postayla gidiyor (yukarıdaki kural gereği yanıtta dönmüyor).
        var emailProvider = configuration["Email:Provider"];
        var smtpSecildi = !string.IsNullOrWhiteSpace(emailProvider) &&
                          emailProvider.Equals("Smtp", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(emailProvider) ||
            emailProvider.Equals("Log", StringComparison.OrdinalIgnoreCase))
        {
            sorunlar.Add("Email:Provider ayarlanmamış (ya da 'Log'). Gerçek gönderici olmadan " +
                         "hiçbir kullanıcı hesabını doğrulayamaz. Ortam değişkeni: Email__Provider=Smtp");
        }
        else if (smtpSecildi)
        {
            /*
              Provider=Smtp KAPIYI GEÇMEYE YETİYORDU ve bu sessiz bir tuzaktı:
              SmtpEmailSender, Host boşken istisna FIRLATMIYOR, LogError yazıp dönüyor.
              Sonuç: kayıt ucu 200 döner, kullanıcı "e-postanı kontrol et" ekranını görür,
              hiçbir e-posta gitmez ve giriş doğrulanmamış hesaba kapalı olduğu için
              HİÇ KİMSE hesabını açamaz. Tek iz sunucu log'undaki bir ERROR satırı.
            */
            var email = configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>()
                        ?? new EmailOptions();

            if (string.IsNullOrWhiteSpace(email.Host))
            {
                sorunlar.Add("Email:Provider=Smtp ama Email:Host boş. Gönderici sessizce " +
                             "başarısız olur (istisna fırlatmaz), kimse hesabını doğrulayamaz. " +
                             "Ortam değişkeni: Email__Host");
            }

            if (string.IsNullOrWhiteSpace(email.FromAddress) ||
                email.FromAddress.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            {
                sorunlar.Add("Email:FromAddress hâlâ varsayılan (.local). Çoğu SMTP sunucusu " +
                             "böyle bir gönderen adresini reddeder. Ortam değişkeni: Email__FromAddress");
            }

            /*
              ⚠️ BU KAPI DURUYOR AMA GEREKÇESİ DEĞİŞTİ (2026-09-04).

              Eskiden buradaki gerekçe DOĞRULAMA e-postasıydı: gövde bir bağlantı taşıyordu
              ve PublicWebUrl boşsa kullanıcıdan 300 karakterlik bir JWT'yi elle kopyalaması
              isteniyordu. 2 Eylül'de doğrulama 6 haneli koda geçti ve o bağımlılık tümüyle
              kalktı — DogrulamaEpostasi.Govde artık alan adına hiç bakmıyor.

              Kapıyı bu yüzden kaldırmak YANLIŞ olurdu: PublicWebUrl hâlâ kullanılıyor,
              yalnızca başka bir yerde. ForgotPassword.cs → ParolaSifirlama.Govde(token, url)
              parola sıfırlamayı hâlâ TIKLANABİLİR BAĞLANTI ile gönderiyor ve ParolaSifirlama
              boş adreste çıplak token'a düşüyor.

              Yani eski gerekçe yanlış, kapının kendisi doğru. Yanlış gerekçeli bir kapı
              tehlikelidir: onu okuyan bir sonraki kişi "doğrulama artık kod kullanıyor,
              bu kontrol bayatlamış" deyip kaldırır ve parola sıfırlamayı sessizce kırar —
              hem de yalnızca ÜRETİMDE, çünkü geliştirmede adres zaten boş bırakılıyor.
            */
            if (string.IsNullOrWhiteSpace(email.PublicWebUrl))
            {
                sorunlar.Add("Email:PublicWebUrl boş — PAROLA SIFIRLAMA e-postası tıklanabilir " +
                             "bağlantı yerine çıplak token taşır ve kullanıcı onu elle kopyalamak " +
                             "zorunda kalır (ParolaSifirlama.Govde). Doğrulama e-postası bu ayara " +
                             "artık bağlı değil, kod kullanıyor. Arayüzün genel adresini verin: " +
                             "Email__PublicWebUrl=https://…");
            }
        }

        /*
          HIZ SINIRI — bu kapının denetlemediği son ayardı ve "varsayılanlar güvenlidir"
          sanısı burada YANLIŞ.

          RateLimitOptions'ta kod içi varsayılanlar 10 / 300. Ama appsettings.json üretimde
          de taban yapılandırma olarak yüklenir ve içindeki GELİŞTİRME değerleri
          (AuthPerMinute: 2000, GlobalPerMinute: 20000 — uçtan uca testler saniyeler içinde
          yüzlerce istek attığı için bilerek yükseltilmiş) o varsayılanları EZER. Depoda
          appsettings.Production.json yok, yani ortam değişkeni verilmezse giriş ucu
          üretimde dakikada 2000 parola denemesine açık kalır.

          Belge bunu yazıyor (URETIME-CIKIS.md §1) ama belgedeki bir uyarı, atlanabilir bir
          adımdır; buradaki kontrol atlanamaz.
        */
        var rateLimit = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()
                        ?? new RateLimitOptions();

        if (rateLimit.AuthPerMinute > GuvenliAuthPerMinute)
        {
            sorunlar.Add($"RateLimit:AuthPerMinute = {rateLimit.AuthPerMinute} — appsettings.json'daki " +
                         $"geliştirme değeri üretime sızmış. Kayıt/giriş ucu IP başına dakikada bu kadar " +
                         $"denemeye açık kalır (kaba kuvvet). Ortam değişkeni: " +
                         $"RateLimit__AuthPerMinute={GuvenliAuthPerMinute}");
        }

        if (rateLimit.GlobalPerMinute > GuvenliGlobalPerMinute)
        {
            sorunlar.Add($"RateLimit:GlobalPerMinute = {rateLimit.GlobalPerMinute} — geliştirme değeri " +
                         $"üretime sızmış. Ortam değişkeni: RateLimit__GlobalPerMinute={GuvenliGlobalPerMinute}");
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
