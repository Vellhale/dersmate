using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Economy;
using PeerLearn.Application.Options;
using PeerLearn.Domain.Community;

namespace PeerLearn.Application.Features.Economy;

/*
  ══════════════════════════════════════════════════════════════════════════════
  TOPLULUK ÖDÜLÜ — forumda alınan net oyun puana dönüşmesi.

  Ürün sahibi kararı (2026-08-29): puan kazanmanın tek yolu ders anlatmaktı ve foruma
  gelip hiç ders vermeyen kullanıcının seviyesi hep 1'de kalıyordu. Kur ve gerekçesi
  Domain/Community/Forum.cs → CommunityRewardRules.

  ─── NEDEN OY ANINDA DEĞİL, ARKA PLANDA ───────────────────────────────────────
  Ödülü oy verme yolunun içine koymak cazipti (anında geri bildirim) ama iki sorun
  çıkarıyordu:

    1. İKİ KİLİT birden gerekirdi — İÇERİK kilidi (oy sayaçları için, oy veren tarafta)
       ve YAZARIN CÜZDAN kilidi (basım için). İkisini her zaman aynı sırada almak
       gerekir, aksi halde iki eşzamanlı oy birbirini bekleyip kilitlenir. Oy veren ile
       yazar farklı kişiler olduğu için sıralama da doğal değil.
    2. Oy vermek SIK ve UCUZ bir işlem; her tıkta defter yazmayı denemek, ekonominin en
       pahalı yolunu en sık yola bağlamak olurdu.

  Arka plan işi ikisini de kapatıyor: tek seferde tek cüzdan kilidi, oy yolu hiç
  değişmiyor. Bedeli, ödülün en fazla bir tur (15 dk) gecikmesi — puan bir bakiye değil
  bir unvan olduğu için bu gecikmenin kullanıcıya maliyeti yok.

  ─── NEDEN HER TURDA YENİDEN HESAP ────────────────────────────────────────────
  İş, kullanıcının O ANKİ net oyundan hak edilen TOPLAMI hesaplayıp ödenmiş tutarı
  düşüyor (fark basılıyor). "Şu oy yeni geldi, şunu öde" biçiminde artımlı çalışsaydı
  içerik kaldırıldığında ya da oy geri alındığında toplam sapardı ve sapmayı düzeltecek
  hiçbir yol olmazdı. Yeniden hesap, moderasyonu ve oy geri almayı kendiliğinden
  doğru sonuca götürüyor.
  ══════════════════════════════════════════════════════════════════════════════
*/

public sealed record GrantCommunityRewardsCommand : IRequest<GrantCommunityRewardsResult>;

/// <param name="Failed">
/// Hata alıp ATLANAN kullanıcı sayısı.
/// </param>
/// <remarks>
/// ⚠️ BU ALAN BİR ARIZADAN SONRA EKLENDİ (2026-08-29) ve sebebi tam olarak şu:
///
/// İlk sürümde sonuç yalnızca (UsersRewarded, CreditsMinted) idi. Enum üyesinin adı
/// kolon sınırını aşınca (varchar(20)) her kullanıcıda SaveChanges patlıyor, döngü
/// hatayı yutup devam ediyor ve iş `{usersRewarded: 0, creditsMinted: 0}` dönüyordu —
/// yani "ödüllendirilecek kimse yok" ile "HERKES başarısız oldu" dışarıdan
/// AYIRT EDİLEMİYORDU. Arıza günlükte vardı ama ucun cevabı sağlıklı görünüyordu.
///
/// Sayaç bu iki durumu ayırıyor: 0 ödül + 0 hata = yapacak iş yoktu; 0 ödül + N hata =
/// sistemik bir sorun var.
/// </remarks>
public sealed record GrantCommunityRewardsResult(int UsersRewarded, int CreditsMinted, int Failed);

