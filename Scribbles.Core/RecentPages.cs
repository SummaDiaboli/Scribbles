namespace Scribbles.Core;

/// <summary>Last-viewed page per document (1-based).</summary>
public sealed record LastPage(string Path, int Page);

/// <summary>Per-document page memory (most-recent-last, capped).</summary>
public static class RecentPages
{
    public const int MaxEntries = 50;

    public static IReadOnlyList<LastPage> Record(
        IReadOnlyList<LastPage> entries, string path, int page, int max = MaxEntries)
    {
        var result = entries
            .Where(e => !string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase))
            .ToList();
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            return result;
        }
        result.RemoveAll(e => string.Equals(e.Path, full, StringComparison.OrdinalIgnoreCase));
        result.Add(new LastPage(full, Math.Max(1, page)));
        while (result.Count > Math.Max(1, max))
            result.RemoveAt(0);
        return result;
    }

    public static int ForPath(IReadOnlyList<LastPage> entries, string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            full = path;
        }
        return entries.FirstOrDefault(e =>
            string.Equals(e.Path, full, StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase))
            ?.Page ?? 1;
    }
}
