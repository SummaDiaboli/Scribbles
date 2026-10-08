using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

/// <summary>
/// TDD RED (M3): text boxes (bindable), stroke modes, v2 model migration,
/// PDF content builder ops. Must fail before the types land.
/// </summary>
public sealed class RecognitionModelTests
{
    [Fact]
    public void TextBox_RoundTrips_WithModel()
    {
        var doc = new NoteDocument("h", new[]
        {
            new NotePage(1, 595, 842,
                new[] { new InkStroke("s", 1, ToolKind.Pen, 0xFF000000, 2.0,
                    new[] { new StrokePoint(10, 10), new StrokePoint(50, 50) },
                    Mode: InkHandleMode.Copyable, RecognizedText: "hello") },
                new[]
                {
                    new TextBoxItem { Id = "t1", PageNumber = 1, X = 10, Y = 10, Width = 200, Height = 30, Text = "hello", FontSizePt = 14, ColorArgb = 0xFF000000 },
                }),
        });
        var restored = NoteSerializer.FromJson(NoteSerializer.ToJson(doc));
        restored.ShouldNotBeNull();
        var page = restored!.Pages[0];
        page.Strokes[0].Mode.ShouldBe(InkHandleMode.Copyable);
        page.Strokes[0].RecognizedText.ShouldBe("hello");
        page.TextBoxes.ShouldHaveSingleItem();
        page.TextBoxes[0].Text.ShouldBe("hello");
        page.TextBoxes[0].FontSizePt.ShouldBe(14);
    }

    [Fact]
    public void V1Model_Migrates_TextBoxesEmpty()
    {
        // Real v1 payload shape (no TextBoxes, no Mode/RecognizedText).
        string v1 = """{"Version":1,"Document":{"SourceHash":"h","Pages":[{"Number":1,"WidthPt":595,"HeightPt":842,"Strokes":[{"Id":"a","PageNumber":1,"Tool":0,"ColorArgb":4278190080,"WidthPt":2,"Points":[{"X":1,"Y":2,"Pressure":0.5}]}]}]}}""";
        var restored = NoteSerializer.FromJson(v1);
        restored.ShouldNotBeNull();
        restored!.Pages[0].Strokes[0].Mode.ShouldBe(InkHandleMode.Keep);
        restored.Pages[0].Strokes[0].RecognizedText.ShouldBe("");
        restored.Pages[0].TextBoxes.ShouldBeEmpty();
    }

    [Fact]
    public void Builder_FlattenedStroke_EmitsPathOps()
    {
        string ops = PdfContentBuilder.FlattenStroke(
            new[] { new StrokePoint(10, 20), new StrokePoint(30, 40) }, 500, 2.0, 0, 0, 0);
        ops.ShouldContain("10 480 m");
        ops.ShouldContain("30 460 l");
        ops.ShouldContain("2 w");
        ops.ShouldContain("\nS\n");
        ops.TrimEnd().ShouldEndWith("Q");
    }

    [Fact]
    public void Builder_InvisibleText_UsesRenderMode3()
    {
        string ops = PdfContentBuilder.InvisibleText("hello", 10, 100, 12, 500);
        ops.ShouldContain("3 Tr");
        ops.ShouldContain("(hello)");
    }

    [Fact]
    public void Builder_VisibleText_EmitsShowOp()
    {
        string ops = PdfContentBuilder.VisibleText("Hi!", 10, 100, 14, 500);
        ops.ShouldContain("(Hi!) Tj");
        ops.ShouldNotContain("3 Tr");
    }

    [Fact]
    public void Builder_EscapesPdfStrings()
    {
        string ops = PdfContentBuilder.VisibleText("a(b)\\c" + "\0e\u00e9", 0, 100, 12, 500);
        ops.ShouldContain(@"a\(b\)\\c?e?");
    }

    [Fact]
    public void Builder_CoverRect_EmitsFill()
    {
        string ops = PdfContentBuilder.CoverRect(10, 490, 100, 20, 500);
        ops.ShouldContain("re");
        ops.ShouldContain("\nf\n");
        ops.TrimEnd().ShouldEndWith("Q");
    }
}
