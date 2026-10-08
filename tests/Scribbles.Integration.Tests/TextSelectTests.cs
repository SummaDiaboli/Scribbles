using System.Text;
using Scribbles.Core;
using Scribbles.Pdf;
using Shouldly;

namespace Scribbles.Integration.Tests;

/// <summary>
/// End-to-end text selection against a hand-built PDF with known text
/// (xref offsets computed, so no font files needed).
/// </summary>
public sealed class TextSelectTests
{
    internal static byte[] BuildTextPdf()
    {
        string content = "BT /F1 24 Tf 100 700 Td (Hello world) Tj ET";
        var objects = new[]
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Resources<</Font<</F1 4 0 R>>>>/Contents 5 0 R>>",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
            $"<</Length {Encoding.ASCII.GetByteCount(content)}>>\nstream\n{content}\nendstream",
        };
        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(sb.Length);
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        int xref = sb.Length;
        sb.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
            sb.Append($"{offset:D10} 00000 n \n");
        sb.Append($"trailer\n<</Size {objects.Length + 1}/Root 1 0 R>>\nstartxref\n{xref}\n%%EOF");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    [Fact]
    public void Extractor_FindsKnownWords()
    {
        var pages = PdfTextExtractor.ExtractPages(BuildTextPdf());
        pages.Count.ShouldBe(1);
        pages[0].Select(w => w.Text).ShouldBe(new[] { "Hello", "world" });
    }

    [Fact]
    public void MarqueeOverExtractedWords_CopiesInOrder()
    {
        var pages = PdfTextExtractor.ExtractPages(BuildTextPdf());
        // "Hello world" sits near y=700 (y-up) → y-down ≈ 92±24 at x=100+.
        var selected = TextSelector.Select(pages[0], new SelectionRect(50, 50, 300, 100));
        selected.Text.ShouldBe("Hello world");
    }

    [Fact]
    public void MarqueeElsewhere_SelectsNothing()
    {
        var pages = PdfTextExtractor.ExtractPages(BuildTextPdf());
        TextSelector.Select(pages[0], new SelectionRect(50, 400, 300, 100))
            .Words.ShouldBeEmpty();
    }
}
