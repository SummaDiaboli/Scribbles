namespace Scribbles.Core;

/// <summary>
/// Pure stroke operations backing the M2 toolbar (partial erase, lasso,
/// move/resize/recolor). All functions are side-effect free.
/// </summary>
public static class StrokeOps
{
    public static double DistancePointToSegment(
        double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-12)
            return Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
        double t = ((px - ax) * dx + (py - ay) * dy) / lenSq;
        t = Math.Clamp(t, 0.0, 1.0);
        double cx = ax + t * dx, cy = ay + t * dy;
        return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
    }

    /// <summary>True when the point hits the stroke within tolerance (points).</summary>
    public static bool HitTest(InkStroke stroke, double x, double y, double tolerancePt = 3.0)
    {
        var pts = stroke.Points;
        if (pts.Count == 1)
        {
            double d = Math.Sqrt((x - pts[0].X) * (x - pts[0].X) + (y - pts[0].Y) * (y - pts[0].Y));
            return d <= tolerancePt + stroke.WidthPt / 2.0;
        }
        double tol = tolerancePt + stroke.WidthPt / 2.0;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            if (DistancePointToSegment(x, y, pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y) <= tol)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Lasso hit-testing: a stroke is selected when any of its points lies
    /// inside the polygon or any of its segments crosses the polygon boundary.
    /// </summary>
    public static bool LassoHitTest(InkStroke stroke, IReadOnlyList<(double X, double Y)> polygon)
    {
        if (polygon.Count < 3 || stroke.Points.Count == 0)
            return false;

        // Fast bbox reject.
        var (minX, minY, maxX, maxY) = stroke.Bounds();
        double pMinX = polygon.Min(p => p.X), pMinY = polygon.Min(p => p.Y);
        double pMaxX = polygon.Max(p => p.X), pMaxY = polygon.Max(p => p.Y);
        if (maxX < pMinX || minX > pMaxX || maxY < pMinY || minY > pMaxY)
            return false;

        foreach (var p in stroke.Points)
        {
            if (IsPointInPolygon(p.X, p.Y, polygon))
                return true;
        }
        var pts = stroke.Points;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            for (int j = 0; j < polygon.Count; j++)
            {
                var a = polygon[j];
                var b = polygon[(j + 1) % polygon.Count];
                if (SegmentsIntersect(pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y, a.X, a.Y, b.X, b.Y))
                    return true;
            }
        }
        return false;
    }

    internal static bool IsPointInPolygon(double x, double y, IReadOnlyList<(double X, double Y)> poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var (xi, yi) = poly[i];
            var (xj, yj) = poly[j];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }

    internal static bool SegmentsIntersect(
        double x1, double y1, double x2, double y2,
        double x3, double y3, double x4, double y4)
    {
        double d = (x2 - x1) * (y4 - y3) - (y2 - y1) * (x4 - x3);
        if (Math.Abs(d) < 1e-12)
            return false;
        double t = ((x3 - x1) * (y4 - y3) - (y3 - y1) * (x4 - x3)) / d;
        double u = ((x3 - x1) * (y2 - y1) - (y3 - y1) * (x2 - x1)) / d;
        return t is >= 0 and <= 1 && u is >= 0 and <= 1;
    }

    public static InkStroke Move(InkStroke stroke, double dx, double dy) =>
        stroke with
        {
            Points = stroke.Points.Select(p => p with { X = p.X + dx, Y = p.Y + dy }).ToList()
        };

    public static InkStroke Recolor(InkStroke stroke, uint colorArgb) =>
        stroke with { ColorArgb = colorArgb };

    /// <summary>Scale a stroke about a center (defaults to its centroid).</summary>
    public static InkStroke Resize(InkStroke stroke, double factor, (double X, double Y)? center = null)
    {
        var c = center ?? Centroid(stroke);
        return stroke with
        {
            Points = stroke.Points
                .Select(p => p with { X = c.X + (p.X - c.X) * factor, Y = c.Y + (p.Y - c.Y) * factor })
                .ToList(),
            WidthPt = stroke.WidthPt * factor,
        };
    }

    public static (double X, double Y) Centroid(InkStroke stroke)
    {
        if (stroke.Points.Count == 0)
            return (0, 0);
        return (stroke.Points.Average(p => p.X), stroke.Points.Average(p => p.Y));
    }

    /// <summary>
    /// Partial erase: removes points within <paramref name="radiusPt"/> of the
    /// eraser segment and returns the surviving runs as new strokes (WPF
    /// EraseByPoint semantics, mirrored here for testability).
    /// </summary>
    public static IReadOnlyList<InkStroke> EraseSplit(
        InkStroke stroke,
        (double X, double Y) eraserFrom,
        (double X, double Y) eraserTo,
        double radiusPt)
    {
        var runs = new List<List<StrokePoint>>();
        var current = new List<StrokePoint>();
        foreach (var p in stroke.Points)
        {
            bool erased = DistancePointToSegment(
                p.X, p.Y, eraserFrom.X, eraserFrom.Y, eraserTo.X, eraserTo.Y) <= radiusPt;
            if (erased)
            {
                if (current.Count >= 2)
                    runs.Add(current);
                current = new List<StrokePoint>();
            }
            else
            {
                current.Add(p);
            }
        }
        if (current.Count >= 2)
            runs.Add(current);

        return runs.Select((run, i) => stroke with
        {
            Id = $"{stroke.Id}/e{i}",
            Points = run,
        }).ToList();
    }
}
