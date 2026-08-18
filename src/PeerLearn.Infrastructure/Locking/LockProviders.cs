using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PeerLearn.Application.Abstractions;
using PeerLearn.Application.Common;
using StackExchange.Redis;

namespace PeerLearn.Infrastructure.Locking;

/// <summary>
/// Redis distributed lock: SET key value NX PX ile alınır, sahiplik token'ı karşılaştıran
/// Lua script ile bırakılır (başkasının kilidini yanlışlıkla silmemek için).
/// TTL, sahibin çökmesi durumunda kilidin sonsuza dek kalmasını önler.
/// </summary>
public sealed class RedisLockProvider : IDistributedLockProvider
{
    // TTL, beklenen en kötü transaction süresinin belirgin üstünde olmalı; aşılırsa kilit
    // el değiştirir (release script 0 döner ve uyarı loglanır — gözlemlenebilirlik).
    private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    private const string ReleaseScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        else
            return 0
        end
        """;

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisLockProvider> _logger;

    public RedisLockProvider(IConnectionMultiplexer redis, ILogger<RedisLockProvider> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<IAsyncDisposable> AcquireAsync(string key, TimeSpan timeout, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var token = Guid.NewGuid().ToString("N");
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (await db.StringSetAsync(key, token, LockTtl, When.NotExists))
            {
                return new RedisLockHandle(db, key, token, _logger);
            }

            await Task.Delay(RetryDelay, ct);
        }

        throw new AppException(ErrorCodes.LockTimeout,
            "Sistem yoğun, lütfen birkaç saniye sonra tekrar deneyin.", statusCode: 503);
    }

    private sealed class RedisLockHandle : IAsyncDisposable
    {
        private readonly IDatabase _db;
        private readonly string _key;
        private readonly string _token;
        private readonly ILogger _logger;

        public RedisLockHandle(IDatabase db, string key, string token, ILogger logger)
        {
            _db = db;
            _key = key;
            _token = token;
            _logger = logger;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                var result = await _db.ScriptEvaluateAsync(ReleaseScript, [(RedisKey)_key], [(RedisValue)_token]);

                if ((long)result == 0)
                {
                    // Kilit TTL ile el değiştirmiş: transaction 60 sn'yi aşmış demektir.
                    // Invariantları xmin + DB constraint'leri korur ama bu durum izlenmeli.
                    _logger.LogWarning(
                        "Redis kilidi sahiplik doğrulamadan düşmüş (TTL aşımı olası): {Key}", _key);
                }
            }
            catch (Exception ex)
            {
                // Bırakma başarısızsa TTL zaten temizler; işlemi düşürme.
                _logger.LogWarning(ex, "Redis kilidi bırakılamadı: {Key}", _key);
            }
        }
    }
}

/// <summary>
/// Tek instance geliştirme ortamı için in-process kilit. Birden çok API instance'ı
/// çalıştırılacaksa MUTLAKA Redis kullanılmalıdır (appsettings: ConnectionStrings:Redis).
/// </summary>
public sealed class InProcessLockProvider : IDistributedLockProvider
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();

    public async Task<IAsyncDisposable> AcquireAsync(string key, TimeSpan timeout, CancellationToken ct = default)
    {
        var semaphore = _semaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        if (!await semaphore.WaitAsync(timeout, ct))
        {
            throw new AppException(ErrorCodes.LockTimeout,
                "Sistem yoğun, lütfen birkaç saniye sonra tekrar deneyin.", statusCode: 503);
        }

        return new SemaphoreHandle(semaphore);
    }

    private sealed class SemaphoreHandle : IAsyncDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private int _released;

        public SemaphoreHandle(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public ValueTask DisposeAsync()
        {
            // Çifte Dispose'a karşı koruma.
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _semaphore.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
