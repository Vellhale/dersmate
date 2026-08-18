using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Identity;
using PeerLearn.Domain.Matchmaking;

namespace PeerLearn.Application.Features.Discovery;

/// <summary>
/// Gelişmiş arama/filtreleme (Modül 1). Sonuç birimi "ders ilanı" = bir eğitmenin bir
/// konudaki Offer kaydıdır; filtrelerin (konu, seviye) doğal olarak uygulandığı taneciklik
/// budur. Aynı eğitmen farklı konularla birden çok satırda çıkabilir — Keşfet'teki
/// "eğitmen kartı" görünümünden bilinçli olarak farklı.
///
/// "PUAN ARALIĞI" HAKKINDA ÖNEMLİ NOT:
/// Bu üründe dersin bir FİYATI yoktur — ekonomi sıfır toplamlıdır ve 1 kredi = 60 dakika
/// sabittir (bkz. LessonSession.CreditCost). Dolayısıyla filtrelenebilecek bir fiyat alanı
/// yok. Karşılığı olan iki sayısal ölçüt şunlar:
///   • MinLevel/MaxLevel  → eğitmenin o konudaki öz değerlendirmesi (PortfolioEntry, 1-5)
///   • MinRating/MaxRating→ eğitmenin aldığı değerlendirme ortalaması (User.AverageRating)
/// Değişken fiyat istenirse bu, ekonominin sıfır toplam değişmezini kıracak bir ürün kararı
/// olur (kredi basılması/yakılması gerekir) — şema değişikliği öncesi ayrıca konuşulmalı.
/// </summary>
public sealed record SearchOffersQuery(
    Guid? CurrentUserId,
    string? Search,
    Guid? CategoryId,
    Guid? SubjectId,
    Guid? TopicId,
    int? MinLevel,
    int? MaxLevel,
    decimal? MinRating,
    decimal? MaxRating,

    /// <summary>Yalnızca gönüllü (0 kredi) ilanlar. Kredisi biten öğrencinin tek çıkış yolu.</summary>
    bool OnlyVolunteer = false,

    OfferSortOrder Sort = OfferSortOrder.Relevance,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<OfferCardDto>>;

public enum OfferSortOrder
{
    /// <summary>Varsayılan: çapraz takas olasılığı yüksek olanlar üstte, sonra puan.</summary>
    Relevance = 0,
    Newest = 1,
    Popular = 2,
    RatingDesc = 3,
    RatingAsc = 4
}

public sealed record OfferCardDto(
    Guid OfferId,
    Guid TutorUserId,
    string TutorDisplayName,
    decimal TutorAverageRating,
    int TutorRatingCount,
    Guid TopicId,
    string TopicName,
    string SubjectName,
    string CategoryName,
    int SelfAssessedLevel,
    string? Note,

    /// <summary>
    /// Gönüllü (0 kredi) ilan. Fiyat KARAR ANINDA görünmeli: bu bayrak olmadan kredisi biten
    /// öğrenci ücretsiz dersi arama sonucunda ücretlisinden ayırt edemiyordu.
    /// </summary>
    bool IsVolunteer,

    DateTime CreatedAtUtc);

public sealed class SearchOffersHandler : IRequestHandler<SearchOffersQuery, PagedResult<OfferCardDto>>
{
    /// <summary>
    /// Kısa TTL bilinçli: portföy değişikliğinden sonra sonucun en fazla bu kadar bayat
    /// kalmasını kabul ediyoruz. Alternatif (her portföy yazımında öneki düşürmek) daha
    /// karmaşık ve keşif akışında bu tazelik gerekmiyor.
    /// </summary>
    private static readonly TimeSpan ResultTtl = TimeSpan.FromSeconds(60);

    /// <summary>Kategori ağacı neredeyse hiç değişmez; uzun tutulabilir.</summary>
    private static readonly TimeSpan CategoryTreeTtl = TimeSpan.FromMinutes(30);

    private const int MaxPageSize = 50;

    private readonly IAppDbContext _db;
    private readonly ICacheService _cache;

    public SearchOffersHandler(IAppDbContext db, ICacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<PagedResult<OfferCardDto>> Handle(SearchOffersQuery request, CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        // Kategori seçilmişse alt kategorileri de kapsa: "YKS" seçen kullanıcı TYT/AYT
        // altındaki dersleri de görmeli. Ağaç küçük ve durağan olduğu için bellekte
        // çözülüp id listesi sorguya veriliyor (PostgreSQL'de recursive CTE'ye gerek yok).
        IReadOnlyList<Guid>? categoryIds = request.CategoryId is null
            ? null
            : await ResolveCategoryBranchAsync(request.CategoryId.Value, ct);

        if (categoryIds is { Count: 0 })
        {
            return PagedResult<OfferCardDto>.Empty(page, pageSize);
        }

        /*
          ÖNBELLEK ANAHTARI kullanıcıdan BAĞIMSIZ tutulur (CurrentUserId anahtara girmez).
          Sebep: aksi halde her kullanıcı kendi kopyasını üretir ve önbellek isabet oranı
          neredeyse sıfıra iner — yani hiç önbelleklememiş oluruz. Kişiselleştirmenin tek
          parçası olan "kendini listede görme" kuralı, önbellekten SONRA bellekte uygulanır.
          Bunun bedeli: kullanıcı kendi ilanına denk gelen sayfada pageSize-1 sonuç görebilir.
          Keşif akışı için kabul edilebilir; sayfa sayısını bozacak bir tutarsızlık değil.
        */
        var cacheKey = BuildCacheKey(request, page, pageSize, categoryIds);

        var cached = await _cache.GetOrCreateAsync(
            cacheKey,
            ResultTtl,
            innerCt => QueryAsync(request, categoryIds, page, pageSize, innerCt),
            ct);

        if (request.CurrentUserId is null)
        {
            return cached;
        }

        var visible = cached.Items.Where(i => i.TutorUserId != request.CurrentUserId.Value).ToList();

        // TotalCount önbellekteki ham sayı: kendi ilanları düşülünce en fazla birkaç
        // birim sapar, sayfa çubuğunu bozmaz.
        return cached with { Items = visible };
    }

    private async Task<PagedResult<OfferCardDto>> QueryAsync(
        SearchOffersQuery request,
        IReadOnlyList<Guid>? categoryIds,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        // Dinamik filtreleme: IQueryable üzerine koşullu Where'ler bindirilir. Her koşul
        // yalnızca DOLU geldiğinde eklenir — böylece boş filtreler sorguya "1=1" gibi
        // gereksiz yük bindirmez ve PostgreSQL planı sadeleşir.
        var query =
            from offer in _db.PortfolioEntries.AsNoTracking()
            join topic in _db.Topics.AsNoTracking() on offer.TopicId equals topic.Id
            join subject in _db.Subjects.AsNoTracking() on topic.SubjectId equals subject.Id
            join category in _db.EducationCategories.AsNoTracking() on subject.CategoryId equals category.Id
            join tutor in _db.Users.AsNoTracking() on offer.UserId equals tutor.Id
            where offer.IsActive
                  && offer.Direction == PortfolioDirection.Offer
                  && topic.IsActive
                  && subject.IsActive
                  && tutor.Status == UserStatus.Active
            select new { offer, topic, subject, category, tutor };

        if (categoryIds is not null)
        {
            query = query.Where(x => categoryIds.Contains(x.subject.CategoryId));
        }

        if (request.SubjectId is { } subjectId)
        {
            query = query.Where(x => x.topic.SubjectId == subjectId);
        }

        if (request.TopicId is { } topicId)
        {
            query = query.Where(x => x.offer.TopicId == topicId);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            /*
              Büyük/küçük harf duyarsız arama. EF.Functions.ILike KULLANILMADI: o Npgsql'e
              özgüdür ve Application katmanını veritabanı sağlayıcısına bağlardı (bu proje
              Clean Architecture gereği Application → Infrastructure bağımlılığını yasaklıyor).
              ToLower().Contains(...) sağlayıcıdan bağımsızdır ve PostgreSQL'de zaten
              "lower(x) LIKE '%...%'"e çevrilir — ILIKE ile aynı maliyet.

              Ölçekleme notu: baştaki joker yüzünden b-tree index kullanılamaz. Katalog
              büyüdüğünde çözüm pg_trgm + GIN index'tir; o aşamada bu ifade ham SQL'e ya da
              Infrastructure'daki bir sorgu nesnesine taşınmalı.
            */
            var term = request.Search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.topic.Name.ToLower().Contains(term)
                || x.subject.Name.ToLower().Contains(term)
                || x.tutor.DisplayName.ToLower().Contains(term));
        }

        if (request.MinLevel is { } minLevel)
        {
            query = query.Where(x => x.offer.SelfAssessedLevel >= minLevel);
        }

        if (request.MaxLevel is { } maxLevel)
        {
            query = query.Where(x => x.offer.SelfAssessedLevel <= maxLevel);
        }

        if (request.OnlyVolunteer)
        {
            query = query.Where(x => x.offer.IsVolunteer);
        }

        if (request.MinRating is { } minRating)
        {
            query = query.Where(x => x.tutor.AverageRating >= minRating);
        }

        if (request.MaxRating is { } maxRating)
        {
            query = query.Where(x => x.tutor.AverageRating <= maxRating);
        }

        // Sayım, sıralamadan ÖNCE ve projeksiyondan bağımsız yapılır: ORDER BY sayıya
        // etki etmez ve PostgreSQL'i gereksiz sıralamaya zorlamak istemiyoruz.
        var totalCount = await query.CountAsync(ct);
        if (totalCount == 0)
        {
            return PagedResult<OfferCardDto>.Empty(page, pageSize);
        }

        var ordered = request.Sort switch
        {
            OfferSortOrder.Newest => query.OrderByDescending(x => x.offer.CreatedAtUtc),

            // "Popüler" = çok değerlendirme almış, yani gerçekten ders vermiş eğitmen.
            // Ortalama puana göre sıralamak tek 5 yıldızlı yeni kullanıcıyı tepeye taşırdı.
            OfferSortOrder.Popular => query
                .OrderByDescending(x => x.tutor.RatingCount)
                .ThenByDescending(x => x.tutor.AverageRating),

            OfferSortOrder.RatingDesc => query
                .OrderByDescending(x => x.tutor.AverageRating)
                .ThenByDescending(x => x.tutor.RatingCount),

            // Puanı en düşük: yeni/puansız eğitmenler (RatingCount=0) en başa dolmasın diye
            // önce değerlendirilmiş olanlar gelir.
            OfferSortOrder.RatingAsc => query
                .OrderByDescending(x => x.tutor.RatingCount > 0)
                .ThenBy(x => x.tutor.AverageRating),

            _ => query
                .OrderByDescending(x => x.offer.SelfAssessedLevel)
                .ThenByDescending(x => x.tutor.AverageRating)
                .ThenByDescending(x => x.tutor.RatingCount)
        };

        // Kararlı sıralama: eşit değerlerde PostgreSQL satır sırasını garanti etmez ve aynı
        // kayıt iki farklı sayfada görünebilir ya da hiç görünmeyebilir. Id son kırıcı olarak
        // eklenir — ThenBy ile, OrderBy ile DEĞİL: OrderBy yukarıdaki zinciri sıfırlardı.
        var rows = await ordered
            .ThenBy(x => x.offer.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new OfferCardDto(
                x.offer.Id,
                x.tutor.Id,
                x.tutor.DisplayName,
                x.tutor.AverageRating,
                x.tutor.RatingCount,
                x.topic.Id,
                x.topic.Name,
                x.subject.Name,
                x.category.Name,
                x.offer.SelfAssessedLevel,
                x.offer.Note,
                x.offer.IsVolunteer,
                x.offer.CreatedAtUtc))
            .ToListAsync(ct);

        return new PagedResult<OfferCardDto>(rows, totalCount, page, pageSize);
    }

    /// <summary>
    /// Verilen kategori ve tüm alt kategorilerinin id'leri. Ağacın tamamı önbellekten
    /// okunur (küçük ve durağan), gezinme bellekte yapılır.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveCategoryBranchAsync(Guid rootId, CancellationToken ct)
    {
        var all = await _cache.GetOrCreateAsync(
            "catalog:category-tree",
            CategoryTreeTtl,
            LoadCategoryTreeAsync,
            ct);

        /*
          ÖNBELLEK DOĞRULUĞU ETKİLEYEMEZ. Kategori önbellekte YOKSA bu "kategori yok"
          demek değildir — yeni eklenmiş de olabilir. Önbelleği tek gerçek kaynak saymak,
          yeni bir kategoriyi TTL boyunca (30 dk) filtrede sonuçsuz bırakıyordu: arayüzde
          pill görünüyor, tıklanınca "sonuç yok" çıkıyordu. e2e-discovery bunu yakaladı.

          Bu yüzden bulunamayan kategori için önbellek atlanıp katalog bir kez taze okunur.
          Maliyet küçük ve yalnızca gerçekten bilinmeyen kategori kimliklerinde ödenir.
        */
        if (all.All(c => c.Id != rootId))
        {
            all = await LoadCategoryTreeAsync(ct);

            if (all.All(c => c.Id != rootId))
            {
                return []; // Gerçekten bilinmeyen/pasif kategori → sonuç yok (tüm katalog DEĞİL).
            }

            // Taze ağaç bayat kaydın üzerine yazılır ki sonraki istekler yeniden okumasın.
            await _cache.RemoveByPrefixAsync("catalog:category-tree", ct);
        }

        var childrenByParent = all
            .Where(c => c.ParentId is not null)
            .GroupBy(c => c.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList());

        var result = new List<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(rootId);

        // Döngüsel veriye karşı ziyaret kümesi: kendi kendine parent olan bozuk bir kayıt
        // sonsuz döngüye sokmasın.
        var visited = new HashSet<Guid>();

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            result.Add(current);

            if (childrenByParent.TryGetValue(current, out var children))
            {
                foreach (var child in children)
                {
                    stack.Push(child);
                }
            }
        }

        return result;
    }

    private async Task<List<CategoryNode>> LoadCategoryTreeAsync(CancellationToken ct)
        => await _db.EducationCategories.AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => new CategoryNode(c.Id, c.ParentCategoryId))
            .ToListAsync(ct);

    /// <summary>Serileştirilebilir olmalı (önbelleğe JSON olarak yazılıyor).</summary>
    public sealed record CategoryNode(Guid Id, Guid? ParentId);

    /// <summary>
    /// Filtre kombinasyonunun kararlı imzası. Ham dize birleştirmek yerine hash alınıyor:
    /// arama metni Redis anahtarına doğrudan girseydi hem anahtar uzunluğu kontrolsüz
    /// büyürdü hem de ':' gibi karakterler anahtar alanını bozardı.
    /// </summary>
    private static string BuildCacheKey(
        SearchOffersQuery request,
        int page,
        int pageSize,
        IReadOnlyList<Guid>? categoryIds)
    {
        var signature = string.Join('|',
            request.Search?.Trim().ToLowerInvariant() ?? string.Empty,
            categoryIds is null ? string.Empty : string.Join(',', categoryIds.OrderBy(id => id)),
            request.SubjectId?.ToString() ?? string.Empty,
            request.TopicId?.ToString() ?? string.Empty,
            request.MinLevel?.ToString() ?? string.Empty,
            request.MaxLevel?.ToString() ?? string.Empty,
            request.MinRating?.ToString() ?? string.Empty,
            request.MaxRating?.ToString() ?? string.Empty,
            // Filtre anahtara GİRMELİ: aksi halde gönüllü filtresi açık ve kapalı aramalar
            // aynı önbellek satırını paylaşır ve biri diğerinin sonucunu görür.
            request.OnlyVolunteer ? "vol" : string.Empty,
            request.Sort.ToString(),
            page.ToString(),
            pageSize.ToString());

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(signature));
        return "search:offers:" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
