using PeerLearn.Domain.Identity;

namespace PeerLearn.Application.Abstractions;

/// <summary>Test edilebilirlik için sistem saati soyutlaması (Time-Lock kuralı buna bağlı).</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

/// <summary>
/// Dağıtık kilit. Redis implementasyonu (production) veya tek-instance in-process fallback.
/// Aynı cüzdana eşzamanlı yazmaları serileştirir; xmin optimistic concurrency son savunmadır.
/// </summary>
public interface IDistributedLockProvider
{
    /// <summary>Kilit alınamazsa AppException(LockTimeout) fırlatır.</summary>
    Task<IAsyncDisposable> AcquireAsync(string key, TimeSpan timeout, CancellationToken ct = default);
}

/// <summary>
/// Salt okunur sorgu sonuçları için önbellek (Redis; yoksa süreç içi).
///
/// KİLİTTEN FARKI — ve bu fark neden önemli: kilit sağlayıcısında süreç içi yedeğe düşmek
/// TEHLİKELİDİR (çok instance'ta yarış koşulu doğar). Burada değildir: önbellek yalnızca
/// bir hızlandırmadır, kaçırılması ya da bayat kalması en fazla eski bir liste gösterir.
/// Bu yüzden Redis yoksa uyarı vermeden süreç içi belleğe düşer.
///
/// KURAL: doğruluğu önbelleğe BAĞLI hiçbir şey buraya konmaz — bakiye, kilit durumu, yetki.
/// Yalnızca "biraz bayat olabilir" denebilecek keşif/katalog verisi.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Anahtar yoksa <paramref name="factory"/> çalıştırılır, sonucu <paramref name="ttl"/>
    /// süresince saklanır. Önbellek erişilemezse sessizce factory'ye düşer — Redis'in
    /// kapalı olması aramayı ÇALIŞMAZ hâle getirmemeli.
    /// </summary>
    Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct = default) where T : class;

    /// <summary>Önek altındaki tüm anahtarları düşürür (ör. katalog değişince "search:").</summary>
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public interface ITokenService
{
    string CreateAccessToken(User user);

    /// <summary>Tek amaçlı, süreli, imzalı token (e-posta doğrulama linki). DB tablosu gerektirmez.</summary>
    string CreatePurposeToken(Guid userId, string purpose, TimeSpan lifetime);

    /// <summary>Geçersiz/yanlış amaçlı token'da null döner.</summary>
    Guid? ValidatePurposeToken(string token, string purpose);
}

/// <summary>Depodaki tek bir nesne. Temizlik işi bu listeyi DB'deki referanslarla karşılaştırır.</summary>
public readonly record struct StoredObject(string Key, DateTime LastModifiedUtc);

public interface IProofStorage
{
    /// <summary>Dosyayı kalıcı depoya yazar, storage key döner (DB'de yalnızca key saklanır).</summary>
    Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default);

    /// <summary>
    /// Kanıtı okumak için açar. Dosya yoksa null döner (depo ile DB arasında tutarsızlık:
    /// çağıran bunu 404'e çevirir, exception fırlatmaz).
    /// </summary>
    Task<Stream?> OpenAsync(string storageKey, CancellationToken ct = default);

    /// <summary>
    /// Dosyayı siler. Dosya zaten yoksa HATA DEĞİLDİR (temizlik işi idempotent olmalı:
    /// yarıda kalan bir tur, sonraki turda aynı anahtarı yeniden dener).
    /// </summary>
    Task DeleteAsync(string storageKey, CancellationToken ct = default);

    /// <summary>
    /// Depodaki tüm nesneleri akıtır. Yalnızca bakım işi kullanır — istek yolunda ÇAĞRILMAZ.
    /// Akış olarak dönmesinin sebebi, uzak depolarda (S3) listenin sayfalı gelmesi ve
    /// tamamının belleğe alınmasının gerekmemesi.
    /// </summary>
    IAsyncEnumerable<StoredObject> ListAsync(CancellationToken ct = default);
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}
