using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Options;
using PeerLearn.Domain.Economy;
using PeerLearn.Domain.Identity;
using PeerLearn.Domain.Scheduling;

namespace PeerLearn.Application.Economy;

/// <summary>
/// Kredi defterinin tek yazma noktası. Cüzdan/lot/defter tutarlılığı yalnızca bu sınıf
/// üzerinden değişir.
///
/// SÖZLEŞME (tüm public metotlar için):
///   - Çağıran, ilgili cüzdan için distributed lock almış ve bir DB transaction
///     (ReadCommitted) başlatmış olmalıdır. Bu sınıf SaveChanges/Commit ÇAĞIRMAZ;
///     atomiklik sınırını use-case handler'ı çizer.
///   - Tüm bakiye değişimleri xmin (optimistic concurrency) korumalı satırlar üzerinden
///     yapılır; çakışmada handler ConcurrencyRetry ile baştan dener.
///
/// Korunan değişmezler:
///   Wallet.AvailableBalance == SUM(lot.RemainingAmount)
///   Dolaşımdaki toplam      == SUM(CreditTransaction.Amount)   (bkz. hakem paneli)
/// </summary>
/// <remarks>
/// SIFIR TOPLAM MODELİ TERK EDİLDİ.
///
/// Önceki tasarımda kredi bir para birimiydi: öğrenci öderdi, eğitmen alırdı, toplam
/// sabit kalırdı (arz = hoş geldin bonusu − vade yakımı) ve escrow bu takasın güvencesiydi.
/// Yeni modelde öğrenci hiçbir şey ödemiyor; kredi, eğitmenin ders anlatarak biriktirdiği
/// bir BAŞARI PUANI ve her onaylanan derste yoktan üretiliyor.
///
/// Bunun iki doğrudan sonucu var ve ikisi de bilinçli:
///  1. Escrow'un konusu kalmadı — bloke edilecek bir öğrenci kredisi yok. Hold yazan,
///     çözen ve iade eden tüm yollar kaldırıldı.
///  2. "Toplam sabit" değişmezinin yerini "her kredi kayıtlı bir basımdan gelir" aldı:
///     cüzdanlardaki toplam, defterdeki tüm hareketlerin toplamına eşit olmalı. Bu daha
///     zayıf değil, DAHA GENEL bir denetim — hangi ekonomi modeli olursa olsun geçerli.
///
/// KAYBEDİLEN FREN: yetersiz kredi kontrolü, aynı zamanda sınırsız ders açmayı engelleyen
/// tek ekonomik sınırdı. Yerine davranışsal tavanlar konuldu (bkz. MintGuard).
/// </remarks>
public sealed class CreditLedgerService
{
    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly EconomyOptions _options;

    public CreditLedgerService(IAppDbContext db, IClock clock, IOptions<EconomyOptions> options)
    {
        _db = db;
        _clock = clock;
        _options = options.Value;
    }

    /// <summary>Cüzdanı yoksa oluşturur (UserId unique index'i eşzamanlı oluşturmaya karşı son savunma).</summary>
    public async Task<Wallet> EnsureWalletAsync(Guid userId, CancellationToken ct)
    {
        var wallet = await _db.Wallets.SingleOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet is null)
        {
            wallet = new Wallet { UserId = userId };
            _db.Wallets.Add(wallet);
        }

