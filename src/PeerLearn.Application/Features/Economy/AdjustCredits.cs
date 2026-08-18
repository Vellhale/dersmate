using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Economy;
using PeerLearn.Application.Features.Moderation;
using PeerLearn.Application.Options;
using PeerLearn.Domain.Moderation;

namespace PeerLearn.Application.Features.Economy;

/// <summary>
/// Yönetim eliyle puan tanımlama/düzeltme. Pozitif <paramref name="Amount"/> ekler,
/// negatif düşer.
/// </summary>
/// <remarks>
/// NEDEN TEK KOMUT (ekle/düş ayrı değil): ikisi de aynı değişmezleri korumak zorunda
/// (cüzdan = lot toplamı, cüzdan toplamı = defter toplamı) ve iki ayrı komut bu mantığı
/// iki yere kopyalardı. İşaret zaten niyeti taşıyor.
///
/// GEREKÇE ZORUNLU. Denetim izinin tek işe yarar alanı budur: "kim, kime, ne kadar"
/// zaten kayıtta; cevaplanması gereken soru NEDEN. Serbest metin ama boş bırakılamaz.
///
/// TEKİLLİK ANAHTARI ZORUNLU — eski gerekçe yanlıştı ve düzeltildi.
///
/// Burası önce bilinçli olarak idempotent DEĞİLDİ; gerekçe "her düzeltme denetim izine
/// ayrı satır bırakır, yani yanlış görünür ve ters işaretli bir düzeltmeyle geri alınır"
/// idi. Bu, kusuru bir özellik gibi anlatıyordu. Asıl açık çift tıklama değil, HİÇ
/// GÖRÜLMEYEN yol: istek sunucuya ulaşır, defter yazılır, yanıt dönerken bağlantı kopar.
/// Yönetici bir hata görür ve doğal olarak tekrar dener — puan iki kez uygulanır ve
/// kimse ikincisinin fazladan olduğunu bilmez. "Geri alınabilir" olması burada işe
/// yaramaz, çünkü geri alınması gerektiği fark edilmez.
///
/// Diğer gerekçe ("istemci anahtar üretmiyor, o yüzden alan korumadığı bir şeyi
/// koruyormuş gibi görünür") doğruydu ama çözümü alanı EKLEMEMEK değil, istemciye anahtar
/// ÜRETTİRMEK. Panel artık her yeni düzeltme için bir anahtar üretiyor ve yalnızca işlem
/// başarılı olduğunda yenisine geçiyor; yani tekrar denemeler aynı anahtarla gidiyor.
/// Anahtar zorunlu: isteğe bağlı olsaydı koruma yalnızca onu göndermeyi hatırlayanlar
/// için çalışırdı ki bu tam olarak "koruyormuş gibi görünen alan"ın kendisi olurdu.
/// </remarks>
public sealed record AdjustCreditsCommand(
    Guid TargetUserId,
    Guid AdminUserId,
    int Amount,
    string Reason,
    string IdempotencyKey) : IRequest<AdjustCreditsResult>;

/// <param name="Replayed">
/// true ise bu istek YENİ bir düzeltme uygulamadı; aynı anahtarla daha önce uygulanmış
/// olanın sonucu döndürüldü. Panelin "uygulandı" ile "zaten uygulanmıştı" arasındaki farkı
/// söyleyebilmesi için gerekli — ikisini aynı göstermek yöneticiyi ikinci kez denemeye
/// iter, ki kaçınmaya çalıştığımız davranış tam olarak budur.
/// </param>
public sealed record AdjustCreditsResult(
    Guid TargetUserId, int Amount, int NewAvailableBalance, bool Replayed);

public sealed class AdjustCreditsHandler : IRequestHandler<AdjustCreditsCommand, AdjustCreditsResult>
{
    /// <summary>
    /// Tek işlemde değiştirilebilecek en büyük tutar. Yazım hatası freni: 100 yerine
    /// 100000 yazmak, defteri tek tuşla anlamsız hâle getirebilirdi. Daha büyüğü gerekiyorsa
    /// birden fazla kayıt açılır — ki bu da denetim izinde daha okunur bir geçmiş bırakır.
    /// </summary>
    private const int MaxAbsoluteAmount = 10_000;

