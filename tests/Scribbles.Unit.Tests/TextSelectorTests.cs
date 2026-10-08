using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

/// <summary>
/// TDD RED (2026-10-08): native PDF text marquee selection.
/// Types don't exist yet — must fail before TextSelector lands.
/// </summary>
public sealed class TextSelectorTests
{
    private static IReadOnlyList<PdfWord> TwoLines() => new[]
    {
        new PdfWord("Hello", 50, 100, 40, 14),
        new PdfWord("world", 95, 100, 42, 14),
        new PdfWord("Second", 50, 130, 48, 14),
        new PdfWord("line", 103, 130, 30, 14),
    };

    [Fact]
    public void MarqueeOverFirstLine_SelectsBothWordsInOrder()
    {
        var selected = TextSelector.Select(TwoLines(), new SelectionRect(40, 90, 150, 30));
        selected.Words.Select(w => w.Text).ShouldBe(new[] { "Hello", "world" });
        selected.Text.ShouldBe("Hello world");
    }

    [Fact]
    public void MarqueeOverBothLines_JoinsLinesWithNewline()
    {
        var selected = TextSelector.Select(TwoLines(), new SelectionRect(40, 90, 150, 70));
        selected.Text.ShouldBe("Hello world\nSecond line");
    }

    [Fact]
    public void MarqueeMissingEverything_SelectsNothing()
    {
        var selected = TextSelector.Select(TwoLines(), new SelectionRect(500, 500, 10, 10));
        selected.Words.ShouldBeEmpty();
        selected.Text.ShouldBe("");
    }

    [Fact]
    public void PartialOverlap_SelectsWord()
    {
        // Marquee clips the second word: any overlap counts.
        var selected = TextSelector.Select(TwoLines(), new SelectionRect(40, 90, 60, 30));
        selected.Words.Select(w => w.Text).ShouldBe(new[] { "Hello", "world" });
    }

    [Fact]
    public void EmptyWords_SelectsNothing()
    {
        TextSelector.Select(Array.Empty<PdfWord>(), new SelectionRect(0, 0, 100, 100))
            .Words.ShouldBeEmpty();
    }
}
