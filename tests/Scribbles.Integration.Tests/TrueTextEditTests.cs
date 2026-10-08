using System.Text;
using Scribbles.Pdf;
using Shouldly;
using UglyToad.PdfPig;

namespace Scribbles.Integration.Tests;

/// <summary>M3 true text-layer editing: parse, match, splice, rewrite.</summary>
public sealed class TrueTextEditTests
{
    private static string FixturePath() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "sample-text.pdf"));

    private static byte[] InlinePdf(params string[] contentOps)
    {
        string content = "BT /F1 14 Tf 50 700 Td\n" + string.Join("\n", contentOps) + "\nET";
        var objs = new[]
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            "<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Resources<</Font<</F1 4 0 R>>>>/Contents 5 0 R>>",
            "<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>",
            $"<</Length {Encoding.ASCII.GetByteCount(content)}>>\nstream\n{content}\nendstream",
        };
        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objs.Length; i++)
        {
            offsets.Add(sb.Length);
            sb.Append($"{i + 1} 0 obj\n{objs[i]}\nendobj\n");
        }
        int xref = sb.Length;
        sb.Append($"xref\n0 {objs.Length + 1}\n0000000000 65535 f \n");
        foreach (int offset in offsets)
            sb.Append($"{offset:D10} 00000 n \n");
        sb.Append($"trailer\n<</Size {objs.Length + 1}/Root 1 0 R>>\nstartxref\n{xref}\n%%EOF");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static string ExtractWords(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join(" ", document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
    }

    [Fact]
    public void NoopRewrite_PreservesText()
    {
        byte[] original = File.ReadAllBytes(FixturePath());
        string before = ExtractWords(original);
        // Same-line rewrite exercises the full parse/mutate/write cycle.
        TrueTextEdit.TryReplaceLine(original, 1,
            "Pack my box with five dozen", "Pack my box with five dozen", out byte[] rewritten)
            .ShouldBeTrue();
        ExtractWords(rewritten).ShouldBe(before);
    }

    [Fact]
    public void ReplaceWord_KeepsPosition_AndRest()
    {
        byte[] original = File.ReadAllBytes(FixturePath());
        using var before = PdfDocument.Open(original);
        double beforeX = before.GetPage(1).GetWords().First(w => w.Text == "brown").BoundingBox.BottomLeft.X;

        TrueTextEdit.TryReplaceLine(original, 1, "brown", "green", out byte[] updated).ShouldBeTrue();
        string text = ExtractAllText(updated);
        text.ShouldContain("green");
        text.ShouldNotContain("brown");
        text.ShouldContain("the lazy dog");

        using var after = PdfDocument.Open(updated);
        double afterX = after.GetPage(1).GetWords().First(w => w.Text == "green").BoundingBox.BottomLeft.X;
        afterX.ShouldBe(beforeX, tolerance: 0.01);
    }

    [Fact]
    public void ReplaceWholeLine()
    {
        byte[] original = File.ReadAllBytes(FixturePath());
        TrueTextEdit.TryReplaceLine(original, 1,
            "liquor jugs for the long trip", "soda cans for the short stroll", out byte[] updated)
            .ShouldBeTrue();
        string text = ExtractAllText(updated);
        text.ShouldContain("soda cans for the short stroll");
        text.ShouldNotContain("liquor");
    }

    [Fact]
    public void AmbiguousWord_RefusesSafely()
    {
        byte[] pdf = InlinePdf("(repeat after me) Tj", "0 -24 Td", "(repeat after me) Tj");
        TrueTextEdit.TryReplaceLine(pdf, 1, "repeat", "recite", out byte[] updated).ShouldBeFalse();
        updated.ShouldBe(pdf);
    }

    [Fact]
    public void KernSplitWord_ReplacedAcrossNodes()
    {
        byte[] pdf = InlinePdf("[(Hel) -20 (lo)] TJ");
        TrueTextEdit.TryReplaceLine(pdf, 1, "Hello", "Jello", out byte[] updated).ShouldBeTrue();
        ExtractAllText(updated).ShouldContain("Jello");
    }

    [Fact]
    public void MissingWord_ReturnsFalse()
    {
        byte[] original = File.ReadAllBytes(FixturePath());
        TrueTextEdit.TryReplaceLine(original, 1, "xyzzy", "hello", out _).ShouldBeFalse();
    }

    private static string ExtractAllText(byte[] pdf) => ExtractWords(pdf);
}
