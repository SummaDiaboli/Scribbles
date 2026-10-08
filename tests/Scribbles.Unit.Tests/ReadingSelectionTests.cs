using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

/// <summary>
/// TDD RED (2026-10-08): browser anchor-range selection over reading order.
/// Types don't exist yet — must fail before they land.
/// </summary>
public sealed class ReadingSelectionTests
{
    private static IReadOnlyList<PdfWord> Doc() => new[]
    {
        new PdfWord("Hello", 50, 100, 40, 14),
        new PdfWord("world", 95, 100, 42, 14),
        new PdfWord("Second", 50, 130, 48, 14),
        new PdfWord("line", 103, 130, 30, 14),
    };

    [Fact]
    public void OrderReading_GroupsLinesTopDown_WordsLeftRight()
    {
        var lines = TextSelector.OrderReading(new[] { Doc()[3], Doc()[0], Doc()[2], Doc()[1] });
        lines.Count.ShouldBe(2);
        lines[0].Select(w => w.Text).ShouldBe(new[] { "Hello", "world" });
        lines[1].Select(w => w.Text).ShouldBe(new[] { "Second", "line" });
    }

    [Fact]
    public void WordAt_HitsInsideWord()
    {
        TextSelector.WordAt(Doc(), 60, 105)?.Text.ShouldBe("Hello");
    }

    [Fact]
    public void WordAt_MissesFarAway()
    {
        TextSelector.WordAt(Doc(), 500, 500).ShouldBeNull();
    }

    [Fact]
    public void Range_Forward_SelectsSpanWithSpaces()
    {
        var lines = TextSelector.OrderReading(Doc());
        var selected = TextSelector.Range(lines, Doc()[0], Doc()[1]);
        selected.Text.ShouldBe("Hello world");
    }

    [Fact]
    public void Range_Reversed_SelectsSameSpan()
    {
        var lines = TextSelector.OrderReading(Doc());
        var selected = TextSelector.Range(lines, Doc()[1], Doc()[0]);
        selected.Text.ShouldBe("Hello world");
    }

    [Fact]
    public void Range_AcrossLines_JoinsWithNewline()
    {
        var lines = TextSelector.OrderReading(Doc());
        var selected = TextSelector.Range(lines, Doc()[1], Doc()[3]);
        selected.Text.ShouldBe("world\nSecond line");
    }

    [Fact]
    public void Range_SameWord_SelectsOneWord()
    {
        var lines = TextSelector.OrderReading(Doc());
        TextSelector.Range(lines, Doc()[2], Doc()[2]).Text.ShouldBe("Second");
    }

    [Fact]
    public void WordNearest_BesideWord_SnapsToIt()
    {
        // In the gap between "Hello" (ends x=90) and "world" (starts x=95).
        TextSelector.WordNearest(Doc(), 92, 105, maxDistancePt: 15)?.Text.ShouldBe("Hello");
    }

    [Fact]
    public void WordNearest_FarAway_ReturnsNull()
    {
        TextSelector.WordNearest(Doc(), 500, 500, maxDistancePt: 15).ShouldBeNull();
    }
}