    private const int MinReasonLength = 10;

    /// <summary>Sütun genişliğiyle aynı (AdminActionLog.IdempotencyKey). UUID 36 karakter.</summary>
    private const int MaxKeyLength = 64;

    private static readonly JsonSerializerOptions MetaOptions =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>Denetim izindeki metadata'nın tekrar oynatma için gereken alanları.</summary>
    private sealed record AdjustmentMetadata(int Amount, int NewAvailableBalance, string Reason);

    private readonly IAppDbContext _db;
    private readonly CreditLedgerService _ledger;
    private readonly IDistributedLockProvider _locks;
    private readonly EconomyOptions _economy;

    public AdjustCreditsHandler(IAppDbContext db, CreditLedgerService ledger,
        IDistributedLockProvider locks, IOptions<EconomyOptions> economy)
    {
        _db = db;
        _ledger = ledger;
        _locks = locks;
        _economy = economy.Value;
    }

    public async Task<AdjustCreditsResult> Handle(AdjustCreditsCommand request, CancellationToken ct)
    {
        var key = (request.IdempotencyKey ?? string.Empty).Trim();
        if (key.Length == 0)
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                "Idempotency-Key başlığı zorunludur.");
        }

        if (key.Length > MaxKeyLength)
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                $"Idempotency-Key en fazla {MaxKeyLength} karakter olabilir.");
        }

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length < MinReasonLength)
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                $"Gerekçe zorunludur (en az {MinReasonLength} karakter).");
        }

        if (request.Amount == 0)
        {
            throw new AppException(ErrorCodes.ValidationFailed, "Düzeltme tutarı sıfır olamaz.");
        }

        if (Math.Abs(request.Amount) > MaxAbsoluteAmount)
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                $"Tek işlemde en fazla {MaxAbsoluteAmount} puan değiştirilebilir.");
        }

        /*
          TEKRAR KONTROLÜ EN BAŞTA — kilitten de, kullanıcı aramasından da önce.

          Tekrar oynatma bu ucun en sık yürüyeceği ikinci yol (ilki: yeni düzeltme) ve
          cüzdana hiç dokunmadan cevaplanabilir. Kilidin arkasına konsaydı, ağ hatası
          sonrası art arda gelen denemeler gereksizce sıraya girerdi.
        */
        var tekrar = await TekrarBulAsync(request, key, reason, ct);
        if (tekrar is not null)
        {
            return tekrar;
        }

        var hedef = await _db.Users.AsNoTracking()
                        .Where(u => u.Id == request.TargetUserId)
                        .Select(u => new { u.Id, u.DisplayName })
                        .SingleOrDefaultAsync(ct)
                    ?? throw new AppException(ErrorCodes.UserNotFound, "Kullanıcı bulunamadı.", statusCode: 404);

        var actorRole = await _db.Users.AsNoTracking()
            .Where(u => u.Id == request.AdminUserId)
            .Select(u => u.Role)
            .SingleAsync(ct);

        /*
          Cüzdan kilidi + tek transaction: defter servisi sözleşmesi bunu şart koşuyor.
          Aynı cüzdana eşzamanlı bir ders basımı gelirse düzeltme onunla serileşir; xmin
          çakışmasında ConcurrencyRetry baştan dener.
        */
        var walletKey = LockKeys.Wallet(request.TargetUserId);
        await using var walletLock = await _locks.AcquireAsync(
            walletKey, TimeSpan.FromSeconds(_economy.LockTimeoutSeconds), ct);

        try
        {
            return await ConcurrencyRetry.RunAsync(_db, async () =>
            {
                await using var tx = await _db.BeginTransactionAsync(cancellationToken: ct);

                // Cüzdan yoksa açılır: e-posta doğrulamamış bir kullanıcıya düzeltme yapmak
                // meşru bir senaryo (yanlış hesaba basılmış puanın geri alınması gibi).
                var wallet = await _ledger.EnsureWalletAsync(request.TargetUserId, ct);

                var yeniBakiye = await _ledger.AdminAdjustAsync(wallet, request.Amount, ct);

                AdminAudit.Record(
                    _db,
                    request.AdminUserId,
                    actorRole,
                    AdminActionType.CreditAdjusted,
                    targetType: "User",
                    targetId: hedef.Id,
                    summary: $"{(request.Amount > 0 ? "+" : "")}{request.Amount} puan — {reason}",
                    metadata: new
                    {
                        amount = request.Amount,
                        newAvailableBalance = yeniBakiye,
                        reason
                    },
                    idempotencyKey: key);

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return new AdjustCreditsResult(hedef.Id, request.Amount, yeniBakiye, Replayed: false);
            }, ct: ct);
        }
        catch (DbUpdateException)
        {
            /*
              AYNI ANAHTARLA EŞZAMANLI İKİNCİ İSTEK — kısmi unique index yakaladı.

              Kaybeden taraf hata döndürmemeli: çağıran açısından ikisi TEK bir istekti,
              ve o istek başarılı oldu. Kazananın sonucunu döndürüyoruz.

              SQL hata kodunu (23505) ayıklamıyoruz — Application katmanı Npgsql'e bağlı
              değil, ama daha önemlisi doğru soru "hangi kısıt patladı" değil "bizim
              anahtarımız yazıldı mı". Yazılmadıysa hata bizim değildir ve olduğu gibi
              yukarı çıkmalı: yutulursa gerçek bir veri hatası "başarılı" görünür.
            */
            _db.ClearChangeTracker();

            var kazananinSonucu = await TekrarBulAsync(request, key, reason, ct);
            if (kazananinSonucu is null) throw;
            return kazananinSonucu;
        }
    }

    /// <summary>
    /// Bu anahtarla daha önce yazılmış bir düzeltme varsa sonucunu döndürür; yoksa null.
    /// Anahtar var ama YÜK FARKLIYSA fırlatır.
    /// </summary>
    /// <remarks>
    /// FARKLI YÜKTE NEDEN SESSİZCE İLK SONUÇ DÖNDÜRÜLMÜYOR:
    /// yönetici tutarı 100'den 200'e düzeltip tekrar gönderdiyse, tekrar oynatma 100'ü
    /// uygular ve "uygulandı" der. Hata, kullanıcının düzeltmeye çalıştığı şeyi gizlerdi.
    /// Gerekçe de karşılaştırmaya dahil: gerekçe denetim izinin tek işe yarar alanı,
    /// "düzelttim ama kaydolmamış" durumu sessiz kalmamalı.
    /// </remarks>
    private async Task<AdjustCreditsResult?> TekrarBulAsync(
        AdjustCreditsCommand request, string key, string reason, CancellationToken ct)
    {
        var kayit = await _db.AdminActionLogs.AsNoTracking()
            .Where(l => l.ActorUserId == request.AdminUserId && l.IdempotencyKey == key)
            .Select(l => new { l.Action, l.TargetId, l.MetadataJson })
            .SingleOrDefaultAsync(ct);

        if (kayit is null)
        {
            return null;
        }

        AdjustmentMetadata? meta = null;
        if (kayit.Action == AdminActionType.CreditAdjusted && kayit.MetadataJson is not null)
        {
            try
            {
                meta = JsonSerializer.Deserialize<AdjustmentMetadata>(kayit.MetadataJson, MetaOptions);
            }
            catch (JsonException)
            {
                meta = null;   // Bozuk metadata: aşağıda "aynı istek değil" sayılır.
            }
        }

        var ayniIstek = meta is not null
                        && kayit.TargetId == request.TargetUserId
                        && meta.Amount == request.Amount
                        && string.Equals(meta.Reason, reason, StringComparison.Ordinal);

        if (!ayniIstek)
        {
            throw new AppException(ErrorCodes.IdempotencyKeyReused,
                "Bu Idempotency-Key farklı bir işlem için kullanılmış. Değişiklik uygulanmadı; " +
                "yeni bir anahtarla tekrar gönderin.",
                statusCode: 409);
        }

        return new AdjustCreditsResult(
            request.TargetUserId, request.Amount, meta!.NewAvailableBalance, Replayed: true);
    }
}
