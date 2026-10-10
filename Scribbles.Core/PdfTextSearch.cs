namespace Scribbles.Core;

/// <summary>Case-insensitive substring search over page words (Ctrl+F).</summary>
public static class PdfTextSearch
{
    public static IReadOnlyList<PdfWord> Find(IReadOnlyList<PdfWord> words, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<PdfWord>();
        string needle = query.Trim();
        return words
            .Where(w => w.Text.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
