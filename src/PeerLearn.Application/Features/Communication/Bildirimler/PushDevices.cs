using System.Data.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using PeerLearn.Application.Identity;
using PeerLearn.Domain.Communication;

namespace PeerLearn.Application.Features.Communication.Bildirimler;

// ---------------------------------------------------------------------------
// PUT /api/v1/push/devices — cihaz kaydı
// ---------------------------------------------------------------------------

/// <param name="Token">ExponentPushToken[…].</param>
/// <param name="Platform">Yük platforma göre kuruluyor; eksikse 400 (varsayılana düşmesin).</param>
/// <param name="HwidHash">Girişteki ve yenilemedeki ile AYNI cihaz özeti; oturum bağı buna dayanıyor.</param>
/// <param name="KapaliKanallar">Android'de telefon ayarlarından kapatılmış kanal kimlikleri.</param>
public sealed record PushCihaziKaydetCommand(
    Guid UserId,
    string? Token,
    PushPlatform? Platform,
    string? HwidHash,
    IReadOnlyList<string>? KapaliKanallar) : IRequest<PushCihaziKaydiSonucu>;

/// <param name="Kayitli">
/// false: hiçbir şey YAZILMADI (aydınlatma görülmemiş ya da bu cihazın oturumu kapalı).
/// Hata değil; mobil sessizce devam eder, bir sonraki öne gelişte yeniden dener.
/// </param>
/// <param name="Alici">
/// BildirimEtiketi.Alici: bildirim verisindeki "alici" ile aynı değer. Mobil, telefondaki
/// oturumun etiketiyle uyuşmayan bildirimi yok sayar. Hesap kimliğini açığa vermez.
/// </param>
public sealed record PushCihaziKaydiSonucu(bool Kayitli, string Alici);

/// <summary>
/// Bu cihazın push token'ını kullanıcıya bağlar. İdempotent: mobil her açılışta ve her öne
/// gelişte çağırıyor.
/// </summary>
/// <remarks>
/// ─── HİÇBİR ŞEY YAZMAYAN İKİ DURUM (kayitli:false) ──────────────────────────
/// (a) Aydınlatma damgası yok (NotificationPreferences.DisclosureShownAtUtc). Kullanıcı
///     aydınlatma ekranında "Aç/Devam" demeden token sunucuya kaydedilmez. Bu KVKK
///     güvencesi istemci koduna bırakılmadı: Android 7-12'de bildirim izni varsayılan açık,
///     yani "izin verildi mi" sorusu aydınlatmanın yapıldığını göstermez.
/// (b) Cihaz bağlı değil (OturumBagi: bu (UserId, HwidHash) için EN YENİ yenileme token'ı
///     aktif değil). Çıkış yapmış ama elinde hâlâ geçerli erişim token'ı olan bir istemci
///     (≤2 saat) cihazı yeniden bağlayamasın. "Herhangi bir aktif token" yetmezdi: giriş
///     aynı cihazın eski token'larını iptal etmiyor, yeniden kurulumlar 60 gün yaşayan
///     artıklar bırakıyor.
///
/// ─── YAZIM: KİLİT → SİL → UPSERT, TEK KISA TRANSACTION ─────────────────────
/// İki tekil index var: Token ve (UserId, HwidHash). "Oku, sonra yaz" biçimindeki bir
/// kayıt, aynı cihazdan art arda gelen iki PUT'ta (açılıştaki kayıt eski token'la uçuştayken
/// token dinleyicisinin yenisiyle attığı istek) çakışır; çakışmayı yutmak cihazı ESKİ
/// token'la bırakırdı ve DeviceNotRegistered gelince bir sonraki soğuk açılışa kadar hiç
/// bildirim gelmezdi. Bu yüzden:
/// <list type="number">
/// <item><c>pg_advisory_xact_lock</c>: aynı (kullanıcı, cihaz) için yazımlar sıraya girer;
/// son gelen kazanır.</item>
/// <item>Aynı (kullanıcı, cihaz) için BAŞKA token taşıyan satır silinir (yeniden kurulum
/// token'ı değiştirir, HWID aynı kalır).</item>
/// <item><c>INSERT … ON CONFLICT ("Token") DO UPDATE</c>: token başka hesaptaysa satır bu
/// hesaba TAŞINIR — aynı telefonda hesap değişince eski hesabın bildirimleri o telefona
/// gitmeye devam etmesin.</item>
/// </list>
/// Çakışma YUTULMAZ: tekil index ihlali ya da kilitlenme (deadlock) olursa bütün yazım bir
/// kez yeniden denenir; ikincisi de düşerse hata istemciye gider (mobil sonra yeniden dener).
///
/// ─── KAPALI KANALLAR ────────────────────────────────────────────────────────
/// Bilinmeyen kanal kimliği REDDEDİLMİYOR, süzülüyor: mobil ileride beşinci bir kanal
/// eklerse sunucu onu tanıyana kadar kayıt tamamen düşmesin (kayıt düşerse HİÇBİR bildirim
/// gelmez; tanınmayan kanalın yok sayılması ise yalnızca o kanalın tercihini kaybettirir).
/// iOS'ta kanal yok; gelen liste yok sayılır, yanlış bir istemci iPhone'u sessizce
/// susturamasın.
/// </remarks>
public sealed class PushCihaziKaydetHandler : IRequestHandler<PushCihaziKaydetCommand, PushCihaziKaydiSonucu>
{
    /// <summary>Çakışmada toplam deneme: ilki + bir yeniden deneme.</summary>
    private const int EnFazlaDeneme = 2;

