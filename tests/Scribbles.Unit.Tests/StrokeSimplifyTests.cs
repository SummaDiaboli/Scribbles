using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

public sealed class StrokeSimplifyTests
{
    private static List<StrokePoint> Line(int n)
    {
        var pts = new List<StrokePoint>();
        for (int i = 0; i < n; i++)
            pts.Add(new StrokePoint(i, 2 * i, 0.5f));
        return pts;
    }

    [Fact]
    public void StraightLine_CollapsesToEndpoints()
    {
        var simplified = StrokeSimplify.Simplify(Line(50), 0.5);
        simplified.Count.ShouldBe(2);
        simplified[0].ShouldBe(new StrokePoint(0, 0, 0.5f));
        simplified[1].ShouldBe(new StrokePoint(49, 98, 0.5f));
    }

    [Fact]
    public void Corner_IsKept()
    {
        var pts = new List<StrokePoint>
        {
            new(0, 0), new(10, 0), new(20, 0), new(20, 10), new(20, 20),
        };
        var simplified = StrokeSimplify.Simplify(pts, 0.5);
        simplified.Count.ShouldBe(3);
        simplified[1].ShouldBe(new StrokePoint(20, 0));
    }

    [Fact]
    public void ShortInputs_PassThrough()
    {
        StrokeSimplify.Simplify(new List<StrokePoint>(), 0.5).ShouldBeEmpty();
        StrokeSimplify.Simplify(new List<StrokePoint> { new(1, 2, 0.7f) }, 0.5)
            .ShouldBe(new[] { new StrokePoint(1, 2, 0.7f) });
    }

    [Fact]
    public void Arc_StaysWithinTolerance_AndKeepsPressure()
    {
        var pts = new List<StrokePoint>();
        for (int i = 0; i <= 40; i++)
        {
            double t = i / 40.0 * Math.PI;
            pts.Add(new StrokePoint(100 * Math.Cos(t), 100 * Math.Sin(t), (float)(i / 40.0)));
        }
        var simplified = StrokeSimplify.Simplify(pts, 0.5);
        simplified.Count.ShouldBeGreaterThan(2, "curve needs intermediate points");
        simplified.Count.ShouldBeLessThan(41, "collinear runs still collapse");
        foreach (var p in simplified)
            p.Pressure.ShouldBeGreaterThanOrEqualTo(0f);
        // Every dropped point stays within tolerance of the kept polyline.
        foreach (var p in pts)
            DistanceToPolyline(p, simplified).ShouldBeLessThanOrEqualTo(0.5 + 1e-6);
    }

    private static double DistanceToPolyline(StrokePoint p, IReadOnlyList<StrokePoint> poly)
    {
        double best = double.MaxValue;
        for (int i = 0; i + 1 < poly.Count; i++)
            best = Math.Min(best, DistanceToSegment(p, poly[i], poly[i + 1]));
        return best;
    }

    private static double DistanceToSegment(StrokePoint p, StrokePoint a, StrokePoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 == 0)
            return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        double px = a.X + t * dx - p.X, py = a.Y + t * dy - p.Y;
        return Math.Sqrt(px * px + py * py);
    }
}
