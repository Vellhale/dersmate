using Microsoft.EntityFrameworkCore;
using PeerLearn.Domain.Catalog;

namespace PeerLearn.Infrastructure.Persistence;

/// <summary>
/// Ders kataloğunu <see cref="Curriculum"/> ile eşitler: YKS → TYT / AYT, sekiz ders,
/// tüm konular.
/// </summary>
/// <remarks>
/// EKLEYİCİ DEĞİL, EŞİTLEYİCİ — ama SİLİCİ DE DEĞİL.
///
/// Eski tohumlayıcı "kategori tablosunda satır varsa hiç dokunma" diyordu; müfredat
/// güncellendiğinde mevcut hiçbir kurulum yeni konuları görmezdi. Yeni davranış:
/// eksik olanı ekle, listeden düşeni PASİFLEŞTİR.
///
/// ⚠️ NEDEN SİLMİYORUZ — bu maddenin en kritik kararı.
/// Katalogdaki her FK <c>Restrict</c>'tir (finansal izlenebilirlik gereği, bkz.
/// docs/ASAMA-1-MIMARI.md). Mevcut kurulumlarda <c>PortfolioEntries.TopicId</c> ve
/// <c>LessonSessions.TopicId</c> eski konulara bağlı. Eski kataloğu silmeye kalkmak iki
/// sonuçtan birini verirdi: ya FK ihlaliyle göç patlar, ya da (cascade açık olsaydı)
/// kullanıcıların ders geçmişi sessizce yok olurdu. Bunun yerine <c>IsActive=false</c>
/// yapılıyor — okuma sorguları zaten aktiflik filtresi uyguluyor (bkz. CatalogController),
/// yani kullanıcı eski konuyu artık SEÇEMEZ ama geçmiş dersi yerinde durur.
///
/// İDEMPOTENT: art arda çalıştırmak aynı sonucu verir. Eşleştirme doğal anahtarlarla
/// yapılır (kategori: Slug, ders: Kategori+Branş, konu: Ders+Ad), Guid'lerle değil —
/// böylece ikinci çalıştırma kopya üretmez.
/// </remarks>
public static class CatalogSeeder
{
    private const string YksSlug = "yks";
    private const string TytSlug = "yks-tyt";
    private const string AytSlug = "yks-ayt";

    public static async Task SyncAsync(PeerLearnDbContext db, CancellationToken ct = default)
    {
        var yks = await UpsertCategoryAsync(db, YksSlug, "YKS", parentId: null, sortOrder: 1, ct);
        var tyt = await UpsertCategoryAsync(db, TytSlug, "TYT", yks.Id, sortOrder: 1, ct);
        var ayt = await UpsertCategoryAsync(db, AytSlug, "AYT", yks.Id, sortOrder: 2, ct);

        await db.SaveChangesAsync(ct);

        var kalanKategoriler = new HashSet<Guid> { yks.Id, tyt.Id, ayt.Id };
        var kalanDersler = new HashSet<Guid>();
        var kalanKonular = new HashSet<Guid>();

        await SyncLevelAsync(db, tyt, Curriculum.TYT, Curriculum.ExamLevel.Tyt, kalanDersler, kalanKonular, ct);
        await SyncLevelAsync(db, ayt, Curriculum.AYT, Curriculum.ExamLevel.Ayt, kalanDersler, kalanKonular, ct);

        await db.SaveChangesAsync(ct);

        await DeactivateStrandedAsync(db, kalanKategoriler, kalanDersler, kalanKonular, ct);

        await db.SaveChangesAsync(ct);
    }

    private static async Task<EducationCategory> UpsertCategoryAsync(
        PeerLearnDbContext db, string slug, string name, Guid? parentId, int sortOrder, CancellationToken ct)
    {
        // Slug tüm ağaçta benzersiz (bkz. EducationCategoryConfiguration) — doğal anahtar.
        var mevcut = await db.EducationCategories.SingleOrDefaultAsync(c => c.Slug == slug, ct);
        if (mevcut is not null)
        {
            mevcut.Name = name;
            mevcut.ParentCategoryId = parentId;
            mevcut.SortOrder = sortOrder;
            mevcut.IsActive = true;
            return mevcut;
        }

        var yeni = new EducationCategory
        {
            Slug = slug, Name = name, ParentCategoryId = parentId, SortOrder = sortOrder, IsActive = true
        };
        db.EducationCategories.Add(yeni);
        return yeni;
    }

