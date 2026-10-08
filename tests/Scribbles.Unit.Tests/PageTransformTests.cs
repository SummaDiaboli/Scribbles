using FsCheck.Xunit;
using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

public sealed class PageTransformTests
{
    [Fact]
    public void PageToScreen_At100Percent_UsesDpiScale()
    {
        var t = new PageTransform(PageWidthPt: 612, PageHeightPt: 792, Zoom: 1.0, DpiX: 96, DpiY: 96);
        var (sx, sy) = t.PageToScreen(72, 72);
        sx.ShouldBe(96.0);
        sy.ShouldBe(96.0);
    }

    [Fact]
    public void PageToScreen_RespectsZoom()
    {
        var t = new PageTransform(PageWidthPt: 612, PageHeightPt: 792, Zoom: 2.0, DpiX: 96, DpiY: 96);
        var (sx, _) = t.PageToScreen(72, 0);
        sx.ShouldBe(192.0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void RoundTrip_ThereAndBack_IsIdentity(int rotation)
    {
        var t = new PageTransform(PageWidthPt: 612, PageHeightPt: 792, Zoom: 1.5, DpiX: 144, DpiY: 144, RotationDegrees: rotation);
        var (sx, sy) = t.PageToScreen(123.4, 456.7);
        var (px, py) = t.ScreenToPage(sx, sy);
        px.ShouldBe(123.4, tolerance: 1e-6);
        py.ShouldBe(456.7, tolerance: 1e-6);
    }

    [Property]
    public bool RoundTrip_Property(double x, double y)
    {
        if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y))
            return true;
        x = Math.Abs(x) % 612;
        y = Math.Abs(y) % 792;
        var t = new PageTransform(PageWidthPt: 612, PageHeightPt: 792, Zoom: 1.25, DpiX: 96, DpiY: 96, RotationDegrees: 90);
        var (sx, sy) = t.PageToScreen(x, y);
        var (px, py) = t.ScreenToPage(sx, sy);
        return Math.Abs(px - x) < 1e-6 && Math.Abs(py - y) < 1e-6;
    }
}
