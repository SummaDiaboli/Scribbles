using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

/// <summary>
/// TDD RED (2026-10-08): group raw glyphs into words when a PDF exposes
/// letters but no words ( subset fonts, odd encodings).
/// Types don't exist yet — must fail before LetterGrouping lands.
/// </summary>
public sealed class LetterGroupingTests
{
    private static TextGlyph G(string text, double x, double y = 100, double w = 10, double h = 14) =>
        new(text, x, y, w, h);

    [Fact]
    public void AdjacentGlyphs_FormOneWord()
    {
        var words = LetterGrouping.Group(new[] { G("Hel", 50), G("lo", 61), });
        words.Select(w => w.Text).ShouldBe(new[] { "Hello" });
    }

    [Fact]
    public void WideGap_SplitsWords()
    {
        var words = LetterGrouping.Group(new[] { G("Hi", 50, w: 20), G("there", 120, w: 40) });
        words.Select(w => w.Text).ShouldBe(new[] { "Hi", "there" });
    }

    [Fact]
    public void TwoRows_GroupSeparately()
    {
        var words = LetterGrouping.Group(new[]
        {
            G("top", 50, y: 100, w: 30),
            G("bottom", 50, y: 140, w: 50),
        });
        words.Select(w => w.Text).ShouldBe(new[] { "top", "bottom" });
    }

    [Fact]
    public void EmptyGlyphs_YieldNoWords()
    {
        LetterGrouping.Group(Array.Empty<TextGlyph>()).ShouldBeEmpty();
    }

    [Fact]
    public void GroupedWords_CarryTightBounds()
    {
        var words = LetterGrouping.Group(new[] { G("A", 50, y: 100, w: 12, h: 14), G("B", 62, y: 100, w: 12, h: 14) });
        var word = words.ShouldHaveSingleItem();
        word.X.ShouldBe(50);
        word.Y.ShouldBe(100);
        word.Width.ShouldBe(24);
        word.Height.ShouldBe(14);
    }
}
