namespace Scribbles.Core;

/// <summary>
/// Ramer–Douglas–Peucker simplification for captured strokes: dense pen
/// packets collapse to the points that matter, shrinking the model, files,
/// and every downstream conversion. Endpoints (with pressure) always survive.
/// </summary>
public static class StrokeSimplify
{
    public static IReadOnlyList<StrokePoint> Simplify(IReadOnlyList<StrokePoint> points, double tolerancePt)
    {
        if (points.Count < 3 || tolerancePt <= 0)
            return points;
        var keep = new bool[points.Count];
        keep[0] = true;
        keep[points.Count - 1] = true;
        SimplifyRange(points, 0, points.Count - 1, tolerancePt, keep);
        var result = new List<StrokePoint>();
        for (int i = 0; i < points.Count; i++)
        {
            if (keep[i])
                result.Add(points[i]);
        }
        return result;
    }

    private static void SimplifyRange(
        IReadOnlyList<StrokePoint> points, int first, int last, double tolerance, bool[] keep)
    {
        double maxDist = 0;
        int split = -1;
        for (int i = first + 1; i < last; i++)
        {
            double dist = PerpendicularDistance(points[i], points[first], points[last]);
            if (dist > maxDist)
            {
                maxDist = dist;
                split = i;
            }
        }
        if (split >= 0 && maxDist > tolerance)
        {
            keep[split] = true;
            SimplifyRange(points, first, split, tolerance, keep);
            SimplifyRange(points, split, last, tolerance, keep);
        }
    }

    private static double PerpendicularDistance(StrokePoint p, StrokePoint a, StrokePoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 == 0)
        {
            double ex = p.X - a.X, ey = p.Y - a.Y;
            return Math.Sqrt(ex * ex + ey * ey);
        }
        // Distance to the segment (not the infinite line): ends are pinned.
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        double px = a.X + t * dx - p.X, py = a.Y + t * dy - p.Y;
        return Math.Sqrt(px * px + py * py);
    }
}
