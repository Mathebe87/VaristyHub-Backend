namespace VarsityHub.Common;

/// <summary>
/// Standard pagination/sorting query parameters for list endpoints.
/// PageSize is capped so a client can't request unbounded rows.
/// </summary>
public sealed record PageQuery
{
    private const int MaxPageSize = 100;
    private int _pageSize = 20;

    public int Page { get; init; } = 1;

    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value < 1 ? 20 : Math.Min(value, MaxPageSize);
    }

    public string? Sort { get; init; }

    /// <summary>Zero-based row offset for the current page.</summary>
    public int Offset => (Math.Max(1, Page) - 1) * PageSize;
}

/// <summary>
/// A page of results plus the total count for client-side pagination controls.
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long Total);