    private readonly IAppDbContext _db;
    private readonly IClock _clock;
    private readonly BildirimEtiketi _etiket;
    private readonly ILogger<PushCihaziKaydetHandler> _logger;

    public PushCihaziKaydetHandler(
        IAppDbContext db, IClock clock, BildirimEtiketi etiket, ILogger<PushCihaziKaydetHandler> logger)
    {
        _db = db;
        _clock = clock;
        _etiket = etiket;
        _logger = logger;
    }

    public async Task<PushCihaziKaydiSonucu> Handle(PushCihaziKaydetCommand request, CancellationToken ct)
    {
        var token = request.Token?.Trim();
        if (!PushTokenKurali.Gecerli(token))
        {
            throw new AppException(ErrorCodes.ValidationFailed, "Bildirim anahtarı geçersiz.");
        }

        if (request.Platform is not { } platform)
        {
            throw new AppException(ErrorCodes.ValidationFailed, "Platform zorunludur (Android ya da Ios).");
        }

        /* Normalizasyon giriş ve yenilemeyle BİREBİR aynı olmak zorunda: oturum bağı
           PushDevices.HwidHash ile RefreshTokens.DeviceHwidHash'in EŞİTLİĞİNE dayanıyor.
           Farklı biçimlenseydi hiçbir cihaz "bağlı" sayılmaz, bildirimler hatasız dururdu. */
        var hwid = HwidKurali.Normalize(request.HwidHash)
                   ?? throw new AppException(ErrorCodes.HwidRequired, "Cihaz kimliği (HWID) zorunludur.");

        string[] kanallar = platform == PushPlatform.Ios ? [] : KapaliKanallariSuz(request.KapaliKanallar);
        var alici = _etiket.Alici(request.UserId);
        var now = _clock.UtcNow;

        // (a) Aydınlatma görülmeden kayıt yok.
        var aydinlatma = await _db.NotificationPreferences.AsNoTracking()
            .Where(p => p.UserId == request.UserId)
            .Select(p => p.DisclosureShownAtUtc)
            .FirstOrDefaultAsync(ct);

        if (aydinlatma is null)
        {
            return new PushCihaziKaydiSonucu(false, alici);
        }

        // (b) Bu cihazın en yeni oturumu aktif değilse kayıt yok. Kural tek yerde: OturumBagi.
        if (!await OturumBagi.BagliMiAsync(_db.RefreshTokens, request.UserId, hwid, now, ct))
        {
            return new PushCihaziKaydiSonucu(false, alici);
        }

        for (var deneme = 1; ; deneme++)
        {
            try
            {
                await YazAsync(request.UserId, token!, platform, hwid, kanallar, now, ct);
                break;
            }
            catch (DbException ex) when (deneme < EnFazlaDeneme && YenidenDenenebilir(ex))
            {
                _logger.LogWarning(
                    "Push cihaz kaydı çakıştı ({SqlState}), yeniden deneniyor. Kullanıcı {UserId}, token {Token}.",
                    ex.SqlState, request.UserId, PushTokenKurali.Maskele(token));
            }
        }

        return new PushCihaziKaydiSonucu(true, alici);
    }

    private async Task YazAsync(
        Guid userId, string token, PushPlatform platform, string hwid, string[] kanallar, DateTime now, CancellationToken ct)
    {
        await using var tx = await _db.BeginTransactionAsync(cancellationToken: ct);

        /* Kilit anahtarı (kullanıcı, cihaz). Önek, başka advisory kilit kullanımlarıyla
           (ör. test bildirimi sayacı) aynı anahtar uzayına düşmesin diye. Transaction
           kilidi: commit ya da rollback'te kendiliğinden bırakılır, sızamaz. */
        var kilit = $"push-cihaz:{userId:D}:{hwid}";
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({kilit}, 0))", ct);

        // Yalnızca günlük için: token başka bir hesaptan mı taşınıyor?
        var oncekiSahip = await _db.PushDevices.AsNoTracking()
            .Where(d => d.Token == token)
            .Select(d => (Guid?)d.UserId)
            .FirstOrDefaultAsync(ct);

        // Aynı (kullanıcı, cihaz) için eski token'lı satır: yeniden kurulumda token değişir.
        await _db.PushDevices
            .Where(d => d.UserId == userId && d.HwidHash == hwid && d.Token != token)
            .ExecuteDeleteAsync(ct);

