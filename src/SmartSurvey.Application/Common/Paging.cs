namespace SmartSurvey.Application.Common;

/// <summary>Base class for paged list queries. Values are clamped to sane bounds.</summary>
public class PageRequest
{
    private int _page = 1;
    private int _pageSize = 20;

    /// <summary>Maximum page size accepted by any list endpoint.</summary>
    public const int MaxPageSize = 200;

    /// <summary>1-based page number.</summary>
    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    /// <summary>Items per page (1..200).</summary>
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Clamp(value, 1, MaxPageSize);
    }

    /// <summary>Number of items to skip.</summary>
    public int Skip => (Page - 1) * PageSize;
}

/// <summary>A page of results.</summary>
/// <typeparam name="T">Item type.</typeparam>
/// <param name="Items">Items on this page.</param>
/// <param name="TotalCount">Total number of matching items.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Requested page size.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    /// <summary>Total number of pages (at least 1).</summary>
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize)));

    /// <summary>True when a next page exists.</summary>
    public bool HasNextPage => Page < TotalPages;

    /// <summary>True when a previous page exists.</summary>
    public bool HasPreviousPage => Page > 1;

    /// <summary>An empty page.</summary>
    public static PagedResult<T> Empty(int page = 1, int pageSize = 20) => new([], 0, page, pageSize);
}