public sealed class GrantCommunityRewardsHandler
    : IRequestHandler<GrantCommunityRewardsCommand, GrantCommunityRewardsResult>
{
    /// <summary>
    /// Tek turda en fazla bu kadar kullanıcı ödüllendirilir.
    /// </summary>
    /// <remarks>
    /// Vade süpürmesindeki (ExpireCredits) kalıbın aynısı: sınırsız bir tur, tek bir
    /// koşumda yüzlerce cüzdan kilidi alıp uzun süre tutardı. Sınıra takılan kullanıcılar
    /// bir sonraki turda işleniyor — kimse atlanmıyor, yalnızca sıraya giriyor.
    /// </remarks>
    private const int MaxUsersPerRun = 200;

    private readonly IAppDbContext _db;
    private readonly CreditLedgerService _ledger;
    private readonly IDistributedLockProvider _locks;
    private readonly EconomyOptions _economy;
    private readonly ILogger<GrantCommunityRewardsHandler> _logger;

    public GrantCommunityRewardsHandler(
        IAppDbContext db,
        CreditLedgerService ledger,
        IDistributedLockProvider locks,
        IOptions<EconomyOptions> economy,
        ILogger<GrantCommunityRewardsHandler> logger)
    {
        _db = db;
        _ledger = ledger;
        _locks = locks;
        _economy = economy.Value;
        _logger = logger;
    }

    public async Task<GrantCommunityRewardsResult> Handle(
        GrantCommunityRewardsCommand request, CancellationToken ct)
    {
        /*
          YALNIZCA GÖRÜNÜR İÇERİK sayılıyor (Visible). Perdeli (UnderReview) ve kaldırılmış
          (Removed) içeriğin oyu ödüle girmiyor:
            • kaldırılan içerik kural ihlali — ihlal ödüllendirilemez
            • perdeli içerik hakkında henüz karar yok; şimdiden ödemek, karar "kaldır"
              çıkarsa geri alınamayan bir ödeme bırakırdı

          NET OY (artı − eksi): 300 artı / 400 eksi alan bir gönderi topluluğun değersiz
          bulduğu bir katkıdır. Ham artıyla trolleme kârlı bir strateji olurdu.

          Sum(int?) ?? 0 — hiç içeriği olmayan kullanıcıda SUM boş küme döner ve doğrudan
          int'e toplamak çalışma anında patlar.
        */
        var adaylar = await _db.Users.AsNoTracking()
            .Select(u => new
            {
                u.Id,
                u.CommunityRewardedCredits,
                NetOy =
                    (_db.CommunityPosts
                        .Where(p => p.AuthorUserId == u.Id && p.Status == ForumContentStatus.Visible)
                        .Sum(p => (int?)(p.UpvoteCount - p.DownvoteCount)) ?? 0) +
                    (_db.CommunityComments
                        .Where(c => c.AuthorUserId == u.Id && c.Status == ForumContentStatus.Visible)
                        .Sum(c => (int?)(c.UpvoteCount - c.DownvoteCount)) ?? 0)
            })
            // Hak ediş, ödenmişi geçiyorsa aday. Eşik altındakiler ve düşenler elenir.
            .Where(x => x.NetOy / CommunityRewardRules.NetUpvotesPerReward *
                        CommunityRewardRules.CreditsPerReward > x.CommunityRewardedCredits)
            .OrderBy(x => x.Id)
            .Take(MaxUsersPerRun)
            .ToListAsync(ct);

        var odullenen = 0;
        var toplamPuan = 0;
        var basarisiz = 0;

        foreach (var aday in adaylar)
        {
            try
            {
                // CÜZDAN KİLİDİ: basım cüzdan bakiyesini ve lot tablosunu değiştiriyor.
                // Anahtar kullanıcı bazında çünkü sorgunun grupladığı şey de kullanıcı.
                await using var walletLock = await _locks.AcquireAsync(
                    LockKeys.Wallet(aday.Id), TimeSpan.FromSeconds(_economy.LockTimeoutSeconds), ct);

                var basilan = await ConcurrencyRetry.RunAsync(_db, async () =>
                {
                    await using var tx = await _db.BeginTransactionAsync(cancellationToken: ct);

                    var user = await _db.Users.SingleAsync(u => u.Id == aday.Id, ct);
                    var wallet = await _ledger.EnsureWalletAsync(aday.Id, ct);

                    /*
                      NET OY TRANSACTION İÇİNDE YENİDEN OKUNUYOR, yukarıdaki adaylık
                      sorgusundan taşınmıyor. Aradan geçen sürede oy geri alınmış ya da
                      içerik kaldırılmış olabilir; eski sayıyla ödemek, var olmayan bir
                      katkıyı ödüllendirirdi.
                    */
                    var netOy =
                        (await _db.CommunityPosts.AsNoTracking()
                            .Where(p => p.AuthorUserId == aday.Id && p.Status == ForumContentStatus.Visible)
                            .SumAsync(p => (int?)(p.UpvoteCount - p.DownvoteCount), ct) ?? 0) +
                        (await _db.CommunityComments.AsNoTracking()
                            .Where(c => c.AuthorUserId == aday.Id && c.Status == ForumContentStatus.Visible)
                            .SumAsync(c => (int?)(c.UpvoteCount - c.DownvoteCount), ct) ?? 0);

                    var amount = await _ledger.MintCommunityRewardAsync(user, wallet, netOy, ct);

                    await _db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    return amount;
                }, ct: ct);

                if (basilan > 0)
                {
                    odullenen++;
                    toplamPuan += basilan;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Tek kullanıcı tüm turu düşürmemeli (vade süpürmesindeki kalıp).
                basarisiz++;
                _logger.LogError(ex, "Topluluk ödülü kullanıcıda başarısız, atlandı ({UserId}).", aday.Id);
                _db.ClearChangeTracker();
            }
        }

        if (toplamPuan > 0)
        {
            _logger.LogInformation(
                "Topluluk ödülü: {Kullanici} kullanıcıya {Puan} puan basıldı.", odullenen, toplamPuan);
        }

        /*
          ADAY VARDI AMA HİÇBİRİ ÖDENEMEDİ — bu bir arıza işareti ve ayrıca uyarılıyor.
          Tek tek hatalar zaten LogError'a düşüyor ama onlar kullanıcı bazında; buradaki
          satır ÖRÜNTÜYÜ söylüyor. Sistemik bir sorunda (şema uyuşmazlığı, kilit
          sağlayıcısı kapalı) günlükte tek bir okunabilir cümle kalıyor.
        */
        if (basarisiz > 0 && odullenen == 0)
        {
            _logger.LogWarning(
                "Topluluk ödülü: {Aday} adayın TAMAMI başarısız oldu ({Hata} hata). " +
                "Sistemik bir sorun olabilir.", adaylar.Count, basarisiz);
        }

        return new GrantCommunityRewardsResult(odullenen, toplamPuan, basarisiz);
    }
}
