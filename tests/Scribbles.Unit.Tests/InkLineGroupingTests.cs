using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

/// <summary>TDD RED: group ink strokes into text lines for bulk convert/copyable.</summary>
public sealed class InkLineGroupingTests
{
    private static InkStroke Stroke(string id, double minX, double minY, double maxX, double maxY) => new(
        id, 1, ToolKind.Pen, 0xFF000000, 2.0,
        new[] { new StrokePoint(minX, minY), new StrokePoint(maxX, maxY) });

    [Fact]
    public void TwoRows_Separate()
    {
        var rows = InkLineGrouping.GroupByLines(new[]
        {
            Stroke("a", 10, 10, 50, 20),
            Stroke("b", 10, 100, 50, 110),
        });
        rows.Count.ShouldBe(2);
        rows[0].Select(s => s.Id).ShouldBe(new[] { "a" });
        rows[1].Select(s => s.Id).ShouldBe(new[] { "b" });
    }

    [Fact]
    public void OverlappingVertically_SameRow_LeftToRight()
    {
        var rows = InkLineGrouping.GroupByLines(new[]
        {
            Stroke("b", 100, 12, 150, 22),
            Stroke("a", 10, 10, 50, 20),
        });
        rows.ShouldHaveSingleItem();
        rows[0].Select(s => s.Id).ShouldBe(new[] { "a", "b" });
    }

    [Fact]
    public void Empty_StaysEmpty()
    {
        InkLineGrouping.GroupByLines(Array.Empty<InkStroke>()).ShouldBeEmpty();
    }
}