        return wallet;
    }

    /// <summary>
    /// Tek seferlik hoş geldin kredisi. Idempotent: User.WelcomeCreditGrantedAtUtc doluysa no-op.
    /// DB backstop: cüzdan başına tek WelcomeBonus lotu (partial unique index).
    /// </summary>
    /// <remarks>
    /// NEDEN KALDIRILMADI: harcanacak bir yer kalmadığı için bu kredi artık işlevsiz
    /// görünüyor ve öyle. Yine de duruyor, çünkü ÇÜZDANI OLUŞTURAN TEK YOL bu — e-posta
    /// doğrulamasında çağrılıyor ve onay/iptal yollarındaki <c>Wallets.SingleAsync</c>
    /// çağrıları cüzdanın var olduğunu varsayıyor. Kaldırmak, cüzdansız kullanıcıda ders
    /// onayını 500'e düşürürdü.
    ///
    /// UNVANA SAYILMAZ: <see cref="User.TotalEarnedCredits"/> yalnızca ders kazancıyla
    /// artar. Hoş geldin kredisi bir başarı değil, bir hediyedir.
    ///
    /// Bu kredinin ürün olarak bir karşılığı kalıp kalmadığı açık bir soru — cüzdan
    /// oluşturma sorumluluğu kayıt anına taşınırsa tümüyle kaldırılabilir.
    /// </remarks>
    public async Task GrantWelcomeCreditAsync(User user, CancellationToken ct)
    {
        if (user.WelcomeCreditGrantedAtUtc is not null)
        {
            return;
        }

        var now = _clock.UtcNow;
        var wallet = await EnsureWalletAsync(user.Id, ct);
        var amount = _options.WelcomeCreditAmount;

        user.WelcomeCreditGrantedAtUtc = now;

        _db.CreditLots.Add(new CreditLot
        {
            WalletId = wallet.Id,
            InitialAmount = amount,
            RemainingAmount = amount,
            Source = CreditLotSource.WelcomeBonus,
            EarnedAtUtc = now,
            ExpiresAtUtc = now.AddDays(_options.WelcomeCreditValidityDays)
        });

        wallet.AvailableBalance += amount;
        wallet.UpdatedAtUtc = now;

        _db.CreditTransactions.Add(new CreditTransaction
        {
            WalletId = wallet.Id,
            Amount = amount,
            Type = CreditTransactionType.WelcomeBonus,
            BalanceAfter = wallet.AvailableBalance
        });
    }

    /// <summary>
    /// DERS ÖDÜLÜNÜN BASILMASI: ders onaylandığında eğitmene süreye göre puan üretilir.
    /// Öğrenci tarafında hiçbir hareket yoktur.
    /// </summary>
    /// <remarks>
    /// Eskiden bu metot escrow tahsilatıydı (CaptureHoldAsync): bloke kredi öğrenciden
    /// çıkar, eğitmene geçerdi ve iki bacak aynı CorrelationId ile toplam 0 ederdi.
    /// Artık tek bacak var; karşılığı olmayan bir basım.
    ///
    /// UNVAN SAYACI BURADA ARTIRILIYOR, ÇAĞRI YERLERİNDE DEĞİL. Basımın iki ayrı yolu var
    /// (normal onay ve itirazın eğitmen lehine sonuçlanması); sayacı çağrı yerlerine
    /// bıraksaydım birini eklemeyi unutmak kaçınılmazdı ve o yoldan kazanılan puanlar
    /// unvana hiç yansımadan sessizce kaybolurdu. Basım ve sayaç aynı yerde, aynı
    /// transaction'da.
    ///
    /// ÇİFTE BASIMA KARŞI: CreditLots.SourceSessionId ve (RelatedSessionId, Type) partial
    /// unique index'leri ders başına ikinci basımı INSERT anında reddeder. Bu index'ler
    /// escrow'dan kalma değil, YENİ modelin tek koruması — kaldırılmamalı.
    /// </remarks>
    /// <returns>Basılan puan (gönüllü derste 0).</returns>
    public async Task<int> MintLessonRewardAsync(LessonSession session, Wallet tutorWallet, CancellationToken ct)
    {
        /*
          GÖNÜLLÜ DERS — hiç basım yapılmaz.

          Kontrol çağrı yerlerine değil BURAYA konuldu: basıma dokunan iki yol var (onay ve
          itiraz kararı) ve her birine ayrı "if" yazmak birini atlamayı kaçınılmaz kılardı.
          0 tutarlı lot/hareket de YAZILMAZ — CK_CreditLots_InitialAmount > 0 ve
          CK_CreditTransactions_Amount <> 0 kısıtları zaten reddederdi.
        */
        if (session.IsVolunteer || session.CreditCost <= 0)
        {
            return 0;
        }

        var now = _clock.UtcNow;
        var amount = session.CreditCost;

        tutorWallet.AvailableBalance += amount;
        tutorWallet.UpdatedAtUtc = now;

        _db.CreditLots.Add(new CreditLot
        {
            WalletId = tutorWallet.Id,
            InitialAmount = amount,
            RemainingAmount = amount,
            Source = CreditLotSource.LessonEarning,
            SourceSessionId = session.Id,
            EarnedAtUtc = now,

            // SÜRESİZ: kazanılan puan yanmaz (bkz. CreditLot.ExpiresAtUtc gerekçesi).
            ExpiresAtUtc = null
        });

        _db.CreditTransactions.Add(new CreditTransaction
        {
            WalletId = tutorWallet.Id,
            Amount = amount,
            Type = CreditTransactionType.LessonEarning,
            RelatedSessionId = session.Id,
            CounterpartyUserId = session.StudentUserId,
            BalanceAfter = tutorWallet.AvailableBalance
        });

        // Unvanın dayandığı birikimli sayaç. Yalnızca artar; hiçbir yolda azalmaz.
        var tutor = await _db.Users.SingleAsync(u => u.Id == session.TutorUserId, ct);
        tutor.TotalEarnedCredits += amount;

        return amount;
    }

    /// <summary>
    /// YÖNETİM DÜZELTMESİ: cüzdana elle puan ekler (delta &gt; 0) ya da düşer (delta &lt; 0).
    /// Çağıran, cüzdan kilidini almış ve transaction açmış olmalıdır (sınıf sözleşmesi).
    /// </summary>
    /// <remarks>
    /// NEDEN VAR: <see cref="CreditTransactionType.AdminAdjustment"/> ve
    /// <see cref="CreditLotSource.AdminGrant"/> enum'da baştan beri duruyordu ama hiçbir kod
    /// yolu yazmıyordu. Bir basım kaçtığında ya da yanlış tarafa yazıldığında tek çare elle
    /// SQL'di — yani defteri düzelten işlemin kendisi denetim izi bırakmıyordu. En çok
    /// güvenilmesi gereken işlem, en az iz bırakan işlemdi.
    ///
    /// UNVAN SAYACINA DOKUNMAZ. <see cref="User.TotalEarnedCredits"/> yalnızca ders
    /// anlatarak artar; yönetim eliyle unvan dağıtılamaz. Bu aynı zamanda
    /// "TotalEarnedCredits == SUM(LessonEarning)" denetimini de korur — sayacı burada
    /// artırmak o eşitliği ilk düzeltmede kırardı.
    ///
    /// EKLEME vadesizdir: yanan bir düzeltme, düzeltmenin kendisini geri alırdı.
    ///
    /// DÜŞMEDE LOT SIRASI vadesi en yakından başlar (sistemin baştan beri belgelenmiş FIFO
    /// kuralı). Bu, geçiş öncesinden kalan vadeli lotları önce tüketir; süresiz ders
    /// kazançları en sona kalır.
    /// </remarks>
    /// <returns>Düzeltme sonrası kullanılabilir bakiye.</returns>
    public async Task<int> AdminAdjustAsync(Wallet wallet, int delta, CancellationToken ct)
    {
        if (delta == 0)
        {
            // CK_CreditTransactions_Amount zaten reddederdi; burada net hata mesajı veriliyor.
            throw new AppException(ErrorCodes.ValidationFailed, "Düzeltme tutarı sıfır olamaz.");
        }

        var now = _clock.UtcNow;

        if (delta > 0)
        {
            _db.CreditLots.Add(new CreditLot
            {
                WalletId = wallet.Id,
                InitialAmount = delta,
                RemainingAmount = delta,
                Source = CreditLotSource.AdminGrant,
                EarnedAtUtc = now,
                ExpiresAtUtc = null
            });

            wallet.AvailableBalance += delta;
            wallet.UpdatedAtUtc = now;

            _db.CreditTransactions.Add(new CreditTransaction
            {
                WalletId = wallet.Id,
                Amount = delta,
                Type = CreditTransactionType.AdminAdjustment,
                BalanceAfter = wallet.AvailableBalance
            });

            return wallet.AvailableBalance;
        }

        var dusulecek = -delta;

        /*
          BLOKE BAKİYEYE DOKUNULMAZ, YALNIZCA KULLANILABİLİR OLAN DÜŞÜLÜR.

          Locked, geçiş öncesinden kalan escrow satırlarını temsil ediyor ve sahibi bu
          bakiyeyi harcayamıyor; onu bir düzeltmeyle silmek, kaynağı duran bir kaydı
          kaynaksız bırakırdı (lot toplamı ile bakiye ayrışır). Yetmiyorsa işlem reddedilir.
        */
        if (dusulecek > wallet.AvailableBalance)
        {
            throw new AppException(ErrorCodes.AdjustmentExceedsBalance,
                $"Kullanılabilir bakiye {wallet.AvailableBalance} puan; {dusulecek} puan düşülemez.",
                statusCode: 409);
        }

        var lotlar = await _db.CreditLots
            .Where(l => l.WalletId == wallet.Id && l.RemainingAmount > 0)
            .OrderBy(l => l.ExpiresAtUtc == null)   // vadeli lotlar önce (null'lar sona)
            .ThenBy(l => l.ExpiresAtUtc)
            .ThenBy(l => l.EarnedAtUtc)
            .ToListAsync(ct);

        var hareket = new CreditTransaction
        {
            WalletId = wallet.Id,
            Amount = delta,
            Type = CreditTransactionType.AdminAdjustment
        };
        _db.CreditTransactions.Add(hareket);

        var kalan = dusulecek;
        foreach (var lot in lotlar)
        {
            if (kalan == 0)
            {
                break;
            }

            var pay = Math.Min(lot.RemainingAmount, kalan);
            lot.RemainingAmount -= pay;
            kalan -= pay;

            _db.CreditLotConsumptions.Add(new CreditLotConsumption
            {
                CreditLotId = lot.Id,
                CreditTransactionId = hareket.Id,
                Amount = pay
            });
        }

        /*
          SON SAVUNMA: bakiye ile lot toplamı ayrışmışsa (bir yerde elle SQL çalışmış
          olabilir) yukarıdaki bakiye kontrolü geçer ama lotlar yetmez. Sessizce eksik
          düşmektense işlemi düşürmek gerekir — aksi halde
          "AvailableBalance == SUM(lot.RemainingAmount)" değişmezi bozulurdu.
        */
        if (kalan > 0)
        {
            throw new AppException(ErrorCodes.AdjustmentExceedsBalance,
                $"Lot toplamı bakiyeyi karşılamıyor ({kalan} puan açık). Cüzdan tutarsız, elle inceleyin.",
                statusCode: 409);
        }

        wallet.AvailableBalance -= dusulecek;
        wallet.UpdatedAtUtc = now;
        hareket.BalanceAfter = wallet.AvailableBalance;

        return wallet.AvailableBalance;
    }

    /// <summary>
    /// Vadesi dolan lotların kalanını yakar, tek Expiry hareketi yazar ve hangi lotlardan
    /// yakıldığını consumption satırlarıyla izler. Background job çağırır.
    /// </summary>
    /// <remarks>
    /// ARTIK YALNIZCA ESKİ LOTLARI İLGİLENDİRİR. Yeni ders kazançları vadesiz açılıyor
    /// (ExpiresAtUtc = null) ve <c>null &lt;= now</c> karşılaştırması SQL'de hiçbir zaman
    /// doğru olmadığı için bu lotlar sorguya doğal olarak hiç girmez. Metot, hoş geldin
    /// kredileri ve geçiş öncesi kazanılmış vadeli lotlar için yaşamaya devam ediyor —
    /// silinseydi o lotlar sonsuza kadar bakiyede kalır ve panel toplamları şişerdi.
    /// </remarks>
    public async Task<int> ExpireDueLotsAsync(Wallet wallet, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        var dueLots = await _db.CreditLots
            .Where(l => l.WalletId == wallet.Id && l.RemainingAmount > 0
                        && l.ExpiresAtUtc != null && l.ExpiresAtUtc <= now)
            .ToListAsync(ct);

        if (dueLots.Count == 0)
        {
            return 0;
        }

        var total = dueLots.Sum(l => l.RemainingAmount);

        wallet.AvailableBalance -= total;
        wallet.UpdatedAtUtc = now;

        var expiry = new CreditTransaction
        {
            WalletId = wallet.Id,
            Amount = -total,
            Type = CreditTransactionType.Expiry,
            BalanceAfter = wallet.AvailableBalance
        };
        _db.CreditTransactions.Add(expiry);

        foreach (var lot in dueLots)
        {
            _db.CreditLotConsumptions.Add(new CreditLotConsumption
            {
                CreditLotId = lot.Id,
                CreditTransactionId = expiry.Id,
                Amount = lot.RemainingAmount
            });

            lot.RemainingAmount = 0;
        }

        return total;
    }

}
