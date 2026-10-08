namespace Scribbles.Core;

/// <summary>
/// Crash-recovery working copies under a directory (default
/// %LocalAppData%/Scribbles/working). Pure file ops — unit-tested.
/// </summary>
public sealed class AutosaveService
{
    public string Directory { get; }

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
