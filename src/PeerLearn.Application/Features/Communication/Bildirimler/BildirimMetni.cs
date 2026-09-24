using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;

namespace PeerLearn.Application.Features.Communication.Bildirimler;

/// <summary>Bir bildirimin kullanıcıya görünen iki metni.</summary>
public sealed record BildirimIcerigi(string Baslik, string Govde);

/// <summary>
/// Bildirim metinlerinin tamamı, ad temizleyici ve göreli süre biçimleri. Saf fonksiyonlar.
/// </summary>
/// <remarks>
/// ─── METİN KURALLARI (gizlilik bunlara dayanıyor) ───────────────────────────
/// <list type="bullet">
/// <item>Başlıklar SABİT; kişi adı başlıkta hiç geçmez.</item>
/// <item>Kişi adı yalnızca GÖVDEDE ve yalnızca iki türde: yeni mesaj ve istek kabulü.
/// İkisinde de kişi alıcının kendi arkadaşı (ya da kendi seçtiği kişi).</item>
/// <item>İstek ve ders bildirimlerinde ad YOK. İstek önceden ilişki gerektirmeden herkese
/// gidebiliyor ve görünen ad serbest metin: ad kilit ekranına taşınsaydı yabancının
/// yazdığı metin, reşit olmayanın ekranına uygulamanın kimliğiyle düşerdi. Ders
/// metinleri engelli ve engelsiz vakada BAYT BAYT aynı: adın yalnızca engelde düşmesi
/// engeli ilan ederdi.</item>
/// <item>Mesaj İÇERİĞİ hiçbir koşulda yok (önizleme ilk sürümden çıkarıldı).</item>
/// <item>Süreler GÖRELİ ("3 saat sonra"). "Yarın/bugün" gibi takvim sözcükleri ve mutlak
/// saat YOK: kullanıcının saat dilimi bilinmiyor ve bildirim gecikmeli okunabilir.</item>
/// </list>
/// </remarks>
public static class BildirimMetni
{
    /// <summary>Temizlenmiş adın en fazla grafem sayısı (üç nokta dahil).</summary>
    public const int AdEnFazla = 24;

    /// <summary>Konu adının en fazla grafem sayısı (katalogda 150'ye kadar çıkabiliyor).</summary>
    public const int KonuEnFazla = 40;

    private const string UcNokta = "…";

    // ─── Ad temizleyici ───────────────────────────────────────────────────────

    /// <summary>
    /// Kullanıcının yazdığı görünen adı bildirime konabilir hâle getirir; konamıyorsa null
    /// (çağıran genel metne düşer: "Bir arkadaşın …").
    /// </summary>
    /// <remarks>
    /// Sıra:
    /// <list type="number">
    /// <item>Her türlü boşluk (satır sonu, sekme, NBSP…) tek boşluğa.</item>
    /// <item>Kontrol (Cc) ve biçim (Cf) karakterleri silinir: yön değiştiriciler (RLO,
    /// LRI… — "evil.com" adı "moc.live" gibi görünmesin), sıfır genişlikli görünmezler.
    /// TEK İSTİSNA ZWJ (U+200D): emoji dizilerini (👨‍👩‍👧) birleştiriyor; silinirse tek emoji
    /// üç ayrı emojiye bölünür. Görünmez olduğu için sahtecilik riski ayrıca kapalı: rezerve
    /// sözcük kontrolü yalnızca harf ve rakama bakıyor.</item>
    /// <item>URL benzeri (://, www., alan adı) ya da rezerve sözcük (ürün adı, destek,
    /// yönetim, admin…) içeriyorsa null. Bildirim uygulamanın kimliğiyle görünüyor;
    /// "dersmate Destek" adlı biri kilit ekranında resmî duyuru gibi görünmemeli.</item>
    /// <item>En fazla <see cref="AdEnFazla"/> grafem (StringInfo: emoji ve birleşik harf
    /// bölünmez); kesilirse sonu "…".</item>
    /// </list>
    /// Kayıt ve profil kuralları DEĞİŞMEDİ (ayrı iş): bu kural yalnızca bildirim metnine uygulanır.
    /// </remarks>
    public static string? AdTemizle(string? ad)
    {
        if (string.IsNullOrWhiteSpace(ad))
        {
            return null;
        }

        var sb = new StringBuilder(ad.Length);
        var oncekiBosluk = false;
        foreach (var ch in ad)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!oncekiBosluk && sb.Length > 0)
                {
                    sb.Append(' ');
                }

