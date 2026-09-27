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

    /// <summary>
    /// Arayüzün genel adresi — e-postadaki bağlantılar buradan kurulur
    /// (ör. https://dersmate.com → https://dersmate.com/dogrula?token=...).
    ///
    /// NEDEN E-POSTA BÖLÜMÜNDE: bu değerin cevapladığı soru "beni kim çağırabilir"
    /// (Cors:Origins) değil, "gönderdiğim mektuptaki bağlantı nereye gitsin". İkisi
    /// üretimde aynı alan adı olsa da farklı iki karar; aynı ayara bağlanırlarsa
    /// birini değiştiren diğerini fark etmeden bozar.
    ///
    /// BOŞ BIRAKILIRSA e-posta bağlantı yerine çıplak token taşır (geliştirme davranışı).
    /// Üretimde ProductionGuard bunu zorunlu kılıyor: bağlantısız doğrulama e-postası,
    /// kullanıcıdan 300 karakterlik bir JWT'yi elle kopyalamasını istemek demek ve
    /// mobilde pratikte yapılamıyor.
    /// </summary>
    public string PublicWebUrl { get; set; } = string.Empty;
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

    /// <summary>
    /// Diğer tüm uçlar. Kimlik doğrulanmışsa KULLANICI başına, değilse IP başına; dakikada.
    /// Bölümleme 2026-09-05'te IP'den kullanıcıya taşındı (bkz. RateLimiting.cs).
    /// </summary>
    public int GlobalPerMinute { get; set; } = 300;

    /// <summary>Sınıra takılan istek kuyruğa alınmaz; 0 = anında 429.</summary>
    public int QueueLimit { get; set; }

    /// <summary>
    /// ÜRETİMDE güvenli tavanların ÜSTÜNE çıkmaya açık izin. Normalde ProductionGuard
    /// AuthPerMinute &gt; 10 ya da GlobalPerMinute &gt; 300 görürse süreci durdurur;
    /// bu bayrak o kapıyı bilinçli olarak açar ve açılışta uyarı loglanır.
    ///
    /// NEDEN VAR: kapı tek yönlüydü — geliştirme değerinin üretime sızmasını engelliyor
    /// ama sınırı BİLEREK yükseltmenin hiçbir yolunu bırakmıyordu. CGNAT kaynaklı bir 429
    /// dalgası canlıda başladığında elde ayar dosyasıyla yapılabilecek tek hafifletme
    /// yoktu; kod değişikliği + derleme + dağıtım gerekiyordu, yani olay anında saatler.
    ///
    /// Ortam değişkeni: RateLimit__YuksekSinirBilerek=true
    /// </summary>
    public bool YuksekSinirBilerek { get; set; }
}

/// <summary>
/// Push bildirimleri (Expo Push Service). E-posta ayarlarıyla aynı kalıp: "Log" geliştirme,
/// "Expo" gerçek gönderim.
/// </summary>
/// <remarks>
/// ÜRETİMDE "Log" KALIRSA SUNUCU DURMAZ (e-postadan bilinçli fark): açılışta LogWarning
/// yazılır. Gerekçe: sunucu dağıtımı Expo erişim token'ının hazır olmasına bağlanmasın.
/// Push yokken uygulama çalışmaya devam eder; e-posta yokken kimse hesabını doğrulayamazdı.
/// "Expo" seçiliyken AccessToken ya da DeneyimKimligi boşsa ise açılış DURUR (ProductionGuard).
///
/// Ortam değişkenleri: Push__Provider, Push__AccessToken, Push__DeneyimKimligi.
/// </remarks>
public sealed class PushOptions
{
    public const string SectionName = "Push";

    /// <summary>"Log" (varsayılan; yalnızca maskeli log) veya "Expo".</summary>
    public string Provider { get; set; } = "Log";

    /// <summary>
    /// Expo robot erişim token'ı (Bearer). ⚠️ SIR: depoya girmez, loglanmaz. Geliştirme ve
    /// üretim için AYRI robot ve token. Expo'daki "Enhanced Security" ancak sunucu Bearer ile
    /// göndermeye başladıktan SONRA açılır; önce açılırsa token'sız gönderimler reddedilir.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Bu sunucunun kabul ettiği Expo deneyimi, "@sahip/slug" (mobil app.json → owner/slug;
    /// bugün "@ardaerenguler/dersmate"). PUSH_TOO_MANY_EXPERIENCE_IDS'te bu deneyimin
    /// DIŞINDAKİ token'lar partiden ayrılır ve cihaz kayıtları silinir.
    /// </summary>
    public string DeneyimKimligi { get; set; } = string.Empty;

    /// <summary>Expo HTTP isteğinin zaman aşımı.</summary>
    public int ZamanAsimiSaniye { get; set; } = 15;

    /// <summary>
    /// Mesaj bildiriminin gecikmesi. Web sohbeti görünürken gelen mesajı hemen okundu
    /// işaretliyor; gecikme olmasa web'de yazışan kullanıcının telefonu her mesajda çalardı.
    /// Bu sürede okunan mesajın bildirimi gönderim anında Skipped(Okundu) olur.
    /// </summary>
    public int MesajGecikmeSaniye { get; set; } = 10;

    /// <summary>(Alıcı, sohbet) başına iki mesaj push'u arasındaki en kısa süre.</summary>
    public int MesajKismaSaniye { get; set; } = 60;

    /// <summary>Provider "Expo" mu? (büyük/küçük harf duyarsız)</summary>
    public bool ExpoMu => string.Equals(Provider, "Expo", StringComparison.OrdinalIgnoreCase);
}
