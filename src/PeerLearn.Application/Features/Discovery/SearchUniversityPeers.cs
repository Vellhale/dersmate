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

    /// <summary>
    /// Yönetici/moderatör işareti — ForumAuthorDto.IsStaff ile aynı bayrak, aynı
    /// gerekçe: resmi hesabın hangisi olduğu keşif listelerinde de ayırt edilebilmeli.
    /// Sunucudan geliyor; istemcide türetilseydi sahte rozet üretilebilirdi.
    /// </summary>
    bool IsStaff,

    DateTime CreatedAtUtc);

public sealed class SearchUniversityPeersHandler
    : IRequestHandler<SearchUniversityPeersQuery, PagedResult<UniversityPeerDto>>
{
    private const int MaxPageSize = 50;

    /*
      ─── TÜRKÇE ARAMA: İKİ AYRI TUZAK, İKİ AYRI ÇARE ─────────────────────────────

      İlk sürüm `x.University.ToLower().Contains(term)` yazıyordu ve Türkçe üniversite
      adlarının NEREDEYSE HİÇBİRİNİ bulamıyordu. Sebebi iki katmanda:

      1. VERİTABANI C KOLASYONUNDA (docker-compose.yml: --locale=C). PostgreSQL'in
         `lower()`'ı argümanının kolasyonunu kullanır ve C kolasyonu ASCII DIŞINI
         KATLAMAZ. Ölçüldü: lower('Boğaziçi Üniversitesi') → 'boğaziçi Üniversitesi'
         — baştaki B düştü, Ü olduğu gibi kaldı. C# tarafı ise ToLowerInvariant() ile
         Ü'yü ü yapıyordu. İki taraf farklı katladığı için "üniversitesi" araması
         tablodaki YEDİ üniversitenin hiçbirini bulmuyordu; hata da sessizdi —
         kullanıcı "kimse yok" diye okuyordu.

         Çare: kolon ICU kolasyonuna çevriliyor (`und-x-icu`), böylece lower() Unicode'u
         katlıyor. `tr-x-icu` DEĞİL bilerek: Türkçe kolasyonda lower('I') = 'ı' ama
         .NET'in ToLowerInvariant()'ı 'i' verir — iki taraf yeniden ayrışırdı. Kök
         kolasyon ile ToLowerInvariant aynı sonucu üretiyor.

      2. NOKTALI İ İKİ TARAFTA İKİ FARKLI ŞEYE DÖNÜYOR. ICU, 'İ' (U+0130) için
         'i' + U+0307 (birleşen üstteki nokta) üretiyor — lower('İTÜ') 4 karakterlik
         'i̇tü' oluyor. .NET ise U+0130'a hiç dokunmuyor. Çare aşağıdaki blokta:
         birleşen nokta atılıyor ve i/ı/İ üçlüsü tek harfte buluşturuluyor.

      Gerçek veriye karşı ölçüldü: üniversitesi→18, odtü→4, itü→3, boğaziçi→4 kayıt.
      Düzeltmeden önce hepsi 0 idi.

      ILIKE denendi ve ÇÖZMÜYOR: o da kolasyona bağlı, C kolasyonunda
      'Boğaziçi Üniversitesi' ILIKE '%üniversitesi%' → false.
      ─────────────────────────────────────────────────────────────────────────────
    */

    private const string IcuKolasyon = "und-x-icu";

    /// <summary>Birleşen üstteki nokta (U+0307) — ICU, noktalı İ'yi 'i' + bu karaktere ayırıyor.</summary>
    private const string BirlesenNokta = "̇";

    /// <summary>Büyük noktalı İ. .NET'in ToLowerInvariant()'ı buna DOKUNMUYOR (aşağıdaki nota bak).</summary>
    private const string BuyukNoktaliI = "İ";

    /// <summary>Küçük noktasız ı.</summary>
    private const string NoktasizI = "ı";

    /*
      ─── İ/I AİLESİ TEK HARFE İNDİRİLİYOR ────────────────────────────────────────

      Bu satırlar ölçümle yazıldı, ezberle değil. `dotnet fsi` ile bakıldı:

        "İTÜ".ToLowerInvariant()  → "İtü"   [U+0130 U+0074 U+00FC]

      Yani .NET, U+0130'u OLDUĞU GİBİ BIRAKIYOR — değişmez kültürde bu harfin tek
      karaktere sığan bir küçük harf karşılığı yok. PostgreSQL'in ICU'su ise aynı harfi
      'i' + U+0307 diye İKİ karaktere açıyor. İki taraf iki farklı sonuç üretiyordu ve
      içinde İ geçen HER arama boş dönüyordu ("Üniversitesi" tuttu, "ÜNİVERSİTESİ"
      tutmadı — fark yalnızca büyük İ).

      Ayrıca Türkçe'nin klasik ikilisi var: 'I' değişmez kültürde 'i'ye, Türkçe'de
      'ı'ya iner. "TIP" yazan kullanıcı "Tıp" kaydını bulamıyordu.

      Çare, i/ı/İ üçlüsünü ARAMA AMACIYLA tek harfte ('i') buluşturmak. Bu bilinçli bir
      ödün: arama biraz FAZLA eşleşir (ör. "sınıf" ile "sinif" aynı sayılır). Bir arama
      kutusunda fazla eşleşmek, hiç eşleşmemekten iyidir — ve kullanıcı zaten gözüyle
      seçiyor. Sıralama ya da tekillik kararlarında bu katlama KULLANILMAMALI.

      `tr-x-icu` kolasyonu neden değil: orada lower('I') = 'ı' olur ama .NET 'i' verir —
      iki taraf yeniden ayrışırdı. Kök kolasyon + açık harf eşlemesi, iki tarafı da
      aynı yere getiriyor.
      ─────────────────────────────────────────────────────────────────────────────
    */

    /// <summary>
    /// Kullanıcının yazdığı terimi kolonla AYNI kurallarla katlar.
    /// Sıra önemli: önce küçült, sonra ICU'nun ürettiği birleşen noktayı at, en son
    /// geriye kalan İ/ı harflerini 'i'ye indir.
    /// </summary>
    private static string AramaIcinKatla(string terim) =>
        terim.Trim()
            .ToLowerInvariant()
            .Replace(BirlesenNokta, string.Empty)
            .Replace(BuyukNoktaliI, "i")
            .Replace(NoktasizI, "i");

    /*
      KOLON KATLAMASI YARDIMCI METODA ÇIKARILAMAZ — SATIR İÇİNDE KALMAK ZORUNDA.

      İlk denemede `KolonKatla(x.University)` diye bir static metot yazıldı ve uç 500
      döndü. Sebep: EF Core, ifade ağacında gördüğü ÖZEL bir metodun gövdesine bakmaz;
      onu SQL'e çeviremeyeceği bir çağrı sayar. (Çevrilemeyen bir `Where`, EF Core 3+
      ile istemci tarafına düşmez — çalışma anında patlar.) Bu yüzden Collate/ToLower/
      Replace zinciri aşağıda, sorgunun içinde AÇIKÇA yazılı.

      Aynı zinciri değiştiren, İKİ yeri birden değiştirmeli (üniversite ve bölüm) ve
      terim tarafındaki AramaIcinKatla ile aynı kuralları uygulamalı — iki taraf
      ayrışırsa arama yine sessizce boş döner.
    */

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
            var u = AramaIcinKatla(request.University);
            query = query.Where(x =>
                EF.Functions.Collate(x.University!, IcuKolasyon)
                    .ToLower()
                    .Replace(BirlesenNokta, "")
                    .Replace(BuyukNoktaliI, "i")
                    .Replace(NoktasizI, "i")
                    .Contains(u));
        }

        if (!string.IsNullOrWhiteSpace(request.Department))
        {
            var d = AramaIcinKatla(request.Department);
            query = query.Where(x =>
                x.Department != null &&
                EF.Functions.Collate(x.Department, IcuKolasyon)
                    .ToLower()
                    .Replace(BirlesenNokta, "")
                    .Replace(BuyukNoktaliI, "i")
                    .Replace(NoktasizI, "i")
                    .Contains(d));
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
                u.Role,
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
                r.Role is UserRole.Admin or UserRole.Moderator,
                r.CreatedAtUtc))
            .ToList();

        // TotalCount ham sayı: kullanıcı kendi kaydına denk gelen sayfada pageSize-1
        // sonuç görebilir. SearchOffers'ta da aynı ödün verildi; sayfa çubuğunu bozmaz.
        return new PagedResult<UniversityPeerDto>(items, totalCount, page, pageSize);
    }
}