                oncekiBosluk = true;
                continue;
            }

            var kategori = char.GetUnicodeCategory(ch);
            if ((kategori is UnicodeCategory.Control or UnicodeCategory.Format) && ch != Zwj)
            {
                continue;
            }

            sb.Append(ch);
            oncekiBosluk = false;
        }

        var temiz = sb.ToString().Trim();
        if (temiz.Length == 0 || temiz.All(c => c == Zwj))
        {
            return null;
        }

        if (UrlBenzeri(temiz) || RezerveIceriyor(temiz))
        {
            return null;
        }

        return Kes(temiz, AdEnFazla);
    }

    private const char Zwj = '‍';

    /// <summary>
    /// Alan adı deseni: nokta (etrafında boşluk olabilir) + bilinen bir üst düzey alan,
    /// ardından harf/rakam yok. Genel "x.yz" deseni "M.Ali" gibi kısaltmaları da yakardı;
    /// liste bu yüzden dar ve kötüye kullanımda sık görülenlerden oluşuyor.
    /// </summary>
    private static readonly Regex AlanAdi = new(
        @"[\p{L}\p{N}-]\s*\.\s*(com|net|org|info|biz|io|co|me|tr|app|dev|ai|gg|ly|to|tv|cc|us|uk|de|ru|xyz|top|site|online|shop|store|live|link|click|club|fun|icu|tk|ml|ga|cf)(?![\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private static bool UrlBenzeri(string ad)
    {
        if (ad.Contains("://", StringComparison.Ordinal) || ad.Contains("www.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            return AlanAdi.IsMatch(ad);
        }
        catch (RegexMatchTimeoutException)
        {
            // Zaman aşımı: karar verilemedi → güvenli yön, adı yazma.
            return true;
        }
    }

    /// <summary>
    /// Rezerve kökler, KATLANMIŞ biçimde (küçük harf, aksansız, yalnızca harf ve rakam).
    /// Arama katlanmış adın İÇİNDE yapılır: "Ders Mate", "D.E.R.S.M.A.T.E", "dersmäte",
    /// "Yönetim Ekibi", "adm1n" hepsi yakalanır. Yanlış pozitif (ör. "Destekçi" soyadı)
    /// yalnızca adı genel metne düşürür — güvenli yön.
    /// </summary>
    private static readonly string[] RezerveKokler =
    [
        Katla(Branding.ProductName),
        "peerlearn",   // eski ürün adı
        "destek", "support",
        "yonetim", "yonetici", "management",
        "moderator", "moderasyon",
        "admin",       // administrator dahil
        "guvenlik", "security",
    ];

    private static bool RezerveIceriyor(string ad)
    {
        var katli = Katla(ad);
        return RezerveKokler.Any(k => katli.Contains(k, StringComparison.Ordinal));
    }

    /// <summary>
    /// Karşılaştırma biçimi: NFKD (tam genişlikli harfler, aksanlar ayrışır) → birleşik
    /// işaretler atılır → Türkçe ı/İ düzeltilir → yaygın Kiril/Yunan benzerleri ve rakam
    /// ikameleri Latin harfe → yalnızca harf ve rakam, küçük harf.
    /// </summary>
    private static string Katla(string metin)
    {
        var sb = new StringBuilder(metin.Length);
        foreach (var ch in metin.Normalize(NormalizationForm.FormKD))
        {
            if (char.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var kucuk = char.ToLowerInvariant(ch);
            kucuk = kucuk switch
            {
                'ı' => 'i',
                // Kiril ve Yunan'dan Latin'e görünüşte eş harfler.
                'а' => 'a', 'е' => 'e', 'о' => 'o', 'р' => 'p', 'с' => 'c', 'х' => 'x', 'у' => 'y',
                'і' => 'i', 'ѕ' => 's', 'ԁ' => 'd', 'м' => 'm', 'т' => 't', 'к' => 'k', 'н' => 'h',
                'ο' => 'o', 'α' => 'a', 'ε' => 'e', 'ι' => 'i', 'κ' => 'k', 'ν' => 'v', 'τ' => 't',
                // Rakam ikameleri ("adm1n", "d3rsmate").
                '0' => 'o', '1' => 'i', '3' => 'e', '4' => 'a', '5' => 's', '7' => 't',
                _ => kucuk
            };

            if (char.IsLetterOrDigit(kucuk))
            {
                sb.Append(kucuk);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// En fazla <paramref name="enFazlaGrafem"/> grafem; kesilirse son grafem "…" olur
    /// (toplam yine sınırda). Grafem = kullanıcının tek karakter gördüğü birim; emoji ve
    /// birleşik harf ortadan bölünmez.
    /// </summary>
    public static string Kes(string metin, int enFazlaGrafem)
    {
        var bilgi = new StringInfo(metin);
        if (bilgi.LengthInTextElements <= enFazlaGrafem)
        {
            return metin;
        }

        return bilgi.SubstringByTextElements(0, Math.Max(0, enFazlaGrafem - 1)).TrimEnd() + UcNokta;
    }

    /// <summary>
    /// Katalogdan gelen konu adı: güvenilir kaynak, yalnızca boşluk daraltma ve uzunluk.
    /// </summary>
    public static string KonuKisalt(string konu)
        => Kes(string.Join(' ', konu.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)), KonuEnFazla);

    // ─── Göreli süreler ──────────────────────────────────────────────────────

    /// <summary>
    /// "{n} saat" / "{n} dakika" — AŞAĞI yuvarlanır. "… içinde onayla" gibi SON SÜRE
    /// bildiren cümleler için: kalan süreyi olduğundan uzun söylemek, öğrenciyi otomatik
    /// onayı kaçırmaya iterdi.
    /// </summary>
    public static string SureIcinde(TimeSpan kalan)
    {
        if (kalan >= TimeSpan.FromHours(1))
        {
            return $"{(int)Math.Floor(kalan.TotalHours)} saat";
        }

        return $"{Math.Max(1, (int)Math.Floor(kalan.TotalMinutes))} dakika";
    }

    /// <summary>
    /// "{n} saat" / "{n} dakika" — yaklaşık: 55 dakikadan kısası 5'in katına, uzunu en
    /// yakın saate yuvarlanır. "yaklaşık … sonra" cümleleri için.
    /// </summary>
    public static string YaklasikSure(TimeSpan kalan)
    {
        if (kalan < TimeSpan.FromMinutes(55))
        {
            return $"{BesinKatinaDakika(kalan)} dakika";
        }

        return $"{YaklasikSaat(kalan)} saat";
    }

    /// <summary>En yakın tam saat, en az 1.</summary>
    public static int YaklasikSaat(TimeSpan kalan)
        => Math.Max(1, (int)Math.Round(kalan.TotalHours, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Dersin başlamasına kalan: "birkaç dakika sonra", "{n} dakika sonra" (5'in katı),
    /// "{n} saat sonra", "{g} gün sonra" (48 saat ve üstü).
    /// </summary>
    /// <remarks>
    /// Gün 48 saatte başlıyor: "1 gün sonra" okuyana "yarın" gibi gelir ve gecikmeli
    /// okunduğunda yanlış olur; 24–48 saat arası saatle yazılıyor.
    /// </remarks>
    public static string BaslamaSuresi(TimeSpan kalan)
    {
        if (kalan < TimeSpan.FromMinutes(5))
        {
            return "birkaç dakika sonra";
        }

        if (kalan < TimeSpan.FromMinutes(55))
        {
            return $"{BesinKatinaDakika(kalan)} dakika sonra";
        }

        if (kalan < TimeSpan.FromHours(48))
        {
            return $"{YaklasikSaat(kalan)} saat sonra";
        }

        return $"{(int)Math.Round(kalan.TotalDays, MidpointRounding.AwayFromZero)} gün sonra";
    }

    private static int BesinKatinaDakika(TimeSpan kalan)
        => Math.Max(5, (int)Math.Round(kalan.TotalMinutes / 5, MidpointRounding.AwayFromZero) * 5);

    // ─── Türlere göre metinler ────────────────────────────────────────────────

    /// <param name="gonderenAdi">Ham görünen ad; burada temizlenir.</param>
    /// <param name="okunmamis">
    /// GÖNDERİM ANINDA sayılan okunmamış mesaj (o sohbette o gönderenden, ReadAtUtc IS NULL,
    /// IsDeleted = FALSE). Birleştirilen satır sayısı DEĞİL: arada okunan mesajlar sayılmamalı.
    /// </param>
    public static BildirimIcerigi YeniMesaj(string? gonderenAdi, int okunmamis)
    {
        var ad = AdTemizle(gonderenAdi) ?? "Bir arkadaşın";
        var govde = okunmamis > 1
            ? $"{ad} sana {okunmamis} yeni mesaj gönderdi."
            : $"{ad} sana yeni bir mesaj gönderdi.";
        return Paketle("Yeni mesaj", govde);
    }

    /// <param name="konu">İstenen konu (katalog). Konusuz istekte null.</param>
    public static BildirimIcerigi YeniIstek(string? konu)
        => Paketle("Yeni arkadaş isteği", string.IsNullOrWhiteSpace(konu)
            ? "Sana yeni bir arkadaş isteği geldi."
            : $"{KonuKisalt(konu)} konusunda sana yeni bir arkadaş isteği geldi.");

    /// <param name="kabulEdenAdi">Ham görünen ad; burada temizlenir. Bu kişiyi alıcı kendisi seçmişti.</param>
    public static BildirimIcerigi IstekKabul(string? kabulEdenAdi)
    {
        var ad = AdTemizle(kabulEdenAdi);
        return Paketle("Arkadaş isteğin kabul edildi", ad is null
            ? "Arkadaş isteğin kabul edildi. Artık mesajlaşabilirsiniz."
            : $"{ad} isteğini kabul etti. Artık mesajlaşabilirsiniz.");
    }

    /// <param name="bekleyen">Gönderim anında sayılan, yanıt bekleyen istek sayısı (≥ 1).</param>
    /// <param name="ilkineKalan">İlk düşecek isteğin düşmesine kalan süre.</param>
    public static BildirimIcerigi IstekDusecek(int bekleyen, TimeSpan ilkineKalan)
    {
        var saat = YaklasikSaat(ilkineKalan);
        return Paketle("Yanıt bekleyen isteklerin var", bekleyen <= 1
            ? $"Bir arkadaş isteğin yanıt bekliyor. Yanıtlamazsan yaklaşık {saat} saat sonra düşecek."
            : $"{bekleyen} arkadaş isteğin yanıt bekliyor. İlki yaklaşık {saat} saat sonra düşecek.");
    }

    /// <param name="konu">Dersin konusu (katalog).</param>
    /// <param name="otomatikOnayaKalan">D − now, gönderim anında.</param>
    /// <param name="itirazSonrasi">
    /// true: itiraz reddedildi ve sayaç yeniden başladı. Defter bunu ayrı bir tür olarak
    /// taşımıyor; dağıtıcı, damgayla AYNI anda (ResolvedAtUtc == OlayDamgasiUtc)
    /// reddedilmiş bir itiraz olup olmadığına bakarak karar verir.
    /// </param>
    public static BildirimIcerigi OnayBekliyor(string konu, TimeSpan otomatikOnayaKalan, bool itirazSonrasi)
    {
        var kalan = SureIcinde(otomatikOnayaKalan);
        var k = KonuKisalt(konu);
        return Paketle("Dersin onay bekliyor", itirazSonrasi
            ? $"İtiraz sonuçlandı. {k} dersin yeniden onayını bekliyor; {kalan} içinde onaylamazsan otomatik onaylanır."
            : $"{k} dersin tamamlandı olarak işaretlendi. {kalan} içinde onayla ya da itiraz et.");
    }

    public static BildirimIcerigi OtoOnayYaklasiyor(string konu, TimeSpan otomatikOnayaKalan)
        => Paketle("Dersin yakında otomatik onaylanacak",
            $"{KonuKisalt(konu)} dersin yaklaşık {YaklasikSure(otomatikOnayaKalan)} sonra otomatik olarak onaylanacak. Bir sorun varsa itiraz etmek için dokun.");

    public static BildirimIcerigi DersPlanlandi(string konu, TimeSpan baslamayaKalan)
        => Paketle("Yeni ders planlandı",
            $"{KonuKisalt(konu)} dersin {BaslamaSuresi(baslamayaKalan)} başlıyor. Ayrıntılar için dokun.");

    public static BildirimIcerigi DersIptal(string konu)
        => Paketle("Ders iptal edildi", $"{KonuKisalt(konu)} dersin iptal edildi.");

    /// <param name="ofsetDakika">60 ya da 10: başlık buna göre (gövdedeki süre gerçek kalandan).</param>
    public static BildirimIcerigi DersYaklasiyor(string konu, TimeSpan baslamayaKalan, int ofsetDakika)
        => Paketle(ofsetDakika >= 60 ? "Dersin yaklaşıyor" : "Dersin birazdan başlıyor",
            $"{KonuKisalt(konu)} dersin {BaslamaSuresi(baslamayaKalan)} başlıyor.");

    /// <param name="kanal">BildirimKanallari kimliği: hangi tür denendiyse.</param>
    public static BildirimIcerigi Test(string kanal)
    {
        var ne = kanal switch
        {
            BildirimKanallari.Mesajlar => "Mesaj bildirimleri",
            BildirimKanallari.Istekler => "Arkadaş isteği bildirimleri",
            BildirimKanallari.DersOnayi => "Ders onayı bildirimleri",
            _ => "Ders planı bildirimleri"
        };
        return Paketle("Test bildirimi", $"{ne} bu cihaza ulaşıyor.");
    }

    /// <summary>Son güvenlik: başlık ve gövde sınırları (PushSinirlari).</summary>
    private static BildirimIcerigi Paketle(string baslik, string govde)
        => new(Kes(baslik, PushSinirlari.BaslikEnFazla), Kes(govde, PushSinirlari.GovdeEnFazla));
}
