using Microsoft.EntityFrameworkCore;
using PeerLearn.Application.Abstractions;

namespace PeerLearn.Application.Common;

/// <summary>
/// xmin (optimistic concurrency) çakışmasında işlemi baştan dener.
/// KRİTİK: action her denemede TÜM verisini yeniden okumalıdır — bu yüzden her
/// denemeden önce ChangeTracker temizlenir; bayat entity ile retry anlamsızdır.
/// </summary>
public static class ConcurrencyRetry
{
    public const int DefaultAttempts = 3;

    public static async Task<T> RunAsync<T>(
        IAppDbContext db,
        Func<Task<T>> action,
        int attempts = DefaultAttempts,
        CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (DbUpdateConcurrencyException) when (attempt < attempts && !ct.IsCancellationRequested)
            {
                db.ClearChangeTracker();
            }
        }
    }
}
