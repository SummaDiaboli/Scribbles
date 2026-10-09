using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

public sealed class WordSelectionTests
{
    private static readonly PdfWord Fox = new("fox", 10, 20, 30, 12);
    private static readonly PdfWord Dog = new("dog", 60, 20, 30, 12);

    [Fact]
    public void WordSelection_Hit_SelectsExactlyIt()
    {
        var words = new[] { Fox, Dog };
        var selected = TextSelector.WordSelection(words, 15, 25);
        selected.Words.ShouldBe(new[] { Fox });
        selected.Text.ShouldBe("fox");
    }

    [Fact]
    public void WordSelection_NearMiss_SnapsToNearest()
    {
        var words = new[] { Fox, Dog };
        TextSelector.WordSelection(words, 58, 25).Text.ShouldBe("dog");
    }

    [Fact]
    public void WordSelection_FarAway_IsEmpty()
    {
        var words = new[] { Fox, Dog };
        var selected = TextSelector.WordSelection(words, 500, 500);
        selected.Words.ShouldBeEmpty();
        selected.Text.ShouldBe("");
    }

    [Fact]
    public void WordSelection_NoWords_IsEmpty()
    {
        var selected = TextSelector.WordSelection(Array.Empty<PdfWord>(), 10, 10);
        selected.Words.ShouldBeEmpty();
    }
}
