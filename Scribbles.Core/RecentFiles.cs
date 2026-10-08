namespace Scribbles.Core;

/// <summary>Most-recent-first recent-files list (capped, deduped).</summary>
public static class RecentFiles
{
    public const int MaxFiles = 8;

    public static string[] Add(string[]? files, string path, int max = MaxFiles)
    {
        files ??= Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(path))
            return files;
        string? full = Normalize(path);
        if (full is null)
            return files;
        return files.Select(Normalize)
            .Where(f => f is not null && !string.Equals(f, full, StringComparison.OrdinalIgnoreCase))
            .Prepend(full!)
            .Take(Math.Max(1, max))
            .ToArray()!;
    }

    private static string? Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch
        {
            return null;
        }
    }

    public static string[] PruneMissing(string[]? files) =>
        (files ?? Array.Empty<string>()).Where(File.Exists).ToArray();
}
