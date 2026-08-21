namespace PeerLearn.Domain.Community;

/// <summary>
/// Kullanıcının ders anlatarak biriktirdiği toplam krediye göre ulaştığı seviye (1–10).
/// </summary>
/// <remarks>
/// UNVAN YERİNE SEVİYE (2026-08-21). Önceki tasarım Çırak / Öğretici / Uzman / Usta /
/// Mentor / Üstat adlarını kullanıyordu. Adlar iki sorun çıkarıyordu: bir lonca metaforu
/// kurup "usta"nın "uzman"dan üstün mü altta mı olduğunu okuyanın tahminine bırakıyor,
/// ayrıca dallanan rozet sistemiyle (Matematik Çırağı) aynı kelimeleri paylaşıp iki farklı
/// şeyi tek isimle anlatıyordu. Sayı bunların ikisini de çözer: 7 &gt; 5 tartışmasızdır.
///
/// KREDİ SİSTEMİNE DOKUNULMADI. Seviye, tıpkı eski unvan gibi, YALNIZCA
/// <c>TotalEarnedCredits</c> okunarak hesaplanır; basım, defter ve cüzdan aynen duruyor.
///
/// VERİTABANINDA SAKLANMAZ. Bu tip hiçbir tabloya yazılmıyor (kolon yok, migration yok),
/// iki sorgu işleyicisinde anlık hesaplanıyor. Bu yüzden eski enum üyelerinin kaldırılması
/// CLAUDE.md'deki "enum üyesini verisinden önce silme" tuzağına girmiyor — saklanan veri yok.
///
/// EŞİK SINIRLARI ALT DAHİL, ÜST HARİÇ: 500 kredi 3. seviyedir, 2. değil. Sınırda iki
/// seviyeye birden işaret eden bir yazım, aynı kullanıcıyı iki yerde iki farklı seviyede
/// gösterirdi.
///
/// YÜKSELMEK GİTTİKÇE ZORLAŞIR: eşikler arası fark her adımda büyür (200, 300, 500, 1.000,
/// 1.500, 2.500, 4.000, 6.000, 9.000). Sabit aralık, üst seviyeleri anlamsızca ucuzlatırdı;
/// ilk seviyelerin ucuz olması ise yeni kullanıcının ilk ilerlemeyi hemen görmesi için.
/// </remarks>
/// <param name="Level">1–10 arası seviye.</param>
/// <param name="Emoji">Başlıktaki rozette adın yanında görünen simge.</param>
/// <param name="Title">Görüntülenecek ad — "3. Seviye".</param>
/// <param name="MinCredits">Bu seviyenin başladığı kredi.</param>
/// <param name="NextLevelAt">
/// Bir sonraki seviyenin başladığı kredi; en üst seviyede <c>null</c>.
/// Arayüz ilerleme çubuğunu bundan çizer — "ne kadar kaldı" sorusu sunucuda yanıtlanır ki
/// eşikler değişirse arayüz yalan söylemesin.
/// </param>
public readonly record struct UserLevelInfo(
    int Level,
    string Emoji,
    string Title,
    int MinCredits,
    int? NextLevelAt);

public static class UserLevelCalculator
{
    /// <summary>En yüksek seviye. Arayüz "10/10" gibi bir ifade kurmak isterse buradan okur.</summary>
    public const int MaxLevel = 10;

    /// <summary>
    /// Eşikler artan sırada. Yeni bir kademe eklenirse yalnızca burası değişir.
    /// Simgeler kademe kademe ilerler; eski unvan simgeleri bilerek korundu ki
    /// mevcut kullanıcı için görsel dil tanıdık kalsın.
    /// </summary>
    private static readonly (int Min, string Emoji)[] Kademeler =
    [
        (0, "🌱"),      // 1
        (200, "🌱"),    // 2
        (500, "📗"),    // 3
        (1_000, "📗"),  // 4
        (2_000, "⭐"),   // 5
        (3_500, "⭐"),   // 6
        (6_000, "🧠"),  // 7
        (10_000, "🧠"), // 8
        (16_000, "🏆"), // 9
        (25_000, "👑")  // 10
    ];

    /// <summary>
    /// Toplam kazanılan krediden seviyeyi hesaplar.
    /// </summary>
    /// <param name="totalEarnedCredits">
    /// Ders anlatarak kazanılmış TOPLAM kredi (birikimli, harcanan/yanan düşülmez).
    /// Negatif değer 0 sayılır: seviye bir başarı ölçüsüdür, bozuk veri kimseyi
    /// ulaştığı seviyeden etmemeli.
    /// </param>
    public static UserLevelInfo Hesapla(int totalEarnedCredits)
    {
        var puan = Math.Max(0, totalEarnedCredits);

        // Sondan başa: eşiği geçilen İLK kademe doğru kademedir.
        for (var i = Kademeler.Length - 1; i >= 0; i--)
        {
            var k = Kademeler[i];
            if (puan >= k.Min)
            {
                var sonraki = i + 1 < Kademeler.Length ? Kademeler[i + 1].Min : (int?)null;
                return new UserLevelInfo(i + 1, k.Emoji, $"{i + 1}. Seviye", k.Min, sonraki);
            }
        }

        // Buraya düşülemez (ilk kademe 0'dan başlıyor ve puan >= 0), ama derleyici için
        // ve eşik tablosu elle bozulursa güvenli varsayılan olarak duruyor.
        return new UserLevelInfo(1, Kademeler[0].Emoji, "1. Seviye", 0, Kademeler[1].Min);
    }
}
