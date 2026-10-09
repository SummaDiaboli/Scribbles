namespace Scribbles.Core;

/// <summary>
/// Crash-recovery working copies under a directory (default
/// %LocalAppData%/Scribbles/working). Pure file ops — unit-tested.
/// </summary>
public sealed class AutosaveService
{
    public string Directory { get; }

    /// <summary>Retention caps: newest copies kept, older-than-this dropped.</summary>
    public const int MaxCopies = 10;
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    public sealed record AutosaveSnapshot(string PdfPath, string? SourcePath, DateTime SavedUtc);

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Scribbles", "working");

    public AutosaveService(string? directory = null)
    {
        Directory = directory ?? DefaultDirectory;
    }

    public static string KeyFor(string? sourcePath) =>
        NoteSerializer.Sha256Hex(sourcePath ?? "unsaved")[..16];

    public string PdfPathFor(string key) => Path.Combine(Directory, key + ".pdf");

    public void Save(string key, byte[] pdfBytes, string? sourcePath)
    {
        System.IO.Directory.CreateDirectory(Directory);
        string pdfPath = PdfPathFor(key);
        File.WriteAllBytes(pdfPath, pdfBytes);
        File.WriteAllText(pdfPath + ".info", $"{sourcePath}\n{DateTime.UtcNow:o}");
        Prune();
    }

    /// <summary>Forgets one working copy (e.g. after a successful save).</summary>
    public void Delete(string key)
    {
        try
        {
            File.Delete(PdfPathFor(key));
            File.Delete(PdfPathFor(key) + ".info");
        }
        catch
        {
            // Hygiene must never break saving.
        }
    }

    /// <summary>Enforces the retention caps (newest kept, stale dropped).</summary>
    public void Prune()
    {
        List<AutosaveSnapshot> snapshots;
        try
        {
            snapshots = ListSnapshots().ToList();
        }
        catch
        {
            return;
        }
        DateTime cutoff = DateTime.UtcNow - MaxAge;
        for (int i = 0; i < snapshots.Count; i++)
        {
            if (i >= MaxCopies || snapshots[i].SavedUtc < cutoff)
                Delete(KeyFromPdfPath(snapshots[i].PdfPath));
        }
    }

    private static string KeyFromPdfPath(string pdfPath) =>
        Path.GetFileNameWithoutExtension(pdfPath);

    /// <summary>All restorable snapshots, newest first.</summary>
    public IReadOnlyList<AutosaveSnapshot> ListSnapshots()
    {
        if (!System.IO.Directory.Exists(Directory))
            return Array.Empty<AutosaveSnapshot>();
        return System.IO.Directory.GetFiles(Directory, "*.pdf")
            .Where(f => !f.EndsWith(".info", StringComparison.OrdinalIgnoreCase))
            .Select(pdfPath => (PdfPath: pdfPath, Info: pdfPath + ".info"))
            .Where(t => File.Exists(t.Info))
            .Select(t =>
            {
                string[] lines = File.ReadAllLines(t.Info);
                string? source = lines.Length > 0 ? lines[0].Trim() : null;
                if (string.IsNullOrEmpty(source))
                    source = null;
                // File mtime is the stamp (single source of truth).
                return new AutosaveSnapshot(t.PdfPath, source, File.GetLastWriteTimeUtc(t.Info));
            })
            .OrderByDescending(s => s.SavedUtc)
            .ToList();
    }

    public string? LatestInfoPath()
    {
        if (!System.IO.Directory.Exists(Directory))
            return null;
        return System.IO.Directory.GetFiles(Directory, "*.pdf.info")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
    }

    /// <returns>(working-copy PDF path, original source path or null).</returns>
    public (string PdfPath, string? SourcePath)? ReadLatest()
    {
        string? info = LatestInfoPath();
        if (info is null)
            return null;
        string pdfPath = info[..^".info".Length];
        if (!File.Exists(pdfPath))
            return null;
        string[] lines = File.ReadAllLines(info);
        string? source = lines.Length > 0 ? lines[0].Trim() : null;
        if (string.IsNullOrEmpty(source))
            source = null;
        return (pdfPath, source);
    }
}
