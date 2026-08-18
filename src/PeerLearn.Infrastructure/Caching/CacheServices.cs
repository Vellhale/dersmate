using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PeerLearn.Application.Abstractions;
using StackExchange.Redis;

namespace PeerLearn.Infrastructure.Caching;

/// <summary>
/// Redis tabanlı önbellek. Değerler JSON olarak saklanır (insan tarafından okunabilir,
/// redis-cli ile hata ayıklanabilir; .NET'e özel binary biçim başka dillerden okunamazdı).
///
/// HATA POLİTİKASI: Redis'e yapılan HER çağrı yutulur ve factory'ye düşülür. Önbellek bir
/// hızlandırmadır; Redis'in kapalı olması arama uçlarını 500 döndürür hâle getirmemeli.
/// Kilit sağlayıcısında bunun tam tersi geçerlidir (orada hata YUTULMAZ) — çünkü orada
/// sessiz düşüş yarış koşulu demektir.
/// </summary>
public sealed class RedisCacheService : ICacheService
{
    private const string KeyPrefix = "peerlearn:cache:";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct = default) where T : class
    {
        var fullKey = KeyPrefix + key;

        try
        {
            var cached = await _redis.GetDatabase().StringGetAsync(fullKey);
            if (cached.HasValue)
            {
                var value = JsonSerializer.Deserialize<T>(cached!);
                if (value is not null)
                {
                    return value;
                }
            }
        }
        catch (Exception ex) when (ex is RedisException or JsonException)
        {
            // Bozuk kayıt veya erişilemeyen Redis: hesaplayıp devam et.
            _logger.LogWarning(ex, "Önbellek okunamadı ({Key}) — kaynaktan hesaplanıyor.", key);
        }

        var fresh = await factory(ct);

        try
        {
            await _redis.GetDatabase().StringSetAsync(fullKey, JsonSerializer.Serialize(fresh), ttl);
        }
        catch (Exception ex) when (ex is RedisException or JsonException)
        {
            _logger.LogWarning(ex, "Önbelleğe yazılamadı ({Key}).", key);
        }

        return fresh;
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        var pattern = KeyPrefix + prefix + "*";

        try
        {
            // Sunucu başına tarama: KEYS değil SCAN kullanılır (KEYS tüm Redis'i bloklar).
            foreach (var endpoint in _redis.GetEndPoints())
            {
                var server = _redis.GetServer(endpoint);
                if (!server.IsConnected || server.IsReplica)
                {
                    continue;
                }

                foreach (var key in server.Keys(pattern: pattern, pageSize: 250))
                {
                    await _redis.GetDatabase().KeyDeleteAsync(key);
                }
            }
        }
        catch (RedisException ex)
        {
            // Düşürülemeyen önbellek en fazla TTL kadar bayat kalır — akış durdurulmaz.
            _logger.LogWarning(ex, "Önbellek öneki düşürülemedi ({Prefix}).", prefix);
        }
    }
}

/// <summary>
/// Redis yapılandırılmadığında kullanılan süreç içi önbellek.
///
/// ÇOK INSTANCE UYARISI: her instance kendi kopyasını tutar, bu yüzden bir instance'ta
/// düşürülen anahtar diğerlerinde TTL dolana kadar bayat kalır. Kabul edilebilir olmasının
/// sebebi buraya yalnızca doğruluğu kritik olmayan verinin konması (bkz. ICacheService).
/// Kilit sağlayıcısının aksine bu düşüş uyarı loglamaz — sessiz kalması güvenli.
/// </summary>
public sealed class InMemoryCacheService : ICacheService
{
    private readonly ConcurrentDictionary<string, (object Value, DateTime ExpiresAtUtc)> _entries = new();

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct = default) where T : class
    {
        var now = DateTime.UtcNow;

        if (_entries.TryGetValue(key, out var entry) && entry.ExpiresAtUtc > now && entry.Value is T typed)
        {
            return typed;
        }

        var fresh = await factory(ct);
        _entries[key] = (fresh, now.Add(ttl));

        // Süresi dolmuş kayıtlar birikmesin (arka plan süpürücü kurmaya değmeyecek boyut).
        if (_entries.Count > 512)
        {
            foreach (var stale in _entries.Where(e => e.Value.ExpiresAtUtc <= now).Select(e => e.Key))
            {
                _entries.TryRemove(stale, out _);
            }
        }

        return fresh;
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        foreach (var key in _entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
        {
            _entries.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }
}
