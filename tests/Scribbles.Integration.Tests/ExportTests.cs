using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Scribbles.Core;
using Scribbles.Pdf;
using Shouldly;
using UglyToad.PdfPig;

namespace Scribbles.Integration.Tests;

/// <summary>M3 export: flattened ink, visible converted text, invisible
/// copyable text, no annotations, no PieceInfo.</summary>
public sealed class ExportTests
{
    private static byte[] CreateBasePdf()
    {
        using var doc = new PdfSharp.Pdf.PdfDocument();
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawLine(XPens.LightGray, 50, 100, 300, 100);
        using var stream = new MemoryStream();
        doc.Save(stream);
        return stream.ToArray();
    }

    private static NoteDocument ExportModel() => new("", new[]
    {
        new NotePage(1, 595, 842,
            new[]
            {
                new InkStroke("kept", 1, ToolKind.Pen, 0xFF000000, 3.0,
                    new[] { new StrokePoint(50, 400), new StrokePoint(200, 400), new StrokePoint(350, 450) }),
                new InkStroke("copied", 1, ToolKind.Pen, 0xFF000000, 2.0,
                    new[] { new StrokePoint(50, 500), new StrokePoint(250, 500) },
                    Mode: InkHandleMode.Copyable, RecognizedText: "copyable words here"),
            },
            new[]
            {
                new TextBoxItem { Id = "t1", PageNumber = 1, X = 50, Y = 600, Width = 300, Height = 30, Text = "Converted hello", FontSizePt = 16, ColorArgb = 0xFF000000, Kind = TextBoxKind.Converted },
                new TextBoxItem { Id = "t2", PageNumber = 1, X = 50, Y = 650, Width = 300, Height = 30, Text = "Edited native", FontSizePt = 14, ColorArgb = 0xFF000000, Kind = TextBoxKind.EditedNative },
            }),
    });

    private static string ExtractAllText(byte[] pdf)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return string.Join(" ", document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
    }

    [Fact]
    public void Export_ContainsConvertedAndCopyableText()
    {
        var store = new ScribblesPdfStore();
        byte[] exported = store.ExportFlat(CreateBasePdf(), ExportModel());
        string text = ExtractAllText(exported);
        text.ShouldContain("Converted hello");
        text.ShouldContain("Edited native");
        text.ShouldContain("copyable words here");
    }

    [Fact]
    public void Export_HasNoInkAnnotationsOrPieceInfo()
    {
        var store = new ScribblesPdfStore();
        byte[] exported = store.ExportFlat(CreateBasePdf(), ExportModel());
        using var stream = new MemoryStream(exported);
        using var doc = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        var page = doc.Pages[0];
        for (int i = 0; i < page.Annotations.Count; i++)
            (page.Annotations[i].Elements["/Subtype"] as PdfSharp.Pdf.PdfName)?.Value
                .ShouldNotBe("/Ink");
        if (page.Elements["/PieceInfo"] is PdfSharp.Pdf.PdfDictionary piece)
            (piece.Elements["/Scribbles"] is null).ShouldBeTrue();
    }

    [Fact]
    public void Export_FlattenedInkRendersWithoutAnnotations()
    {
        var store = new ScribblesPdfStore();
        byte[] original = CreateBasePdf();
        byte[] exported = store.ExportFlat(original, ExportModel());

        static int DarkPixels(byte[] pdf)
        {
            using var reader = Docnet.Core.DocLib.Instance.GetDocReader(
                pdf, new Docnet.Core.Models.PageDimensions(800, 800));
            using var page = reader.GetPageReader(0);
            byte[] bgra = page.GetImage(); // NOTE: no annotation flag
            int dark = 0;
            for (int i = 0; i < page.GetPageWidth() * page.GetPageHeight(); i++)
            {
                // Transparent background reads as (0,0,0,0): only opaque pixels count.
                if (bgra[i * 4 + 3] >= 200
                    && bgra[i * 4] + bgra[i * 4 + 1] + bgra[i * 4 + 2] < 500)
                    dark++;
            }
            return dark;
        }

        DarkPixels(exported).ShouldBeGreaterThan(DarkPixels(original) + 100);
    }
}
