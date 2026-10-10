using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

public sealed class PdfTextSearchTests
{
    private static readonly PdfWord Fox = new("fox", 10, 20, 30, 12);
    private static readonly PdfWord Box = new("Box", 50, 20, 30, 12);
    private static readonly PdfWord Dog = new("dog", 90, 20, 30, 12);

    [Fact]
    public void Find_MatchesSubstring_CaseInsensitive()
    {
        var words = new[] { Fox, Box, Dog };
        PdfTextSearch.Find(words, "ox").ShouldBe(new[] { Fox, Box });
        PdfTextSearch.Find(words, "FOX").ShouldBe(new[] { Fox });
    }

    [Fact]
    public void Find_BlankQuery_MatchesNothing()
    {
        var words = new[] { Fox, Dog };
        PdfTextSearch.Find(words, "").ShouldBeEmpty();
        PdfTextSearch.Find(words, "   ").ShouldBeEmpty();
    }

    [Fact]
    public void Find_NoMatch_IsEmpty()
    {
        var words = new[] { Fox, Dog };
        PdfTextSearch.Find(words, "zebra").ShouldBeEmpty();
    }
}
