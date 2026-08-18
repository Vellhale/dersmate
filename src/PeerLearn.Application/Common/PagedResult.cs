namespace PeerLearn.Application.Common;

/// <summary>
/// Sayfalı sorgu sonucu. TotalCount ayrıca döner çünkü arayüz "1.240 sonuç" yazacak ve
/// sayfa çubuğunu buna göre çizecek; yalnızca "sonraki sayfa var mı" bilgisi yetmez.
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;

    public static PagedResult<T> Empty(int page, int pageSize) => new([], 0, page, pageSize);
}
