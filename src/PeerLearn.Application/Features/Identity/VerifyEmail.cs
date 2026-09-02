using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Economy;
using PeerLearn.Application.Options;
using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Features.Identity;

/// <param name="Email">
/// Kodun sahibi. KOD TEK BAŞINA YETMEZ: altı hane kullanıcıya özgü değil, aynı anda
/// yüzlerce hesapta aynı kod olabilir. E-posta olmadan uç, "bu kodu kimin için
/// deniyorsun" sorusunu cevaplayamaz ve rastgele kod deneyen biri er ya da geç
/// BİRİNİN hesabını doğrulardı.
/// </param>
public sealed record VerifyEmailCommand(string Email, string Code) : IRequest<VerifyEmailResult>;

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
        var eposta = (request.Email ?? string.Empty).Trim();
        var kod = (request.Code ?? string.Empty).Trim();

        /*
          KULLANICI KİLİT DIŞINDA OKUNUYOR — yalnızca id'sini öğrenmek için. Cüzdan
          kilidi kullanıcı bazında ve anahtarı almak için id gerekiyor; kilidin içinde
          yeniden okunuyor (aşağıda), yani buradaki okuma bir karar dayanağı değil.
        */
        var userId = await _db.Users.AsNoTracking()
            .Where(u => u.Email == eposta)
            .Select(u => (Guid?)u.Id)
            .SingleOrDefaultAsync(ct);

        /*
          ⚠️ KULLANICI YOKSA DA AYNI HATA. "Bu e-posta kayıtlı değil" demek, kayıt
          ucundaki varlık sızıntısı korumasını (resend-verification'daki kararın aynısı)
          arka kapıdan delerdi: saldırgan rastgele adreslerle deneyip hangilerinin
          kayıtlı olduğunu öğrenirdi.
        */
        if (userId is null)
        {
            throw new AppException(ErrorCodes.InvalidToken,
                "Kod geçersiz ya da süresi dolmuş. Yeni kod isteyebilirsin.", statusCode: 401);
        }

        await using var walletLock = await _locks.AcquireAsync(
            LockKeys.Wallet(userId.Value), TimeSpan.FromSeconds(_economy.LockTimeoutSeconds), ct);

        return await ConcurrencyRetry.RunAsync(_db, async () =>
        {
            await using var tx = await _db.BeginTransactionAsync(cancellationToken: ct);

            var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct)
                       ?? throw new AppException(ErrorCodes.InvalidToken, "Kullanıcı bulunamadı.", statusCode: 401);

            if (user.Status == UserStatus.Banned)
            {
                throw new AppException(ErrorCodes.UserBanned, "Hesap banlı.", statusCode: 403);
            }

            var simdi = _clock.UtcNow;

            /*
              ─── KOD DENETİMİ ────────────────────────────────────────────────────

              ZATEN DOĞRULANMIŞSA kod istenmiyor: kullanıcı "doğrula" ekranına ikinci
              kez düşerse (geri tuşu, iki sekme) hata görmesi anlamsız — istenen sonuç
              zaten gerçekleşmiş durumda. İşlem sessizce başarılı sayılıyor.
            */
            if (user.EmailVerifiedAtUtc is null)
            {
                if (user.EmailVerificationCodeHash is null ||
                    user.EmailVerificationCodeExpiresAtUtc is null ||
                    user.EmailVerificationCodeExpiresAtUtc <= simdi)
                {
                    throw new AppException(ErrorCodes.InvalidToken,
                        "Kod geçersiz ya da süresi dolmuş. Yeni kod isteyebilirsin.", statusCode: 401);
                }

                /*
                  DENEME SAYACI KODUN TEK GERÇEK KORUMASI. Altı hane = 1.000.000
                  olasılık; hız sınırı IP başına olduğu için saldırgan IP değiştirerek
                  onu aşabilir. Sayaç HESAP bazında, yani IP değiştirmek işe yaramıyor.
                */
                if (user.EmailVerificationAttempts >= EmailVerificationRules.MaxAttempts)
                {
                    throw new AppException(ErrorCodes.InvalidToken,
                        "Çok fazla yanlış deneme yapıldı. Yeni kod iste.", statusCode: 429);
                }

                var beklenen = EmailVerificationRules.HashCode(user.Id, kod);
                if (!EmailVerificationRules.HashesMatch(user.EmailVerificationCodeHash, beklenen))
                {
                    /*
                      YANLIŞ DENEME SAYILIYOR VE KAYDEDİLİYOR. SaveChanges burada
                      şart: hata fırlatınca transaction geri alınıyor, yani sayaç
                      artışı da kaybolurdu ve sınırsız deneme mümkün olurdu — sayaç
                      var ama işlevsiz olurdu, en kötü tür hata.

                      Ayrı bir transaction'da yazılıyor (aşağıdaki commit'e
                      ulaşılamıyor); doğrulama başarısız olduğu için başka bir
                      değişiklik de yok.
                    */
                    user.EmailVerificationAttempts++;
                    await _db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);

                    throw new AppException(ErrorCodes.InvalidToken,
                        "Kod hatalı. Kalan deneme: " +
                        (EmailVerificationRules.MaxAttempts - user.EmailVerificationAttempts),
                        statusCode: 401);
                }

                user.EmailVerifiedAtUtc = simdi;

                // Kullanılmış kod satırda durmamalı: faydası yok, riski var.
                user.EmailVerificationCodeHash = null;
                user.EmailVerificationCodeExpiresAtUtc = null;
                user.EmailVerificationAttempts = 0;
            }

            var granted = false;

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
