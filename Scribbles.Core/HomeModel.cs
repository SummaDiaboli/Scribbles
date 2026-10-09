namespace Scribbles.Core;

/// <summary>Home-grid document kinds and helpers.</summary>
public enum HomeDocumentKind
{
    Pdf,
    Notebook,
    Slides,
}

public sealed record HomeDocument(string Name, string Path, HomeDocumentKind Kind, DateTime ModifiedUtc);

public static class NotebookNaming
{
    public const string Prefix = "Notebook";

    public static string NextName(IEnumerable<string> existingNames, string prefix = Prefix)
    {
        int max = 0;
        foreach (string name in existingNames)
        {
            string stem = Path.GetFileNameWithoutExtension(name);
            if (stem.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(stem.Substring(prefix.Length + 1), out int n))
                max = Math.Max(max, n);
        }
        return $"{prefix} {max + 1}";
    }
}

public enum SlideBackend
{
    PowerPoint,
    LibreOffice,
    ManualPdf,
}

public static class SlideImport
{
    public static SlideBackend ChooseBackend(bool officeAvailable, bool libreOfficeAvailable)
    {
        if (officeAvailable)
            return SlideBackend.PowerPoint;
        if (libreOfficeAvailable)
            return SlideBackend.LibreOffice;
        return SlideBackend.ManualPdf;
    }
}

public static class StandardPageSizes
{
    public const double A4WidthPt = 595.276;
    public const double A4HeightPt = 841.89;
}

/// <summary>Notebook files on disk (pure file-system queries).</summary>
public static class NotebookFiles
{
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Scribbles", "notebooks");

    public static IReadOnlyList<HomeDocument> List(string? directory = null)
    {
        directory ??= DefaultDirectory;
        if (!Directory.Exists(directory))
            return Array.Empty<HomeDocument>();
        try
        {
            return Directory.GetFiles(directory, "*.pdf")
                .Select(path => new FileInfo(path))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => new HomeDocument(f.Name, f.FullName, HomeDocumentKind.Notebook, f.LastWriteTimeUtc))
                .ToList();
        }
        catch
        {
            return Array.Empty<HomeDocument>();
        }
    }
}
