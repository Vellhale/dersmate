using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;
using PeerLearn.Domain.Matchmaking;

namespace PeerLearn.Application.Features.Matchmaking;

/// <summary>
/// "Bir kullanıcının arkadaşları" sorgusunun TEK KAYNAĞI.
/// </summary>
/// <remarks>
/// ARKADAŞ = KABUL EDİLMİŞ EŞLEŞME. Yeni bir tablo ya da kavram YOK: ürün zaten
/// "istek kabul edilince sohbet açılır" diyor, arkadaşlık tam olarak o ilişki. Ayrı bir
/// Friendship tablosu açmak aynı gerçeği iki yerde tutmak ve ikisinin bir gün çelişmesi
/// demekti.
///
/// ─── ⚠️ NEDEN UNION, NEDEN TEK BİR OR'LU WHERE DEĞİL ──────────────────────────
/// İki sebep, ikisi de ölçülebilir:
///
///  1. TEKİLLEŞTİRME ŞART. Aynı çift arasında BİRDEN FAZLA 'Accepted' satırı olabilir ve
///     bu bir veri bozukluğu değil, tasarımın sonucu: tekillik indeksleri yalnızca
///     Status='Pending' üzerinde ve (Initiator, Responder, RequestedTopicId) yönlü
///     (MatchmakingConfigurations). Yani A→B "Matematik" kabul + A→B "Fizik" kabul +
///     B→A konusuz kabul = aynı çift için ÜÇ satır. Ham bir sayım o kişiyi ÜÇ ARKADAŞ
///     sayar. SQL UNION küme birleşimidir, tekilleştirmeyi kendisi yapar.
///
///     Bu hata SESSİZDİR ve tek eşleşmeli bir test kullanıcısıyla bakılırsa HİÇ görünmez —
///     bu yüzden testte aynı çifte ikinci bir kabul yazılıp sayının 1 kaldığı sınanıyor.
///
///  2. İNDEKS. Her bacak (InitiatorUserId, Status) ve (ResponderUserId, Status) bileşik
///     indekslerinin BİREBİR önekidir. Tek OR'lu bir WHERE de doğru sonuç verir ama planı
///     BitmapOr'a bırakır ve tekilleştirmeyi ayrıca yazdırır.
///
/// ─── BU SORGU NE YAPMIYOR ────────────────────────────────────────────────────
/// Yalnızca KİMLİK döndürüyor. Kullanıcının aktif olup olmadığı (silinmiş/banlı/askıda)
/// ve engel süzgeci BİLEREK burada değil: ikisi de "kimin gözünden" sorusuna bağlı ve
/// çağıran tarafta karara bağlanmalı. Buraya gömülselerdi her çağıran aynı varsayımı
/// devralır, farklı bir gözden bakmak isteyen ise sessizce yanlış sonuç alırdı.
/// Çağıranın yapması gerekenler <see cref="Features.Community.GetProfileFriendsHandler"/>
/// içinde sıralı.
/// </remarks>
public static class ArkadasSorgusu
{
    /// <summary>
    /// <paramref name="kisi"/> ile arkadaş olan kullanıcıların kimlikleri — TEKİL.
    /// Sonuç <c>IQueryable</c>: çağıran üstüne süzgeç/birleşim ekleyip TEK sorguda koşsun,
    /// listeyi belleğe çekip kişi başına sorgu atmasın (N+1).
    /// </summary>
    public static IQueryable<Guid> Idler(IAppDbContext db, Guid kisi) =>
        db.Matches
            .Where(m => m.InitiatorUserId == kisi && m.Status == MatchStatus.Accepted)
            .Select(m => m.ResponderUserId)
            .Union(
                db.Matches
                    .Where(m => m.ResponderUserId == kisi && m.Status == MatchStatus.Accepted)
                    .Select(m => m.InitiatorUserId));
}
