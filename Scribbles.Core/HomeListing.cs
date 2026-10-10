namespace Scribbles.Core;

/// <summary>Home grid ordering and filtering (pure; the view maps to cards).</summary>
public enum HomeSort
{
    Newest,
    Name,
}

public enum HomeKindFilter
{
    All,
    Notebooks,
    Pdfs,
}

public static class HomeListing
{
    public static IReadOnlyList<HomeDocument> Apply(
        IEnumerable<HomeDocument> docs,
        HomeSort sort,
        HomeKindFilter kind,
        string? query)
    {
        var filtered = docs.Where(d => kind switch
        {
            HomeKindFilter.Notebooks => d.Kind == HomeDocumentKind.Notebook,
            HomeKindFilter.Pdfs => d.Kind == HomeDocumentKind.Pdf,
            _ => true,
        });
        if (!string.IsNullOrWhiteSpace(query))
        {
            string needle = query.Trim();
            filtered = filtered.Where(d =>
                d.Name.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }
        return (sort switch
        {
            HomeSort.Name => filtered
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase),
            _ => filtered
                .OrderByDescending(d => d.ModifiedUtc)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase),
        }).ToList();
    }
}
