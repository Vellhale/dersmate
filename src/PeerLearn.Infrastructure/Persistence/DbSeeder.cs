using Microsoft.EntityFrameworkCore;
using PeerLearn.Domain.Community;

namespace PeerLearn.Infrastructure.Persistence;

/// <summary>
/// Açılışta çalışan tohumlama girişi: rozet kataloğu + ders kataloğu.
/// İkisi de idempotenttir; `--migrate` her çalıştığında güvenle tekrarlanır.
/// </summary>
public static class DbSeeder
{
    /// <summary>
    /// Rozet kataloğu. Metinler ürün kararıdır ve buradan güncellenir; kural motoru
    /// yalnızca <see cref="BadgeCode"/>'a bakar (bkz. Badge entity notu).
    /// </summary>
    /// <remarks>
    /// EKLEYİCİ DEĞİL, EŞİTLEYİCİ. Eskiden yalnızca eksik kodlar eklenirdi; emekli edilen
    /// kodlar veritabanında sonsuza kadar kalırdı. Bu sadece çöp bırakmıyor, uygulamayı
    /// KIRIYOR: <c>Code</c> metin olarak saklandığı için (HasConversion&lt;string&gt;)
    /// enum'da karşılığı olmayan bir satır okunduğu anda dönüştürme hatası veriyor —
    /// yani bayat tek bir satır, rozet okuyan her sorguyu düşürüyor.
    ///
    /// Bu yüzden bayat satırlar HAM SQL ile siliniyor: EF üzerinden okumak, tam da
    /// temizlemeye çalıştığımız satırda patlardı.
    ///
    /// ÜRETİMDE ASIL TEMİZLİK GÖÇLE YAPILIR (bkz. RetireAchievementBadges); buradaki
    /// eşitleme, göç geçmişi olmayan geliştirme veritabanları için ve bir sonraki
    /// emeklilikte kendiliğinden çalışsın diye var.
    /// </remarks>
    private static async Task SeedBadgesAsync(PeerLearnDbContext db, CancellationToken ct)
    {
        var katalog = new[]
        {
            new Badge { Code = BadgeCode.FutureTeacher, Emoji = "🌱", SortOrder = 1,
                Name = "Geleceğin öğretmeni",
                Description = "Eğitim fakültesi / pedagojik formasyon öğrencisi." }
        };

        var gecerliKodlar = katalog.Select(b => b.Code.ToString()).ToArray();

        // Önce kullanıcıya verilmiş satırlar (FK: UserBadges -> Badges, Restrict), sonra katalog.
        await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM community."UserBadges" ub
            USING community."Badges" b
            WHERE ub."BadgeId" = b."Id" AND b."Code" <> ALL({0});
            """, [gecerliKodlar], ct);

        await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM community."Badges" WHERE "Code" <> ALL({0});
            """, [gecerliKodlar], ct);

        var mevcut = await db.Badges.Select(b => b.Code).ToListAsync(ct);
        var eksik = katalog.Where(b => !mevcut.Contains(b.Code)).ToList();

        if (eksik.Count > 0)
        {
            db.Badges.AddRange(eksik);
            await db.SaveChangesAsync(ct);
        }
    }

    public static async Task SeedAsync(PeerLearnDbContext db, CancellationToken ct = default)
    {
        // Rozet kataloğu ders katalogundan BAĞIMSIZ tohumlanır: mevcut kurulumlarda
        // kategoriler zaten dolu olduğu için eski erken çıkış rozetlerin hiç eklenmemesine
        // yol açıyordu.
        await SeedBadgesAsync(db, ct);

        /*
          DERS KATALOĞU ARTIK "BOŞSA DOLDUR" DEĞİL, "HER AÇILIŞTA EŞİTLE".

          Eski davranış `if (await db.EducationCategories.AnyAsync(ct)) return;` idi: bir
          kez tohumlanmış hiçbir kurulum müfredat güncellemesini görmezdi. YKS müfredatı
          (767 konu) artık ürünün tanımının parçası ve kodla birlikte sürümleniyor;
          eşitleyici bunu her `--migrate` çalıştırmasında uygular.

          Eşitleyici hiçbir satır SİLMEZ, yalnızca pasifleştirir — gerekçe CatalogSeeder
          sınıf notunda (FK'lar Restrict, mevcut ders geçmişi bağlı).
        */
        await CatalogSeeder.SyncAsync(db, ct);
    }
}
