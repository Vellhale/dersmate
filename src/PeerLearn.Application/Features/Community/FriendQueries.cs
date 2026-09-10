using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Features.Identity;
using PeerLearn.Application.Features.Matchmaking;
using PeerLearn.Domain.Community;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Features.Community;

/// <summary>Arkadaş listesindeki tek satır. Avatar YOK — avatar userId'den türüyor.</summary>
/// <remarks>
/// ForumAuthorDto'ya çok benziyor ama ONA BAĞLANMADI: adı "forum yazarı" diyor ve
/// <c>IsStaff</c> bayrağını da sürüklüyor. O bayrağın hangi yüzeylerde göründüğü
/// ForumAuthorDto'nun kendi notunda gerekçeli olarak sayılı; arkadaş listesine
/// sızdırmak o zinciri sessizce bozardı.
/// </remarks>
public sealed record ProfileFriendDto(Guid UserId, string DisplayName, int Level);

/// <param name="FriendCount">
/// Profil sahibinin arkadaş sayısı. HERKESE AYNI görünür — gerekçe handler'da.
/// </param>
/// <param name="Friends">
/// Tam liste. YALNIZCA <paramref name="IsSelf"/> true iken dolu; başkasının profilinde
/// BOŞ döner. Kural uç imzasıyla değil, burada ve handler'da birlikte tutuluyor.
/// </param>
/// <param name="MutualFriends">
/// Ortak arkadaşlar — yalnızca BAŞKASININ profilinde dolu. En fazla
/// <c>OrtakTavani</c> kişi; toplam <paramref name="MutualCount"/>'ta.
/// </param>
public sealed record ProfileFriendsDto(
    int FriendCount,
    bool IsSelf,
    IReadOnlyList<ProfileFriendDto> Friends,
    int MutualCount,
    IReadOnlyList<ProfileFriendDto> MutualFriends);

public sealed record GetProfileFriendsQuery(Guid UserId, Guid ViewerUserId)
    : IRequest<ProfileFriendsDto>;

