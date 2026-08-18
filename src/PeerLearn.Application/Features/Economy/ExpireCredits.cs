using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Economy;
using PeerLearn.Application.Options;

namespace PeerLearn.Application.Features.Economy;

/// <summary>
/// 30 GÜN KURALI (Modül 4.2): vadesi dolan kredileri yakan background job komutu.
/// Cüzdan cüzdan çalışır: her cüzdan kendi lock + transaction'ı ile işlenir ki tek
/// sorunlu cüzdan tüm süpürmeyi düşürmesin. Filtered index (ExpiresAtUtc WHERE
/// RemainingAmount > 0) taramayı ucuz tutar.
/// </summary>
public sealed record ExpireCreditsCommand : IRequest<ExpireCreditsResult>;

public sealed record ExpireCreditsResult(int WalletsProcessed, int CreditsExpired);

public sealed class ExpireCreditsHandler : IRequestHandler<ExpireCreditsCommand, ExpireCreditsResult>
{
    private const int MaxWalletsPerRun = 200;

    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly CreditLedgerService _ledger;
    private readonly IDistributedLockProvider _locks;
    private readonly EconomyOptions _economy;
    private readonly ILogger<ExpireCreditsHandler> _logger;

    public ExpireCreditsHandler(IAppDbContext db, IClock clock, CreditLedgerService ledger,
        IDistributedLockProvider locks, IOptions<EconomyOptions> economy, ILogger<ExpireCreditsHandler> logger)
    {
        _db = db;
        _clock = clock;
        _ledger = ledger;
        _locks = locks;
        _economy = economy.Value;
        _logger = logger;
    }

    public async Task<ExpireCreditsResult> Handle(ExpireCreditsCommand request, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        // Distinct SONRASI sıralama şart: aksi halde Take hangi cüzdanları seçeceği belirsiz olur
        // (EF10102 uyarısı) ve bazı cüzdanlar turdan tura atlanabilirdi. Yakılan lotlar sonuç
        // kümesinden düştüğü için Id sırası her turda ilerleme garantisi verir.
        var due = await (
                from lot in _db.CreditLots
                join wallet in _db.Wallets on lot.WalletId equals wallet.Id
                where lot.RemainingAmount > 0 && lot.ExpiresAtUtc <= now
                select new { wallet.Id, wallet.UserId })
            .Distinct()
            .OrderBy(x => x.Id)
            .Take(MaxWalletsPerRun)
            .ToListAsync(ct);

        var processed = 0;
        var totalExpired = 0;

        foreach (var entry in due)
        {
            try
            {
                await using var walletLock = await _locks.AcquireAsync(
                    LockKeys.Wallet(entry.UserId), TimeSpan.FromSeconds(_economy.LockTimeoutSeconds), ct);

                var burned = await ConcurrencyRetry.RunAsync(_db, async () =>
                {
                    await using var tx = await _db.BeginTransactionAsync(cancellationToken: ct);

                    var wallet = await _db.Wallets.SingleAsync(w => w.Id == entry.Id, ct);
                    var amount = await _ledger.ExpireDueLotsAsync(wallet, ct);

                    await _db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    return amount;
                }, ct: ct);

                processed++;
                totalExpired += burned;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (AppException ex)
            {
                _logger.LogWarning("Vade süpürmesi cüzdanı atladı ({WalletId}): {Code}", entry.Id, ex.Code);
                _db.ClearChangeTracker();
            }
            catch (Exception ex)
            {
                // Tek sorunlu cüzdan tüm süpürmeyi düşürmemeli (sınıf sözleşmesi) — atla ve devam et.
                _logger.LogError(ex, "Vade süpürmesi cüzdanda başarısız, atlandı ({WalletId}).", entry.Id);
                _db.ClearChangeTracker();
            }
        }

        if (totalExpired > 0)
        {
            _logger.LogInformation("Vade dolumu: {Wallets} cüzdanda {Credits} kredi yakıldı.", processed, totalExpired);
        }

        return new ExpireCreditsResult(processed, totalExpired);
    }
}
