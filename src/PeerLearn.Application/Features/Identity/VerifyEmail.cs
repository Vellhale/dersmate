using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Economy;
using PeerLearn.Application.Options;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Features.Identity;

public sealed record VerifyEmailCommand(string Token) : IRequest<VerifyEmailResult>;

public sealed record VerifyEmailResult(bool WelcomeCreditGranted);

/// <summary>
/// E-posta doğrulama + tek seferlik hoş geldin kredisi (İş Kuralı: Modül 1.3).
/// Kredi, doğrulama ile AYNI transaction'da verilir; User.xmin + cüzdan başına tek
/// WelcomeBonus lotu (partial unique) eşzamanlı çift doğrulamada çift krediyi engeller.
/// </summary>
public sealed class VerifyEmailHandler : IRequestHandler<VerifyEmailCommand, VerifyEmailResult>
{
    private readonly IAppDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IClock _clock;
    private readonly CreditLedgerService _ledger;
    private readonly IDistributedLockProvider _locks;
    private readonly EconomyOptions _economy;

    public VerifyEmailHandler(IAppDbContext db, ITokenService tokens, IClock clock,
        CreditLedgerService ledger, IDistributedLockProvider locks, IOptions<EconomyOptions> economy)
    {
        _db = db;
        _tokens = tokens;
        _clock = clock;
        _ledger = ledger;
        _locks = locks;
        _economy = economy.Value;
    }

    public async Task<VerifyEmailResult> Handle(VerifyEmailCommand request, CancellationToken ct)
    {
        var userId = _tokens.ValidatePurposeToken(request.Token, RegisterHandler.EmailVerifyPurpose)
                     ?? throw new AppException(ErrorCodes.InvalidToken,
                         "Doğrulama bağlantısı geçersiz veya süresi dolmuş.", statusCode: 401);

        await using var walletLock = await _locks.AcquireAsync(
            LockKeys.Wallet(userId), TimeSpan.FromSeconds(_economy.LockTimeoutSeconds), ct);

        return await ConcurrencyRetry.RunAsync(_db, async () =>
        {
            await using var tx = await _db.BeginTransactionAsync(cancellationToken: ct);

            var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct)
                       ?? throw new AppException(ErrorCodes.InvalidToken, "Kullanıcı bulunamadı.", statusCode: 401);

            if (user.Status == UserStatus.Banned)
            {
                throw new AppException(ErrorCodes.UserBanned, "Hesap banlı.", statusCode: 403);
            }

            var granted = false;
            if (user.EmailVerifiedAtUtc is null)
            {
                user.EmailVerifiedAtUtc = _clock.UtcNow;
            }

            if (user.Status == UserStatus.PendingVerification)
            {
                user.Status = UserStatus.Active;
            }

            if (user.WelcomeCreditGrantedAtUtc is null)
            {
                await _ledger.GrantWelcomeCreditAsync(user, ct);
                granted = true;
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new VerifyEmailResult(granted);
        }, ct: ct);
    }
}
