using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using PeerLearn.Application.Abstractions;

namespace PeerLearn.Infrastructure.Services;

/// <summary>
/// Kanıt görsellerini yerel diske yazar (MVP). Storage key = "yyyy/MM/guid.ext".
/// Production'da S3/Azure Blob implementasyonu aynı arayüzü uygular; DB yalnızca
/// key sakladığı için geçiş şema değişikliği gerektirmez.
/// </summary>
public sealed class LocalProofStorage : IProofStorage
{
    private readonly string _root;

    public LocalProofStorage(IConfiguration configuration)
    {
        _root = configuration["ProofStorage:RootPath"] ?? "proof-storage";
    }

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var key = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var file = File.Create(fullPath);
        await content.CopyToAsync(file, ct);

        return key;
    }

    public Task<Stream?> OpenAsync(string storageKey, CancellationToken ct = default)
    {
        if (!TamYol(storageKey, out var fullPath) || !File.Exists(fullPath))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        // Kök dışına çıkan anahtar sessizce yok sayılır. Silmede path traversal, okumadan
        // çok daha tehlikeli: bozuk/kötü niyetli tek bir key ile sistem dosyası silinebilirdi.
        if (TamYol(storageKey, out var fullPath))
        {
            // File.Delete yoksa hata vermez — temizliğin idempotent olması tam da bunu gerektiriyor.
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<StoredObject> ListAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var rootFull = Path.GetFullPath(_root);
        if (!Directory.Exists(rootFull))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(rootFull, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            // Anahtar biçimi ileri eğik çizgilidir (S3 uyumu); dosya sistemi ayıracı geri çevrilir.
            var key = Path.GetRelativePath(rootFull, path).Replace(Path.DirectorySeparatorChar, '/');

            yield return new StoredObject(key, File.GetLastWriteTimeUtc(path));
        }

        await Task.CompletedTask; // Yerel diskte gerçek asenkron listeleme yok; sözleşme korunuyor.
    }

    /// <summary>
    /// Anahtarı kök dizin altındaki tam yola çevirir. Kökün DIŞINA çıkıyorsa false döner —
    /// anahtar DB'den gelse bile bu kontrol yapılır (bozuk veri de saldırgan kadar tehlikeli).
    /// </summary>
    private bool TamYol(string storageKey, out string fullPath)
    {
        var rootFull = Path.GetFullPath(_root);
        fullPath = Path.GetFullPath(Path.Combine(rootFull, storageKey.Replace('/', Path.DirectorySeparatorChar)));

        return fullPath.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
