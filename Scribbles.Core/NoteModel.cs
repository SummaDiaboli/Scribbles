namespace Scribbles.Core;

/// <summary>
/// Pen tool that produced a stroke. Mirrors the M2 toolbar.
/// </summary>
public enum ToolKind
{
    Pen,
    Highlighter,
}

/// <summary>
/// A single ink point in PDF points, y-DOWN (same orientation as the rendered
/// page image). The y-flip to PDF user space (y-up) happens only at the
/// PDF /Ink annotation boundary (see ScribblesPdfStore).
/// </summary>
public sealed record StrokePoint(double X, double Y, float Pressure = 0.5f);

/// <summary>One handwriting stroke on a page.</summary>
public sealed record InkStroke(
    string Id,
    int PageNumber,
    ToolKind Tool,
    uint ColorArgb,
    double WidthPt,
    IReadOnlyList<StrokePoint> Points)
{
    public (double MinX, double MinY, double MaxX, double MaxY) Bounds()
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in Points)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
        return (minX, minY, maxX, maxY);
    }
}

public sealed record NotePage(
    int Number,
    double WidthPt,
    double HeightPt,
    IReadOnlyList<InkStroke> Strokes);

/// <summary>
/// Full editable note model. Serialized as JSON into the PDF /PieceInfo
/// dictionary (app-private slot) for round-trip editing.
/// </summary>
public sealed record NoteDocument(
    string SourceHash,
    IReadOnlyList<NotePage> Pages);
