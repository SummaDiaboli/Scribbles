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

    /// <summary>Drops a path (e.g. after a rename or delete).</summary>
    public static string[] Remove(string[]? files, string path)
    {
        files ??= Array.Empty<string>();
        string? full = Normalize(path);
        if (full is null)
            return files;
        return files.Select(Normalize)
            .Where(f => f is not null && !string.Equals(f, full, StringComparison.OrdinalIgnoreCase))
            .ToArray()!;
    }

    /// <summary>Swaps an old path for a new one in place (rename support).</summary>
    public static string[] Rename(string[]? files, string oldPath, string newPath)
    {
        files ??= Array.Empty<string>();
        string? oldFull = Normalize(oldPath);
        string? newFull = Normalize(newPath);
        if (oldFull is null)
            return files;
        if (newFull is null)
            return Remove(files, oldPath);
        bool found = false;
        var result = files.Select(Normalize).Select(f =>
        {
            if (f is not null && string.Equals(f, oldFull, StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                return newFull;
            }
            return f;
        }).ToArray();
        return found ? result! : Add(files, newPath);
    }
}
