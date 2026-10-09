namespace Scribbles.Core;

/// <summary>Groups ink strokes into text lines by vertical overlap (pure).</summary>
public static class InkLineGrouping
{
    public static IReadOnlyList<IReadOnlyList<InkStroke>> GroupByLines(IReadOnlyList<InkStroke> strokes)
    {
        var ordered = strokes
            .Select(s => (Stroke: s, Bounds: s.Bounds()))
            .OrderBy(t => t.Bounds.MinY)
            .ToList();
        var rows = new List<List<InkStroke>>();
        var rowBounds = new List<(double MinY, double MaxY)>();
        foreach (var (stroke, bounds) in ordered)
        {
            int row = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                if (bounds.MinY <= rowBounds[i].MaxY && rowBounds[i].MinY <= bounds.MaxY)
                {
                    row = i;
                    break;
                }
            }
            if (row < 0)
            {
                row = rows.Count;
                rows.Add(new List<InkStroke>());
                rowBounds.Add((bounds.MinY, bounds.MaxY));
            }
            rows[row].Add(stroke);
            rowBounds[row] = (
                Math.Min(rowBounds[row].MinY, bounds.MinY),
                Math.Max(rowBounds[row].MaxY, bounds.MaxY));
        }
        return rows
            .OrderBy(r => r.Min(s => s.Bounds().MinY))
            .Select(r => (IReadOnlyList<InkStroke>)r.OrderBy(s => s.Bounds().MinX).ToList())
            .ToList();
    }
}
