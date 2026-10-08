using FsCheck.Xunit;
using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

public sealed class StrokeOpsTests
{
    private static InkStroke Line(string id = "s1") => new(
        id, 1, ToolKind.Pen, 0xFF000000, 2.0,
        new[] { new StrokePoint(0, 0), new StrokePoint(10, 0), new StrokePoint(20, 0) });

    [Fact]
    public void HitTest_OnStroke_ReturnsTrue()
    {
        StrokeOps.HitTest(Line(), 10, 1).ShouldBeTrue();
    }

    [Fact]
    public void HitTest_FarAway_ReturnsFalse()
    {
        StrokeOps.HitTest(Line(), 10, 50).ShouldBeFalse();
    }

    [Fact]
    public void LassoHitTest_StrokeInsidePolygon_Selected()
    {
        var poly = new List<(double, double)> { (0, -10), (30, -10), (30, 10), (0, 10) };
        StrokeOps.LassoHitTest(Line(), poly).ShouldBeTrue();
    }

    [Fact]
    public void LassoHitTest_StrokeOutsidePolygon_NotSelected()
    {
        var poly = new List<(double, double)> { (100, 100), (200, 100), (200, 200), (100, 200) };
        StrokeOps.LassoHitTest(Line(), poly).ShouldBeFalse();
    }

    [Fact]
    public void Move_TranslatesAllPoints()
    {
        var moved = StrokeOps.Move(Line(), 5, -3);
        moved.Points[0].X.ShouldBe(5);
        moved.Points[0].Y.ShouldBe(-3);
        moved.Points[2].X.ShouldBe(25);
    }

    [Fact]
    public void Recolor_ChangesColorOnly()
    {
        var recolored = StrokeOps.Recolor(Line(), 0xFFFF0000);
        recolored.ColorArgb.ShouldBe(0xFFFF0000);
        recolored.Points.Count.ShouldBe(3);
    }

    [Fact]
    public void Resize_DoublesDistancesFromCentroid()
    {
        var resized = StrokeOps.Resize(Line(), 2.0);
        resized.Points[0].X.ShouldBe(-10, tolerance: 1e-6);
        resized.Points[2].X.ShouldBe(30, tolerance: 1e-6);
        resized.WidthPt.ShouldBe(4.0);
    }

    [Fact]
    public void EraseSplit_MiddleErased_YieldsTwoStrokes()
    {
        var stroke = new InkStroke("s", 1, ToolKind.Pen, 0xFF000000, 2.0,
            Enumerable.Range(0, 11).Select(i => new StrokePoint(i, 0)).ToList());
        var parts = StrokeOps.EraseSplit(stroke, (4.5, 0), (6.5, 0), radiusPt: 1.5);
        parts.Count.ShouldBe(2);
        parts[0].Points.Last().X.ShouldBe(2);
        parts[1].Points.First().X.ShouldBe(9);
    }

    [Fact]
    public void EraseSplit_AllErased_YieldsNothing()
    {
        var parts = StrokeOps.EraseSplit(Line(), (-5, 0), (25, 0), radiusPt: 5);
        parts.ShouldBeEmpty();
    }
}

public sealed class NoteSerializerTests
{
    private static NoteDocument Sample() => new("hash123", new[]
    {
        new NotePage(1, 612, 792, new[]
        {
            new InkStroke("a", 1, ToolKind.Pen, 0xFF112233, 2.5,
                new[] { new StrokePoint(1, 2, 0.8f), new StrokePoint(3, 4, 0.4f) }),
        }),
    });

    [Fact]
    public void RoundTrip_PreservesModel()
    {
        var doc = Sample();
        var restored = NoteSerializer.FromJson(NoteSerializer.ToJson(doc));
        restored.ShouldNotBeNull();
        restored!.SourceHash.ShouldBe("hash123");
        var stroke = restored.Pages[0].Strokes[0];
        stroke.Id.ShouldBe("a");
        stroke.ColorArgb.ShouldBe(0xFF112233);
        stroke.WidthPt.ShouldBe(2.5);
        stroke.Points[0].ShouldBe(new StrokePoint(1, 2, 0.8f));
    }

    [Fact]
    public void FromJson_WrongVersion_ReturnsNull()
    {
        NoteSerializer.FromJson("""{"Version":999,"Document":null}""").ShouldBeNull();
    }

    [Fact]
    public void FromJson_Garbage_ReturnsNull()
    {
        NoteSerializer.FromJson("not json").ShouldBeNull();
    }

    [Property]
    public bool RoundTrip_Property(double x, double y)
    {
        if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y))
            return true;
        var doc = new NoteDocument("h", new[]
        {
            new NotePage(1, 612, 792, new[]
            {
                new InkStroke("p", 1, ToolKind.Highlighter, 0x80FF0000, 8.0,
                    new[] { new StrokePoint(x, y), new StrokePoint(x + 1, y + 1) }),
            }),
        });
        var restored = NoteSerializer.FromJson(NoteSerializer.ToJson(doc));
        return restored is not null
            && Math.Abs(restored.Pages[0].Strokes[0].Points[0].X - x) < 1e-9
            && Math.Abs(restored.Pages[0].Strokes[0].Points[0].Y - y) < 1e-9;
    }
}

public sealed class UndoRedoTests
{
    [Fact]
    public void Undo_RestoresPrior_Redo_Reapplies()
    {
        var history = new UndoRedo<string>();
        history.Push("v1");
        history.CanUndo.ShouldBeTrue();
        history.TryUndo("v2", out var prior).ShouldBeTrue();
        prior.ShouldBe("v1");
        history.TryRedo("v2-again", out var next).ShouldBeTrue();
        next.ShouldBe("v2");
    }

    [Fact]
    public void NewPush_ClearsRedo()
    {
        var history = new UndoRedo<string>();
        history.Push("v1");
        history.TryUndo("v2", out _);
        history.Push("v3");
        history.CanRedo.ShouldBeFalse();
    }
}

public sealed class ExportPlannerTests
{
    [Fact]
    public void Keep_WritesOnlyInk() =>
        ExportPlanner.Plan(InkHandleMode.Keep)
            .ShouldBe(new ExportPlan(true, false, false));

    [Fact]
    public void Convert_WritesOnlyText() =>
        ExportPlanner.Plan(InkHandleMode.Convert)
            .ShouldBe(new ExportPlan(false, true, false));

    [Fact]
    public void Copyable_WritesInkPlusInvisibleLayer() =>
        ExportPlanner.Plan(InkHandleMode.Copyable)
            .ShouldBe(new ExportPlan(true, false, true));
}
