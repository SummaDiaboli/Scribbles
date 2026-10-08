using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Scribbles.Core;
using Scribbles.Pdf;
using Shouldly;

namespace Scribbles.Integration.Tests;

public sealed class RoundTripTests
{
    private static byte[] CreateTwoPagePdf()
    {
        using var doc = new PdfDocument();
        for (int i = 0; i < 2; i++)
        {
            var page = doc.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawLine(XPens.Black, 50, 100, 300, 100 + i * 20);
            gfx.DrawRectangle(XPens.DarkGray, 50, 150, 200, 80);
        }
        using var stream = new MemoryStream();
        doc.Save(stream);
        return stream.ToArray();
    }

    private static NoteDocument SampleModel() => new("", new[]
    {
        new NotePage(1, 595, 842, new[]
        {
            new InkStroke("stroke-1", 1, ToolKind.Pen, 0xFF112233, 2.5,
                new[] { new StrokePoint(100, 200, 0.8f), new StrokePoint(150, 220, 0.4f), new StrokePoint(200, 210, 0.6f) }),
            new InkStroke("stroke-2", 1, ToolKind.Highlighter, 0x80FFEE00, 8.0,
                new[] { new StrokePoint(50, 50), new StrokePoint(300, 60) }),
        }),
        new NotePage(2, 595, 842, Array.Empty<InkStroke>()),
    });

    [Fact]
    public void SaveThenLoad_GivesIdenticalModel()
    {
        var store = new ScribblesPdfStore();
        byte[] original = CreateTwoPagePdf();

        byte[] saved = store.Save(original, SampleModel());
        var loaded = store.Load(saved);

        loaded.HashesMatch.ShouldBeTrue();
        loaded.Model.ShouldNotBeNull();
        // Identity = same JSON (ids, points, colors, widths preserved).
        NoteSerializer.ToJson(loaded.Model!) .ShouldBe(NoteSerializer.ToJson(
            SampleModel() with { SourceHash = loaded.Model!.SourceHash }));
        loaded.Model!.Pages[0].Strokes.Count.ShouldBe(2);
        // PieceInfo path preserves pressure exactly (fallback path does not).
        loaded.Model.Pages[0].Strokes[0].Points[0].ShouldBe(new StrokePoint(100, 200, 0.8f));
        loaded.FallbackInk.ShouldBeEmpty();
    }

    [Fact]
    public void Resave_ReplacesOldInk_DoesNotDuplicate()
    {
        var store = new ScribblesPdfStore();
        byte[] saved = store.Save(CreateTwoPagePdf(), SampleModel());
        byte[] resaved = store.Save(saved, SampleModel());
        var loaded = store.Load(resaved);
        loaded.HashesMatch.ShouldBeTrue();
        loaded.Model!.Pages[0].Strokes.Count.ShouldBe(2);
    }

    [Fact]
    public void ForeignEdit_TriggersFallbackInkImport()
    {
        var store = new ScribblesPdfStore();
        byte[] saved = store.Save(CreateTwoPagePdf(), SampleModel());

        // Another tool edits the base content (hash changes, model unusable).
        using var stream = new MemoryStream(saved);
        using var doc = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
        using var gfx = XGraphics.FromPdfPage(doc.Pages[0]);
        gfx.DrawLine(XPens.Red, 10, 10, 400, 700);
        using var tampered = new MemoryStream();
        doc.Save(tampered);

        var loaded = store.Load(tampered.ToArray());
        loaded.HashesMatch.ShouldBeFalse();
        loaded.Model.ShouldBeNull();
        loaded.FallbackInk.Count.ShouldBe(2);
        loaded.FallbackInk[0].Points[0].X.ShouldBe(100, tolerance: 0.01);
        loaded.FallbackInk[0].Points[0].Y.ShouldBe(200, tolerance: 0.01);
    }

    [Fact]
    public void ForeignPdf_WithInkAnnotations_ImportsThem()
    {
        var store = new ScribblesPdfStore();
        byte[] saved = store.Save(CreateTwoPagePdf(), SampleModel());

        // Strip our PieceInfo -> looks like a foreign PDF with /Ink annots.
        using var stream = new MemoryStream(saved);
        using var doc = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
        doc.Pages[0].Elements.Remove("/PieceInfo");
        doc.Pages[1].Elements.Remove("/PieceInfo");
        using var stripped = new MemoryStream();
        doc.Save(stripped);

        var loaded = store.Load(stripped.ToArray());
        loaded.HashesMatch.ShouldBeFalse();
        loaded.FallbackInk.Count.ShouldBe(2);
        loaded.FallbackInk[0].ColorArgb.ShouldBe(0xFF112233);
    }
}