        /* ON CONFLICT hedefi Token. (UserId, HwidHash) çakışması bir önceki DELETE ve kilit
           yüzünden oluşamaz; oluşursa (beklenmeyen bir yarış) 23505 fırlar ve üstte yeniden
           denenir. CreatedAtUtc taşımada korunur: satırın ilk kaydı, son görülme ayrı alan. */
        var platformMetni = platform.ToString();
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO comms."PushDevices"
                ("Id", "CreatedAtUtc", "UserId", "Token", "Platform", "HwidHash", "KapaliKanallar", "LastSeenAtUtc")
            VALUES ({Guid.NewGuid()}, {now}, {userId}, {token}, {platformMetni}, {hwid}, {kanallar}, {now})
            ON CONFLICT ("Token") DO UPDATE SET
                "UserId" = EXCLUDED."UserId",
                "HwidHash" = EXCLUDED."HwidHash",
                "Platform" = EXCLUDED."Platform",
                "KapaliKanallar" = EXCLUDED."KapaliKanallar",
                "LastSeenAtUtc" = EXCLUDED."LastSeenAtUtc"
            """, ct);

        await tx.CommitAsync(ct);

        if (oncekiSahip is { } eski && eski != userId)
        {
            /* Aynı telefonda hesap değişimi: beklenen bir olay, ama teşhiste "neden A'ya
               bildirim gitmiyor" sorusunun cevabı bu satır. Token asla tam yazılmaz. */
            _logger.LogInformation(
                "Push token'ı başka hesaba taşındı: {EskiKullanici} → {YeniKullanici}, token {Token}.",
                eski, userId, PushTokenKurali.Maskele(token));
        }
    }

    /// <summary>Yalnızca bilinen kanallar, tekrarsız, sıralı (satır karşılaştırılabilir kalsın).</summary>
    private static string[] KapaliKanallariSuz(IReadOnlyList<string>? gelen)
        => gelen is null
            ? []
            : gelen
                .Select(k => k?.Trim())
                .Where(BildirimKanallari.Bilinen)
                .Select(k => k!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToArray();

    /// <summary>
    /// 23505 tekil index ihlali, 40P01 kilitlenme (deadlock), 40001 serileştirme hatası.
    /// Üçü de "aynı anda başka bir yazım vardı" demek; yeniden deneme doğru sonucu verir.
    /// </summary>
    private static bool YenidenDenenebilir(DbException ex)
        => ex.SqlState is "23505" or "40P01" or "40001";
}

// ---------------------------------------------------------------------------
// POST /api/v1/push/devices/forget — çevrimdışı çıkıştan sonra
// ---------------------------------------------------------------------------

public sealed record PushCihaziUnutCommand(string? Token) : IRequest;

/// <summary>
/// Token'ı taşıyan cihaz satırını siler. KİMLİKSİZ çalışır ve her durumda başarılı döner.
/// </summary>
/// <remarks>
/// ─── NEDEN VAR ──────────────────────────────────────────────────────────────
/// Çıkış sunucuya ulaşamazsa (uçak modu, ağ yok) cihaz satırı ve oturum sunucuda kalır;
/// en yeni token 60 gün geçerli olduğu için oturum bağı da tutar ve çıkış yapılmış telefona
/// bildirim gitmeye devam ederdi. Mobil bu durumda cihaza bir "unutulacak" işareti yazıyor
/// ve oturumsuz ilk açılışta bu ucu çağırıyor.
///
/// ─── NEDEN KİMLİKSİZ, NEDEN ZARARSIZ ────────────────────────────────────────
/// Çağrı anında oturum yok (çıkış yapılmış). Token'ı bilen biri yalnızca O cihazın
/// bildirimini kesebilir; Expo token'ı rastgele ve tahmin edilemez. Yanıt varlığı ele
/// vermez: bulunsa da bulunmasa da aynı 204.
///
/// ⚠️ Yalnızca push satırını siler; oturumu (yenileme token'ı) İPTAL ETMEZ — token'ın
/// sahibi kanıtlanmadı. Oturumun kendisi ya çıkışın sonraki denemesiyle ya da 60 günlük
/// ömrüyle kapanır; o süre boyunca da bu silme yüzünden bildirim gitmez.
/// </remarks>
public sealed class PushCihaziUnutHandler : IRequestHandler<PushCihaziUnutCommand>
{
    private readonly IAppDbContext _db;

    public PushCihaziUnutHandler(IAppDbContext db) => _db = db;

    public async Task Handle(PushCihaziUnutCommand request, CancellationToken ct)
    {
        var token = request.Token?.Trim();

        // Biçimi tutmayan değerle veritabanına gitmeye gerek yok; yanıt yine aynı (204).
        if (!PushTokenKurali.Gecerli(token))
        {
            return;
        }

        // ExecuteDelete: satır yoksa 0 satır, hata değil. İzlenen silme eşzamanlı bir
        // silmede DbUpdateConcurrencyException fırlatırdı.
        await _db.PushDevices.Where(d => d.Token == token).ExecuteDeleteAsync(ct);
    }
}
