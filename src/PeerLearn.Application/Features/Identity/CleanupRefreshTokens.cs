using MediatR;
using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;

namespace PeerLearn.Application.Features.Identity;

/// <param name="Silinen">Bu turda silinen satır sayısı.</param>
public sealed record CleanupRefreshTokensResult(int Silinen);

public sealed record CleanupRefreshTokensCommand : IRequest<CleanupRefreshTokensResult>;

/// <summary>
/// Artık işe yaramayan yenileme token'ı satırlarını siler.
/// </summary>
/// <remarks>
/// ⚠️ BU İŞ OLMASAYDI TABLO SINIRSIZ BÜYÜRDÜ VE HİÇBİR YERDE HATA VERMEZDİ. Her giriş
/// bir satır, her yenileme bir satır daha ekliyor; hiçbiri kendiliğinden gitmiyor.
/// Belirti de gecikmeli ve yanıltıcı olurdu: önce yenileme sorgusu yavaşlar, sonra
/// disk dolar, ve sebep "kimlik doğrulama yavaşladı" gibi görünür.
///
/// ─── NE SİLİNİYOR, NE BIRAKILIYOR ────────────────────────────────────────────
/// Silinen: süresi dolmuş VE üstünden saklama penceresi geçmiş satırlar.
///
/// ⛔ İPTAL EDİLMİŞ AMA SÜRESİ DOLMAMIŞ SATIRLAR SİLİNMEZ — bu kritik. Yeniden kullanım
/// tespiti tam olarak "iptal edilmiş bir token yeniden sunuldu mu" sorusuna dayanıyor;
/// satır silinirse o token "hiç var olmamış" gibi görünür ve hırsızlık sessizce sıradan
/// bir "geçersiz token" hatasına dönüşür. Yani erken temizlik, bir güvenlik özelliğini
/// fark edilmeden kapatırdı.
///
/// <see cref="SaklamaGunu"/> kadar bekleniyor ki dönüşüm zinciri bir süre daha
/// incelenebilsin (bir hırsızlık şüphesinde "bu token ne zaman, hangi cihazdan
/// üretilmişti" sorusunun yanıtı bu satırlarda).
/// </remarks>
public sealed class CleanupRefreshTokensHandler
    : IRequestHandler<CleanupRefreshTokensCommand, CleanupRefreshTokensResult>
{
    /// <summary>
    /// Süresi dolmuş bir satırın silinmeden önce bekletildiği süre. Teşhis penceresi:
    /// bu süre boyunca zincir geriye doğru izlenebilir.
    /// </summary>
    public const int SaklamaGunu = 30;

    private readonly IAppDbContext _db;
    private readonly IClock _clock;

    public CleanupRefreshTokensHandler(IAppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<CleanupRefreshTokensResult> Handle(
        CleanupRefreshTokensCommand request,
        CancellationToken ct)
    {
        var esik = _clock.UtcNow.AddDays(-SaklamaGunu);

        /* Koşul SÜREYE bakıyor, iptal durumuna DEĞİL. İptal edilmiş ama süresi dolmamış
           satır kasten kapsam dışı — gerekçesi sınıf açıklamasında. */
        var silinen = await _db.RefreshTokens
            .Where(t => t.ExpiresAtUtc < esik)
            .ExecuteDeleteAsync(ct);

        return new CleanupRefreshTokensResult(silinen);
    }
}
