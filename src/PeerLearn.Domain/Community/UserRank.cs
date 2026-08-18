namespace PeerLearn.Domain.Community;

/// <summary>
/// Kullanıcının ders anlatarak biriktirdiği toplam krediye göre kazandığı unvan.
/// </summary>
/// <remarks>
/// TEK UNVAN, ÇOK ROZET DEĞİL. Önceki tasarımda her başarı ayrı bir rozetti (ilk ders,
/// 10 saat, 5 yıldız, gönüllü, hızlı yanıt) ve profil bir rozet duvarına dönüyordu:
/// hepsi aynı görsel ağırlıkta olduğu için hiçbiri bir şey söylemiyordu. Tek bir unvan
/// tek bir soruyu yanıtlar — "bu kişi ne kadar ders anlatmış?" — ve karşılaştırılabilir.
///
/// EŞİK SINIRLARI ALT DAHİL, ÜST HARİÇ: 500 kredi <see cref="Ogretici"/>'dir,
/// <see cref="Cirak"/> değil. İstekteki "0–500 / 500–1.000" yazımı sınırda iki unvana
/// birden işaret ediyordu; tek bir kural seçilmezse aynı kullanıcı iki yerde iki farklı
/// unvanla görünürdü.
/// </remarks>
public enum UserRank
{
    Cirak = 0,
    Ogretici = 1,
    Uzman = 2,
    Usta = 3,
    Mentor = 4,
    Ustat = 5
}

/// <summary>Unvanın görüntülenecek hâli. Emoji ve ad tek yerde tutulur.</summary>
/// <param name="Rank">Hesaplanan unvan.</param>
/// <param name="Emoji">Profilde adın yanında görünen simge.</param>
/// <param name="Title">Türkçe unvan adı.</param>
/// <param name="MinCredits">Bu unvanın başladığı kredi.</param>
/// <param name="NextRankAt">
/// Bir sonraki unvanın başladığı kredi; en üst unvanda <c>null</c>.
/// Arayüz ilerleme çubuğunu bundan çizer — "ne kadar kaldı" sorusu sunucuda yanıtlanır ki
/// eşikler değişirse arayüz yalan söylemesin.
/// </param>
public readonly record struct UserRankInfo(
    UserRank Rank,
    string Emoji,
    string Title,
    int MinCredits,
    int? NextRankAt);

public static class UserRankCalculator
{
    /// <summary>Eşikler artan sırada. Yeni bir kademe eklenirse yalnızca burası değişir.</summary>
    private static readonly (int Min, UserRank Rank, string Emoji, string Title)[] Kademeler =
    [
        (0, UserRank.Cirak, "🌱", "Çırak"),
        (500, UserRank.Ogretici, "📚", "Öğretici"),
        (1_000, UserRank.Uzman, "⭐", "Uzman"),
        (2_500, UserRank.Usta, "🧠", "Usta"),
        (5_000, UserRank.Mentor, "🏆", "Mentor"),
        (10_000, UserRank.Ustat, "👑", "Üstat")
    ];

    /// <summary>
    /// Toplam kazanılan krediden unvanı hesaplar.
    /// </summary>
    /// <param name="totalEarnedCredits">
    /// Ders anlatarak kazanılmış TOPLAM kredi (birikimli, harcanan/yanan düşülmez).
    /// Negatif değer 0 sayılır: unvan bir başarı ölçüsüdür, bozuk veri kimseyi
    /// sahip olduğu unvandan etmemeli.
    /// </param>
    public static UserRankInfo Hesapla(int totalEarnedCredits)
    {
        var puan = Math.Max(0, totalEarnedCredits);

        // Sondan başa: eşiği geçilen İLK kademe doğru kademedir.
        for (var i = Kademeler.Length - 1; i >= 0; i--)
        {
            var k = Kademeler[i];
            if (puan >= k.Min)
            {
                var sonraki = i + 1 < Kademeler.Length ? Kademeler[i + 1].Min : (int?)null;
                return new UserRankInfo(k.Rank, k.Emoji, k.Title, k.Min, sonraki);
            }
        }

        // Buraya düşülemez (ilk kademe 0'dan başlıyor ve puan >= 0), ama derleyici
        // için ve eşik tablosu elle bozulursa güvenli varsayılan olarak duruyor.
        var ilk = Kademeler[0];
        return new UserRankInfo(ilk.Rank, ilk.Emoji, ilk.Title, ilk.Min, Kademeler[1].Min);
    }
}