    private static async Task SyncLevelAsync(
        PeerLearnDbContext db, EducationCategory kategori, Curriculum.BranchTopics[] mufredat,
        Curriculum.ExamLevel seviye,
        HashSet<Guid> kalanDersler, HashSet<Guid> kalanKonular, CancellationToken ct)
    {
        var mevcutDersler = await db.Subjects.Where(s => s.CategoryId == kategori.Id).ToListAsync(ct);

        for (var i = 0; i < mufredat.Length; i++)
        {
            var (brans, konular) = (mufredat[i].Branch, mufredat[i].Topics);
            var ad = Curriculum.DisplayName(brans, seviye);

            /*
              Ders eşleştirme sırası: ÖNCE BRANŞ, sonra ad.

              Branş, kod tarafından yönetilen sabit bir kimliktir; ad ise ürün kararıdır ve
              değişebilir. Yalnızca ada baksaydık, "Matematik" bir gün "Temel Matematik"
              olduğunda eşitleyici mevcut satırı bulamaz, YENİ bir ders açar ve eskisini
              pasifleştirirdi — o eski derse bağlı tüm konular ve ders geçmişi bir anda
              katalogdan düşerdi.

              Ada geri düşüş, bu alanın YENİ olması için gerekli: mevcut kurulumlarda
              Branch kolonu göçten sonra boştur ve ilk eşitlemede doldurulması gerekir.
            */
            var ders = mevcutDersler.FirstOrDefault(s => s.Branch == brans)
                       ?? mevcutDersler.FirstOrDefault(s => s.Branch == null && s.Name == ad);

            if (ders is null)
            {
                ders = new Subject { CategoryId = kategori.Id, Category = kategori };
                db.Subjects.Add(ders);
                mevcutDersler.Add(ders);
            }

            ders.Name = ad;
            ders.Branch = brans;
            ders.SortOrder = i + 1;
            ders.IsActive = true;

            // Konuları yazabilmek için dersin Id'si gerekiyor; yeni eklenen satırlar için
            // Id nesne oluşturulurken atanıyor (BaseEntity: Guid.NewGuid()), bu yüzden
            // araya bir SaveChanges koymaya gerek yok.
            kalanDersler.Add(ders.Id);

            await SyncTopicsAsync(db, ders, konular, kalanKonular, ct);
        }
    }

    private static async Task SyncTopicsAsync(
        PeerLearnDbContext db, Subject ders, string[] konular, HashSet<Guid> kalanKonular, CancellationToken ct)
    {
        // Konu adı ders içinde benzersiz (bkz. TopicConfiguration) — doğal anahtar.
        var mevcut = await db.Topics.Where(t => t.SubjectId == ders.Id).ToListAsync(ct);
        var indeks = mevcut.ToDictionary(t => t.Name, t => t);

        for (var i = 0; i < konular.Length; i++)
        {
            if (!indeks.TryGetValue(konular[i], out var konu))
            {
                konu = new Topic { SubjectId = ders.Id, Subject = ders, Name = konular[i] };
                db.Topics.Add(konu);
                indeks[konular[i]] = konu;
            }

            konu.SortOrder = i + 1;
            konu.IsActive = true;
            kalanKonular.Add(konu.Id);
        }
    }

    /// <summary>
    /// Müfredatta karşılığı kalmayan katalog satırlarını pasifleştirir (silmez).
    /// </summary>
    /// <remarks>
    /// Bu, eski "Üniversite → Mühendislik → Fizik 1 / Calculus 1" ağacını ve emekli edilen
    /// ders/konuları kullanıcı seçiminden çıkarır. Sıra önemli değil — hiçbir satır
    /// silinmediği için FK ihlali oluşamaz.
    ///
    /// <c>Branch</c> BİLEREK TEMİZLENMİYOR: pasif bir dersin branşı kalırsa, o derse ait
    /// geçmiş dersler rozet hesabına saymaya devam eder. Kazanılmış emeğin, katalog
    /// yeniden düzenlendi diye geriye dönük silinmesi doğru olmazdı.
    /// </remarks>
    private static async Task DeactivateStrandedAsync(
        PeerLearnDbContext db, HashSet<Guid> kalanKategoriler, HashSet<Guid> kalanDersler,
        HashSet<Guid> kalanKonular, CancellationToken ct)
    {
        foreach (var kategori in await db.EducationCategories.Where(c => c.IsActive).ToListAsync(ct))
        {
            if (!kalanKategoriler.Contains(kategori.Id)) kategori.IsActive = false;
        }

        foreach (var ders in await db.Subjects.Where(s => s.IsActive).ToListAsync(ct))
        {
            if (!kalanDersler.Contains(ders.Id)) ders.IsActive = false;
        }

        foreach (var konu in await db.Topics.Where(t => t.IsActive).ToListAsync(ct))
        {
            if (!kalanKonular.Contains(konu.Id)) konu.IsActive = false;
        }
    }
}
