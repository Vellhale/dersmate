using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Domain.Community;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Features.Discovery;

/// <summary>
/// Üniversite ağı araması — sonuç birimi KULLANICI, ilan değil.
///
/// SearchOffers'tan farkı temeldir ve bu yüzden ayrı bir sorgudur: orada taneciklik
/// "bir eğitmenin bir konudaki ilanı"dır ve filtreler (konu, seviye) doğal olarak oraya
/// oturur. Üniversite tarafında ders, konu ve 0-5 konu hâkimiyeti KAVRAMI YOK; aranan
/// şey kişinin kendisi. İki kişi eşleşiyor, sohbet açılıyor, gerisi onların arasında.
///
/// Filtre yalnızca iki alan: <see cref="University"/> ve <see cref="Department"/>.
/// Bilerek üçüncü bir ölçüt eklenmedi — "üniversite + bölüm" ikilisi bu ağda birini
/// bulmak için yeterli ve her ek filtre, kavramı yeniden ders/konu'ya doğru çeker.
///
/// GENEL SEVİYE (1-10) BURADA DA VAR ve bu bilinçli: seviye krediden türeyen hesap
/// çapında bir niteliktir, YKS'ye özgü değil. Kaldırılan şey ders/konu/konu-seviyesi.
/// </summary>
public sealed record SearchUniversityPeersQuery(
    Guid? CurrentUserId,
    string? University,
    string? Department,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<UniversityPeerDto>>;

/// <summary>
/// Üniversite kartı. Konu/ders alanı YOK — kasıtlı.
///
/// <paramref name="Level"/> sunucudan hazır gelir; arayüz eşik tablosu taşımaz
/// (bkz. frontend/src/lib/seviye.js).
/// </summary>
public sealed record UniversityPeerDto(
    Guid UserId,
    string DisplayName,
    string? University,
    string? Department,
    decimal AverageRating,
    int RatingCount,
    int Level,
    DateTime CreatedAtUtc);

public sealed class SearchUniversityPeersHandler
    : IRequestHandler<SearchUniversityPeersQuery, PagedResult<UniversityPeerDto>>
{
    private const int MaxPageSize = 50;

    private readonly IAppDbContext _db;

    public SearchUniversityPeersHandler(IAppDbContext db) => _db = db;

    public async Task<PagedResult<UniversityPeerDto>> Handle(
        SearchUniversityPeersQuery request,
        CancellationToken ct)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        /*
          ÖNBELLEK YOK — SearchOffers'tan bilinçli ayrım.

          Oradaki önbellek katalog geneli bir birleştirmeyi (offer × topic × subject ×
          category × user) koruyor. Burada tek tablo ve iki metin karşılaştırması var;
          önbellek, kazandırdığından çok anahtar üretme/geçersizleştirme yüzeyi getirirdi.
          Ayrıca profil bilgisi değişince sonucun anında tazelenmesi burada daha değerli:
          kullanıcı bölümünü düzeltip aramaya döndüğünde kendini bulamamak kafa karıştırır.
        */

        // Üniversite ağı = üniversite bilgisini GİRMİŞ kullanıcılar. Bilgi girilmemişse
        // kişi bu ağın parçası değildir; boş bir kart göstermek "bölümü yok" gibi okunur.
        var query = _db.Users
            .AsNoTracking()
            .Where(u => u.Status == UserStatus.Active && u.University != null && u.University != "");

        if (!string.IsNullOrWhiteSpace(request.University))
        {
            // ILike DEĞİL ToLower().Contains(): EF.Functions.ILike Npgsql'e (Infrastructure)
            // bağımlılık demek ve Application katmanının oraya bakması yasak. Aynı karar
            // SearchOffers'ta da alındı; ikisi aynı yolu kullansın diye burada tekrarlandı.
            var u = request.University.Trim().ToLowerInvariant();
            query = query.Where(x => x.University!.ToLower().Contains(u));
        }

        if (!string.IsNullOrWhiteSpace(request.Department))
        {
            var d = request.Department.Trim().ToLowerInvariant();
            query = query.Where(x => x.Department != null && x.Department.ToLower().Contains(d));
        }

        // Sayım sıralamadan ÖNCE: boş sonuçta sıralama ve sayfalama boşuna çalışmasın.
        var totalCount = await query.CountAsync(ct);
        if (totalCount == 0)
        {
            return PagedResult<UniversityPeerDto>.Empty(page, pageSize);
        }

        /*
          SIRALAMA SABİT: önce puanı yüksek olan, sonra yeni katılan.

          Sıralama seçeneği sunulmadı çünkü bu listede kıyaslanacak tek nesnel ölçüt
          değerlendirme ortalaması. "Popüler" (ilan görüntülenmesi) burada karşılıksız,
          "seviye" ise kaç ders anlattığını söyler — üniversite ağında aranan şey o değil.

          ThenBy(Id): kararlı sıralama. Aynı puana sahip iki kişi arasında sıra her
          sorguda değişirse sayfa 2'ye geçen kullanıcı bazılarını hiç görmez, bazılarını
          iki kez görür.
        */
        var rows = await query
            .OrderByDescending(u => u.AverageRating)
            .ThenByDescending(u => u.CreatedAtUtc)
            .ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                u.Id,
                u.DisplayName,
                u.University,
                u.Department,
                u.AverageRating,
                u.RatingCount,
                u.TotalEarnedCredits,
                u.CreatedAtUtc,
            })
            .ToListAsync(ct);

        // Seviye BELLEKTE hesaplanıyor: UserLevelRules.Hesapla bir C# fonksiyonu, EF onu SQL’e
        // çeviremez. Projeksiyonun içinde çağrılırsa sorgu ya istemci tarafı
        // değerlendirmeye düşer ya da çalışma anında patlar.
        var items = rows
            .Where(r => request.CurrentUserId is null || r.Id != request.CurrentUserId.Value)
            .Select(r => new UniversityPeerDto(
                r.Id,
                r.DisplayName,
                r.University,
                r.Department,
                r.AverageRating,
                r.RatingCount,
                UserLevelRules.Hesapla(r.TotalEarnedCredits).Level,
                r.CreatedAtUtc))
            .ToList();

        // TotalCount ham sayı: kullanıcı kendi kaydına denk gelen sayfada pageSize-1
        // sonuç görebilir. SearchOffers'ta da aynı ödün verildi; sayfa çubuğunu bozmaz.
        return new PagedResult<UniversityPeerDto>(items, totalCount, page, pageSize);
    }
}
