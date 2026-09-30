namespace PaymentGateway.Application.Contracts;

/// <summary>
/// Generic paged result. Page numbers are 1-indexed. PageSize is capped by the API layer.
/// </summary>
public sealed record PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }

    public required int TotalCount { get; init; }

    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

    public bool HasNextPage => Page < TotalPages;
}
