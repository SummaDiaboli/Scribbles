namespace Scribbles.Core;

/// <summary>A raw text glyph in PDF points, y-DOWN (top edge).</summary>
public sealed record TextGlyph(string Text, double X, double Y, double Width, double Height);

/// <summary>
/// Groups raw glyphs into words for PDFs that expose letters but no words
/// (subset/odd fonts). Pure and unit-tested; feeds <see cref="TextSelector"/>.
/// </summary>
public static class LetterGrouping
{
    public static IReadOnlyList<PdfWord> Group(IReadOnlyList<TextGlyph> glyphs)
    {
        var useful = glyphs.Where(g => !string.IsNullOrEmpty(g.Text) && g.Width > 0 && g.Height > 0).ToList();
        if (useful.Count == 0)
            return Array.Empty<PdfWord>();

        // Rows by top edge, then left-to-right.
        var byTop = useful.OrderBy(g => g.Y).ToList();
        var rows = new List<List<TextGlyph>>();
        foreach (var glyph in byTop)
        {
            var row = rows.FirstOrDefault(r =>
                Math.Abs(r[0].Y - glyph.Y) <= Math.Max(r[0].Height, glyph.Height) / 2.0);
            if (row is null)
            {
                row = new List<TextGlyph>();
                rows.Add(row);
            }
            row.Add(glyph);
        }

        var words = new List<PdfWord>();
        foreach (var row in rows.OrderBy(r => r.Min(g => g.Y)))
        {
            var ordered = row.OrderBy(g => g.X).ToList();
            double lineHeight = ordered.Max(g => g.Height);
            double gapLimit = lineHeight * 0.4;
            var current = new List<TextGlyph> { ordered[0] };
            foreach (var glyph in ordered.Skip(1))
            {
                var prev = current[^1];
                if (glyph.X - (prev.X + prev.Width) > gapLimit)
                {
                    words.Add(Merge(current));
                    current = new List<TextGlyph>();
                }
                current.Add(glyph);
            }
            words.Add(Merge(current));
        }
        return words;
    }

    private static PdfWord Merge(List<TextGlyph> glyphs) => new(
        string.Concat(glyphs.Select(g => g.Text)),
        glyphs.Min(g => g.X),
        glyphs.Min(g => g.Y),
        glyphs.Max(g => g.X + g.Width) - glyphs.Min(g => g.X),
        glyphs.Max(g => g.Y + g.Height) - glyphs.Min(g => g.Y));
}
