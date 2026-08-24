namespace PeerLearn.Domain.Community;

/// <summary>
/// Kullanıcının ders anlatarak biriktirdiği toplam krediden türeyen seviye (1..10).
/// </summary>
/// <remarks>
/// UNVAN MERDİVENİNİN YERİNE GEÇTİ. Önceki tasarım altı adlandırılmış kademeydi
/// (Çırak → Öğretici → Uzman → Usta → Mentor → Üstat) ve iki sorunu vardı:
///
/// 1. KAÇ BASAMAK OLDUĞU GÖRÜNMÜYORDU. "Uzman" olan biri yolun neresinde olduğunu
///    bilmiyordu; merdivenin uzunluğu yalnızca kodu okuyanın malumuydu. Numaralı
///    seviye bunu tek bakışta söylüyor: 10 üzerinden kaçtasın.
/// 2. AD, BİR KARAKTER İDDİASIYDI. "Mentor" kelimesi anlatılan ders saatinden fazlasını
///    ima ediyordu; oysa ölçtüğümüz tek şey birikmiş kredi.
///
/// MEKANİZMA DEĞİŞMEDİ: seviye eskisi gibi <see cref="Identity.User.TotalEarnedCredits"/>
/// üzerinden hesaplanıyor. Değişen yalnızca basamak sayısı ve etiketleme.
///
/// BRANŞ ROZETLERİYLE KARIŞTIRILMAMALI. Seviye GENEL emeği ölçer (tüm derslerden birikmiş
/// kredi); <see cref="UserSubjectBadge"/> ise TEK BİR BRANŞTA anlatılan süreyi. İkisi
/// bilerek farklı şeye bakıyor: 10. seviye çok ders anlatmış demektir, "Matematik Üstadı"
/// ise Matematik'te derinleşmiş demektir. Eski adlandırma bu ayrımı bulanıklaştırıyordu —
/// hem unvan hem rozet seviyeleri "Çırak/Usta/Üstad" kelimelerini kullanıyordu ve aynı
/// kullanıcı profilde iki ayrı "Çırak" taşıyabiliyordu. Numaraya geçmek bu çakışmayı
/// kendiliğinden kaldırdı; rozet tarafındaki adlar bu yüzden DEĞİŞMEDİ.
///
/// EŞİK SINIRLARI ALT DAHİL, ÜST HARİÇ: 100 kredi 2. seviyedir, 1. değil. Kural tek bir
/// yerde seçilmezse aynı kullanıcı profilde bir seviye, başlıkta başka bir seviye görünür.
///
/// VERİTABANINDA SAKLANMIYOR. Seviye her istekte krediden hesaplanıyor; kalıcı bir kolon
/// yok. Bu bilinçli: saklanan bir seviye, kredi elle düzeltildiğinde ya da bir olay
/// kaçırıldığında kalıcı olarak yanlış kalırdı. Aynı karar rozet motorunda da verilmiş
/// (bkz. <see cref="UserSubjectBadge"/> notu). Pratik faydası da oldu — unvandan seviyeye
/// geçiş HİÇBİR GÖÇ gerektirmedi.
/// </remarks>
public static class UserLevelRules
{
    /// <summary>Sistemdeki en yüksek seviye. Arayüz "3 / 10" bağlamını buradan okur.</summary>
    public const int MaxLevel = 10;

