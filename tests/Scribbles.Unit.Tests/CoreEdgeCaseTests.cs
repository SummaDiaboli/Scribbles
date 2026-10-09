using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

public sealed class StrokeOpsEdgeTests
{
    private static InkStroke Stroke(params (double X, double Y)[] points) => new(
        "s1", 1, ToolKind.Pen, 0xFF000000, 2.0,
        points.Select(p => new StrokePoint(p.X, p.Y)).ToList());

    private static readonly List<(double, double)> Square =
        new() { (0, 0), (30, 0), (30, 10), (0, 10) };

    [Fact]
    public void DistanceToDegenerateSegment_IsDistanceToItsPoint()
    {
        StrokeOps.DistancePointToSegment(3, 4, 0, 0, 0, 0).ShouldBe(5, tolerance: 1e-9);
    }

    [Fact]
    public void HitTest_SinglePointStroke_UsesRadius()
    {
        var dot = Stroke((10, 10));
        StrokeOps.HitTest(dot, 12, 10).ShouldBeTrue();
        StrokeOps.HitTest(dot, 20, 10).ShouldBeFalse();
    }

    [Fact]
    public void LassoHitTest_DegeneratePolygon_SelectsNothing()
    {
        var poly = new List<(double, double)> { (0, 0), (30, 0) };
        StrokeOps.LassoHitTest(Stroke((5, 0), (10, 0)), poly).ShouldBeFalse();
    }

    [Fact]
    public void LassoHitTest_StrokeCrossingLassoWithNoPointInside_Selected()
    {
        // Both endpoints lie outside the square; only the segment crosses it.
        StrokeOps.LassoHitTest(Stroke((15, -20), (15, 30)), Square).ShouldBeTrue();
    }

    [Fact]
    public void LassoHitTest_StrokeInBoundsButMissingLasso_NotSelected()
    {
        // L-shaped lasso: the stroke sits inside its bounding box but outside the shape.
        var lShape = new List<(double, double)> { (0, 0), (10, 0), (10, 20), (30, 20), (30, 30), (0, 30) };
        StrokeOps.LassoHitTest(Stroke((15, 5), (25, 5)), lShape).ShouldBeFalse();
    }

    [Fact]
    public void Centroid_EmptyStroke_IsOrigin()
    {
        StrokeOps.Centroid(Stroke()).ShouldBe((0.0, 0.0));
    }
}

public sealed class UndoRedoEdgeTests
{
    [Fact]
    public void Push_BeyondCapacity_DropsOldest()
    {
        var history = new UndoRedo<int>(capacity: 2);
        history.Push(1);
        history.Push(2);
        history.Push(3);

        history.UndoCount.ShouldBe(2);
        history.TryUndo(4, out var a).ShouldBeTrue();
        history.TryUndo(a, out var b).ShouldBeTrue();
        (a, b).ShouldBe((3, 2));
        history.TryUndo(b, out _).ShouldBeFalse();
    }

    [Fact]
    public void UndoAndRedo_WhenEmpty_ReturnCurrentUnchanged()
    {
        var history = new UndoRedo<string>();
        history.TryUndo("now", out var prior).ShouldBeFalse();
        prior.ShouldBe("now");
        history.TryRedo("now", out var next).ShouldBeFalse();
        next.ShouldBe("now");
    }

    [Fact]
    public void Clear_EmptiesBothStacks()
    {
        var history = new UndoRedo<string>();
        history.Push("v1");
        history.Push("v2");
        history.TryUndo("v3", out _);

        history.Clear();

        history.CanUndo.ShouldBeFalse();
        history.CanRedo.ShouldBeFalse();
    }
}

public sealed class TextBoxItemTests
{
    [Fact]
    public void Clone_CopiesEveryField_WithoutAliasing()
    {
        var original = new TextBoxItem
        {
            Id = "t1", PageNumber = 2, X = 1, Y = 2, Width = 3, Height = 4,
            FontSizePt = 18, ColorArgb = 0xFF112233, Kind = TextBoxKind.EditedNative, Text = "hi",
            DisplayX = 5, DisplayY = 6, DisplayWidth = 7, DisplayHeight = 8, DisplayFontSize = 24,
        };

        var copy = original.Clone();
        original.Text = "changed";

        copy.ShouldNotBeSameAs(original);
        copy.Text.ShouldBe("hi");
        (copy.Id, copy.PageNumber, copy.X, copy.Y, copy.Width, copy.Height)
            .ShouldBe(("t1", 2, 1.0, 2.0, 3.0, 4.0));
        (copy.FontSizePt, copy.ColorArgb, copy.Kind).ShouldBe((18.0, 0xFF112233u, TextBoxKind.EditedNative));
        (copy.DisplayX, copy.DisplayY, copy.DisplayWidth, copy.DisplayHeight, copy.DisplayFontSize)
            .ShouldBe((5.0, 6.0, 7.0, 8.0, 24.0));
    }

    [Fact]
    public void Text_RaisesPropertyChanged()
    {
        var item = new TextBoxItem();
        var raised = new List<string?>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        item.Text = "new";

        raised.ShouldContain(nameof(TextBoxItem.Text));
    }
}

public sealed class AppSettingsHashTests
{
    private static AppSettings Copy(AppSettings s) => s with
    {
        Palette = s.Palette.ToArray(),
        RecentFiles = new[] { @"C:\a.pdf" },
        LastPages = new List<LastPage> { new(@"C:\a.pdf", 3) },
        Window = new WindowPlacement(1, 2, 300, 400, false),
    };

    [Fact]
    public void EqualSettings_WithDistinctArrays_HashTheSame()
    {
        var a = Copy(AppSettings.Default);
        var b = Copy(AppSettings.Default);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void DifferentLastPage_ChangesHash()
    {
        var a = Copy(AppSettings.Default);
        var b = a with { LastPages = new List<LastPage> { new(@"C:\a.pdf", 4) } };

        a.ShouldNotBe(b);
        a.GetHashCode().ShouldNotBe(b.GetHashCode());
    }
}

public sealed class InkUnitsTests
{
    [Fact]
    public void Argb_PacksAndUnpacks()
    {
        uint argb = InkUnits.ToArgb(0x80, 0x11, 0x22, 0x33);
        argb.ShouldBe(0x80112233u);
        InkUnits.FromArgb(argb).ShouldBe(((byte)0x80, (byte)0x11, (byte)0x22, (byte)0x33));
    }

#pragma warning disable CS0618 // legacy fixed-DPI helpers stay correct until removed
    [Fact]
    public void LegacyDipPointConversion_RoundTrips()
    {
        InkUnits.PtToDip(72).ShouldBe(96, tolerance: 1e-9);
        InkUnits.DipToPt(InkUnits.PtToDip(12.5)).ShouldBe(12.5, tolerance: 1e-9);
    }
#pragma warning restore CS0618
}
