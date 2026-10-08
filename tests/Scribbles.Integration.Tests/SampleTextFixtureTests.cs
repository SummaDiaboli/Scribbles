using Scribbles.Core;
using Scribbles.Pdf;
using Shouldly;

namespace Scribbles.Integration.Tests;

/// <summary>
/// Line-based (not box) selection against the committed sample-text.pdf
/// fixture: anchoring "quick" (line 1) through "lazy" (line 2) must yield
/// the whole reading span, which a thin diagonal box would miss.
/// </summary>
public sealed class SampleTextFixtureTests
{
    private static string FixturePath()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures");
        return Path.GetFullPath(Path.Combine(dir, "sample-text.pdf"));
    }

    [Fact]
    public void Fixture_ExtractsThirtyFiveWords()
    {
        var pages = PdfTextExtractor.ExtractPages(File.ReadAllBytes(FixturePath()));
        pages.Count.ShouldBe(1);
        pages[0].Count.ShouldBe(35);
    }

    [Fact]
    public void AnchorRange_IsLineBased_NotBox()
    {
        var words = PdfTextExtractor.ExtractPages(File.ReadAllBytes(FixturePath()))[0];
        var lines = TextSelector.OrderReading(words);
        var anchor = words.First(w => w.Text == "quick");
        var focus = words.First(w => w.Text == "lazy");
        TextSelector.Range(lines, anchor, focus).Text.ShouldBe(
            "quick brown fox jumps over\nthe lazy");
    }
}
