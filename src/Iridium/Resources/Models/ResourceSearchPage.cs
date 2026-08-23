namespace Iridium.Resources.Models;


public sealed record ResourceSearchPage<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