    /// <summary>
    /// Seviye başlangıç eşikleri, artan sırada. Dizinin i. elemanı (i+1). seviyenin alt
    /// sınırıdır: <c>Esikler[0] = 0</c> → 1. seviye, <c>Esikler[1] = 100</c> → 2. seviye.
    /// </summary>
    /// <remarks>
    /// EĞRİ GEOMETRİK, DOĞRUSAL DEĞİL: her basamak bir öncekinin kabaca 1,8 katı.
    /// Gerekçe iki uçta da aynı: ilerleme HİSSEDİLİR kalmalı.
    ///
    /// - Doğrusal bir dağılımda (her 1.000 kredide bir seviye) yeni kullanıcı 20 ders
    ///   anlatana kadar hiç ilerleme görmezdi; ilk seviyeler bir ödül değil, bir bekleme
    ///   odası olurdu.
    /// - Sabit oranlı ama dik bir eğride ise üst seviyeler ulaşılamaz hâle gelir ve
    ///   ölçek fiilen 5 basamağa iner.
    ///
    /// Seçilen dağılımda 2. seviye iki derste (30 dk × 2 = 100 kredi) geliyor, 10. seviye
    /// ise 200 derslik gerçek bir emek istiyor. Üst sınır 10.000'de bırakıldı: eski
    /// merdivenin en üst eşiği de buydu, yani bugüne kadar en çok ders anlatmış
    /// kullanıcılar seviye değişiminde GERİLEMİYOR.
    ///
    /// Yeni bir basamak eklenecekse değişecek tek yer burasıdır — ama <see cref="MaxLevel"/>
    /// ile uzunluğun tutması şart (UserLevelTests bunu sınıyor).
    /// </remarks>
    private static readonly int[] Esikler =
    [
        0,      //  1. seviye
        100,    //  2. seviye —   2 ders (30 dk)
        200,    //  3. seviye —   4 ders
        350,    //  4. seviye —   7 ders
        600,    //  5. seviye —  12 ders
        1_000,  //  6. seviye —  20 ders
        1_750,  //  7. seviye —  35 ders
        3_000,  //  8. seviye —  60 ders
        5_500,  //  9. seviye — 110 ders
        10_000  // 10. seviye — 200 ders
    ];

    /// <summary>
    /// Toplam kazanılan krediden seviyeyi hesaplar.
    /// </summary>
    /// <param name="totalEarnedCredits">
    /// Ders anlatarak kazanılmış TOPLAM kredi (birikimli; harcanan/yanan düşülmez).
    /// Negatif değer 0 sayılır: seviye bir başarı ölçüsüdür, bozuk veri kimseyi
    /// hak ettiği seviyeden etmemeli.
    /// </param>
    public static UserLevelInfo Hesapla(int totalEarnedCredits)
    {
        var puan = Math.Max(0, totalEarnedCredits);

        // Sondan başa: eşiği geçilen İLK basamak doğru basamaktır.
        for (var i = Esikler.Length - 1; i >= 0; i--)
        {
            if (puan < Esikler[i]) continue;

            var sonraki = i + 1 < Esikler.Length ? Esikler[i + 1] : (int?)null;
            return new UserLevelInfo(i + 1, Esikler[i], sonraki);
        }

        // Buraya düşülemez (ilk eşik 0 ve puan >= 0), ama eşik tablosu elle bozulursa
        // istisna fırlatmak yerine en alt basamağa düşmek doğru davranış: seviye bir
        // gösterge, profili açılamaz hâle getirecek kadar kritik değil.
        return new UserLevelInfo(1, 0, Esikler.Length > 1 ? Esikler[1] : null);
    }

    /// <summary>Verilen seviyenin alt sınırı. Test ve tohumlama için; üretim yolu <see cref="Hesapla"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">seviye 1..<see cref="MaxLevel"/> dışındaysa.</exception>
    public static int MinCreditsFor(int seviye)
    {
        if (seviye < 1 || seviye > MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(seviye), seviye, $"Seviye 1..{MaxLevel} olmalı.");

        return Esikler[seviye - 1];
    }
}

/// <summary>Seviyenin görüntülenecek hâli.</summary>
/// <param name="Level">Hesaplanan seviye (1..<see cref="UserLevelRules.MaxLevel"/>).</param>
/// <param name="MinCredits">Bu seviyenin başladığı kredi.</param>
/// <param name="NextLevelAt">
/// Bir sonraki seviyenin başladığı kredi; en üst seviyede <c>null</c>.
/// Arayüz "ne kadar kaldı" satırını bundan çizer — hesap SUNUCUDA yapılır ki eşikler
/// değiştiğinde arayüz eski sayıyı göstermeye devam etmesin. Bu projede aynı sapma
/// fiyat formülünde bir kez yaşandı.
/// </param>
public readonly record struct UserLevelInfo(int Level, int MinCredits, int? NextLevelAt);
