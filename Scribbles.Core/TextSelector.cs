namespace Scribbles.Core;

/// <summary>A native PDF text word in PDF points, y-DOWN (top edge).</summary>
public sealed record PdfWord(string Text, double X, double Y, double Width, double Height)
{
    public double CenterY => Y + Height / 2.0;

    public bool Intersects(double rx, double ry, double rw, double rh) =>
        X < rx + rw && rx < X + Width && Y < ry + rh && ry < Y + Height;

    public bool Contains(double x, double y, double tolerance = 0) =>
        x >= X - tolerance && x <= X + Width + tolerance
        && y >= Y - tolerance && y <= Y + Height + tolerance;

    /// <summary>Distance from a point to the word box (0 when inside).</summary>
    public double EdgeDistance(double x, double y)
    {
        double dx = x < X ? X - x : x > X + Width ? x - (X + Width) : 0;
        double dy = y < Y ? Y - y : y > Y + Height ? y - (Y + Height) : 0;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>Marquee rectangle in PDF points (any corner order).</summary>
public sealed record SelectionRect(double X, double Y, double Width, double Height)
{
    public (double X, double Y, double W, double H) Normalized()
    {
        double x = Width >= 0 ? X : X + Width;
        double y = Height >= 0 ? Y : Y + Height;
        return (x, y, Math.Abs(Width), Math.Abs(Height));
    }
}

public sealed record SelectedText(IReadOnlyList<PdfWord> Words, string Text);

/// <summary>Pure native-text selection: marquee overlap and browser anchor-range.</summary>
public static class TextSelector
{
    public static SelectedText Select(IReadOnlyList<PdfWord> words, SelectionRect marquee)
    {
        var (rx, ry, rw, rh) = marquee.Normalized();
        if (rw <= 0 || rh <= 0 || words.Count == 0)
            return new SelectedText(Array.Empty<PdfWord>(), "");

        var hit = words.Where(w => w.Intersects(rx, ry, rw, rh)).ToList();
        return FromSet(hit);
    }

    /// <summary>Words grouped into lines, top-to-bottom, left-to-right.</summary>
    public static IReadOnlyList<IReadOnlyList<PdfWord>> OrderReading(IReadOnlyList<PdfWord> words)
    {
        var byY = words.OrderBy(w => w.CenterY).ToList();
        var lines = new List<List<PdfWord>>();
        foreach (var word in byY)
        {
            var line = lines.FirstOrDefault(l =>
                Math.Abs(l[0].CenterY - word.CenterY) <= Math.Max(l[0].Height, word.Height) / 2.0);
            if (line is null)
            {
                line = new List<PdfWord>();
                lines.Add(line);
            }
            line.Add(word);
        }
        return lines
            .OrderBy(l => l.Min(w => w.CenterY))
            .Select(l => (IReadOnlyList<PdfWord>)l.OrderBy(w => w.X).ToList())
            .ToList();
    }

    /// <summary>Word containing the point (with grab tolerance), or null.</summary>
    public static PdfWord? WordAt(IReadOnlyList<PdfWord> words, double x, double y, double tolerance = 2.0)
    {
        PdfWord? best = null;
        double bestArea = double.MaxValue;
        foreach (var word in words)
        {
            if (word.Contains(x, y, tolerance) && word.Width * word.Height < bestArea)
            {
                best = word;
                bestArea = word.Width * word.Height;
            }
        }
        return best;
    }

    /// <summary>Nearest word within maxDistancePt, or null when text is far away.</summary>
    public static PdfWord? WordNearest(IReadOnlyList<PdfWord> words, double x, double y, double maxDistancePt = 15.0)
    {
        PdfWord? best = null;
        double bestDist = maxDistancePt;
        foreach (var word in words)
        {
            double dist = word.EdgeDistance(x, y);
            if (dist < bestDist)
            {
                best = word;
                bestDist = dist;
            }
        }
        return best;
    }

    /// <summary>
    /// Browser anchor-range: span between two words in reading order
    /// (direction-independent), lines joined with newlines.
    /// </summary>
    public static SelectedText Range(
        IReadOnlyList<IReadOnlyList<PdfWord>> lines, PdfWord anchor, PdfWord focus)
    {
        var flat = lines.SelectMany(l => l).ToList();
        int a = flat.IndexOf(anchor);
        int f = flat.IndexOf(focus);
        if (a < 0 || f < 0)
            return new SelectedText(Array.Empty<PdfWord>(), "");
        return FromSet(flat.Skip(Math.Min(a, f)).Take(Math.Abs(f - a) + 1).ToList());
    }

    private static SelectedText FromSet(List<PdfWord> hit)
    {
        if (hit.Count == 0)
            return new SelectedText(Array.Empty<PdfWord>(), "");
        var lines = OrderReading(hit);
        var ordered = lines.SelectMany(l => l).ToList();
        string text = string.Join("\n",
            lines.Select(l => string.Join(" ", l.Select(w => w.Text))));
        return new SelectedText(ordered, text);
    }
}