/// <summary>
/// Profil sayfasının arkadaş bölümü.
/// </summary>
/// <remarks>
/// ─── NEDEN AYRI BİR UÇ, NEDEN UserProfileDto'YA ALAN EKLENMEDİ ───────────────
/// Mobil uygulama AYRI BİR DEPODA ve bu depodan doğrulanamıyor. JSON'a alan eklemek
/// çoğu ayrıştırıcıda zararsızdır (bilinmeyen anahtar yok sayılır) ama kotlinx
/// .serialization VARSAYILAN AYARDA bilinmeyen anahtarda İSTİSNA ATAR — yani
/// <c>api/v1/users/{id}/profile</c> yanıtına alan eklemek, mağazadaki mobil sürümde
/// profil ekranını komple beyaza düşürebilir. Ayrı uç bu riski sıfırlıyor: eski
/// istemci yeni ucu hiç çağırmaz.
///
/// İkinci fayda: profil kartı hızlı kalıyor. GetUserProfileHandler zaten 5 gidiş-dönüş
/// koşuyor; arkadaş sorguları oraya eklenseydi her profil açılışı 7'ye çıkardı.
///
/// ─── ÜÇ YÜZEY, ÜÇ AYRI "KİMİN GÖZÜNDEN" KARARI ──────────────────────────────
/// Engelleme KABUL EDİLMİŞ eşleşmeyi kapatmıyor (bilinçli sınır, gerekçe
/// SendMessage.cs ve UserBlocks.cs). Yani engellenen kişinin satırı 'Accepted' KALIYOR
/// ve arkadaş sorguları engeli AYRI AYRI elemek zorunda. Üçü de elenmezse engel tam
/// olarak bu yüzeyden delinir — hata mesajıyla değil, sessizce görünen bir kartla.
///
///  1. SAYI → yalnızca SAHİBİN engelleri elenir. Herkese aynı sayı görünür.
///     Bakanın engelleri de uygulansaydı sayı KİŞİDEN KİŞİYE OYNARDI ve bu dolaylı bir
///     sızıntı ucu açardı: sayının kendisi "beni engelledi mi" sorusunun cevabına
///     yaklaşan bir gösterge olurdu. Engellemek zaten "o kişi artık arkadaşım değil"
///     demek, dolayısıyla sahibin engelinin sayıdan düşmesi doğru davranış.
///  2. TAM LİSTE → sahip = bakan olduğu için (1) ile aynı süzgeç. Sayı ile liste
///     BİREBİR tutar; ayrışırlarsa "12 arkadaş" yazıp 11 kart gösterilir. Bu hata bu
///     projede bir kez yapıldı (SearchUniversityPeers totalCount) ve düzeltildi.
///  3. ORTAK ARKADAŞLAR → HEM sahibin HEM bakanın engelleri elenir. Bakanınki de
///     elenmezse, engellediğim biri başkasının profilinde karşıma çıkar.
///
/// ─── AKTİF OLMAYAN HESAPLAR ─────────────────────────────────────────────────
/// Hesap silme Users satırını SİLMİYOR, anonimleştiriyor (23 yabancı anahtar bakıyor)
/// ve eşleşme satırları duruyor. Süzgeç olmasaydı liste "Silinmiş kullanıcı"
/// kartlarıyla dolardı. <c>Status == Active</c> tek koşulu Deleted/Banned/Suspended/
/// PendingVerification'ın dördünü birden kapatıyor — SearchUniversityPeers ve
/// GetMatchSuggestions da aynı tek koşulu kullanıyor.
///
/// ─── SIRALAMA ───────────────────────────────────────────────────────────────
/// Ada göre, ICU kolasyonuyla. "En son eklenen üstte" daha canlı olurdu ama arkadaşlık
/// tarihi bu modelde tek bir satırda durmuyor (aynı çiftin birden fazla kabul satırı
/// olabiliyor); tarihi taşımak için UNION'ı çift alanlı yapmak gerekirdi ve o zaman
/// küme birleşimi tekilleştirmeyi ARTIK YAPMAZDI — sayıyı şişiren tam o hata geri
/// gelirdi. Alfabetik sıra kararlı, tekrarlanabilir ve sayfalamaya uygun.
/// </remarks>
public sealed class GetProfileFriendsHandler
    : IRequestHandler<GetProfileFriendsQuery, ProfileFriendsDto>
{
    /// <summary>Tam listede dönen en fazla kişi. Profil bölümü bir liste sayfası değil.</summary>
    private const int ListeTavani = 100;

    /// <summary>
    /// Ortak arkadaşlarda gösterilen en fazla kişi. Toplam ayrıca dönüyor, yani
    /// "Ali, Ayşe ve 7 kişi daha" yazılabiliyor. Tavan olmasaydı çok arkadaşlı iki
    /// kullanıcının profili yüzlerce satırlık bir yanıt üretirdi ve bunu en çok mobil
    /// hisseder.
    /// </summary>
    private const int OrtakTavani = 12;

    /// <summary>
    /// Türkçe ad sıralaması. Veritabanı C kolasyonunda (docker-compose: --locale=C) ve
    /// orada 'Ş' ASCII'nin dışında kaldığı için sıra bozuk çıkıyor. Aynı sabit
    /// SearchUniversityPeers'ta da var; ikisi de aynı gerekçeye yaslanıyor.
    /// </summary>
    private const string IcuKolasyon = "und-x-icu";

    private readonly IAppDbContext _db;

    public GetProfileFriendsHandler(IAppDbContext db) => _db = db;

    public async Task<ProfileFriendsDto> Handle(GetProfileFriendsQuery request, CancellationToken ct)
    {
        var kendisi = request.UserId == request.ViewerUserId;

        /*
          Sahibin arkadaşları: kimlikler → sahibin engelleri elendi → aktif kullanıcılar.
          Üçü de SORGUDA; hiçbiri belleğe çekilip süzülmüyor. Belleğe çekilseydi hem
          N+1 riski doğardı hem de sayım elenmemiş küme üzerinden yapılırdı.
        */
        var sahipIdler = EngelSorgusu.Engelsiz(
            _db, ArkadasSorgusu.Idler(_db, request.UserId), request.UserId);

        var sahipAktif = _db.Users
            .AsNoTracking()
            .Where(u => u.Status == UserStatus.Active && sahipIdler.Contains(u.Id));

        var sayi = await sahipAktif.CountAsync(ct);

        var liste = kendisi
            ? await KisilerAsync(sahipAktif, ListeTavani, ct)
            : (IReadOnlyList<ProfileFriendDto>)Array.Empty<ProfileFriendDto>();

        if (kendisi)
        {
            // Kendi profilinde ortak arkadaş kavramı yok: kendinle ortak arkadaşın
            // herkes olurdu. IsSelf dallanması bu iki sorguyu bedavaya atlatıyor.
            return new ProfileFriendsDto(sayi, IsSelf: true, liste, 0, Array.Empty<ProfileFriendDto>());
        }

        var bakanIdler = EngelSorgusu.Engelsiz(
            _db, ArkadasSorgusu.Idler(_db, request.ViewerUserId), request.ViewerUserId);

        var ortakAktif = _db.Users
            .AsNoTracking()
            .Where(u => u.Status == UserStatus.Active
                        && sahipIdler.Contains(u.Id)
                        && bakanIdler.Contains(u.Id));

        var ortakSayi = await ortakAktif.CountAsync(ct);
        var ortak = ortakSayi == 0
            ? (IReadOnlyList<ProfileFriendDto>)Array.Empty<ProfileFriendDto>()
            : await KisilerAsync(ortakAktif, OrtakTavani, ct);

        return new ProfileFriendsDto(sayi, IsSelf: false, liste, ortakSayi, ortak);
    }

    /// <summary>
    /// Kullanıcı sorgusunu karta çevirir.
    /// </summary>
    /// <remarks>
    /// ⚠️ SEVİYE BELLEKTE HESAPLANIYOR. <c>UserLevelRules.Hesapla</c> bir C# fonksiyonu;
    /// projeksiyonun İÇİNDE çağrılırsa EF onu SQL'e çeviremez ve sorgu ya istemci
    /// tarafı değerlendirmeye düşer ya da çalışma anında patlar. Aynı tuzak
    /// SearchUniversityPeers ve GetMatchSuggestions'ta da yorumla işaretli.
    /// </remarks>
    private static async Task<IReadOnlyList<ProfileFriendDto>> KisilerAsync(
        IQueryable<User> sorgu, int tavan, CancellationToken ct)
    {
        var satirlar = await sorgu
            .OrderBy(u => EF.Functions.Collate(u.DisplayName, IcuKolasyon))
            .ThenBy(u => u.Id)
            .Take(tavan)
            .Select(u => new { u.Id, u.DisplayName, u.TotalEarnedCredits })
            .ToListAsync(ct);

        return satirlar
            .Select(r => new ProfileFriendDto(
                r.Id, r.DisplayName, UserLevelRules.Hesapla(r.TotalEarnedCredits).Level))
            .ToList();
    }
}
