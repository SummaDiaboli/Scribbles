using Scribbles.Pdf;
using Shouldly;

namespace Scribbles.Integration.Tests;

/// <summary>
/// Single-open page read: one PdfPig open yields sizes AND words that agree
/// with the legacy two-open path.
/// </summary>
public sealed class PdfPageContentTests
{
    private static string FixturePath()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures");
        return Path.GetFullPath(Path.Combine(dir, "sample-text.pdf"));
    }

    [Fact]
    public void Read_SizesAndWordsAgree()
    {
        byte[] bytes = File.ReadAllBytes(FixturePath());
        var content = PdfPageContentReader.Read(bytes);
        content.Sizes.Count.ShouldBe(1);
        content.Words.Count.ShouldBe(1);
        content.Words[0].Count.ShouldBe(35);
        content.Sizes[0].Width.ShouldBeGreaterThan(0);
        content.Sizes[0].Height.ShouldBeGreaterThan(0);
        content.Words.ShouldBe(PdfTextExtractor.ExtractPages(bytes));
        content.Sizes.ShouldBe(PdfPageSizes.Read(bytes));
    }

    [Fact]
    public void Read_Garbage_ReturnsEmpty()
    {
        var content = PdfPageContentReader.Read(new byte[] { 1, 2, 3 });
        content.Sizes.ShouldBeEmpty();
        content.Words.ShouldBeEmpty();
    }
}
