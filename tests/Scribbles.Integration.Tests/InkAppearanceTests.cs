using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Scribbles.Core;
using Scribbles.Pdf;
using Shouldly;

namespace Scribbles.Integration.Tests;

/// <summary>
/// Verifies saved ink carries a painted /AP appearance (Form XObject with
/// the stroke path), so every viewer renders it — proven visually via an
/// independent renderer (PyMuPDF) during M2.
/// NOTE: Docnet.Core 2.6.0's RenderAnnotations flag does not paint ink
/// annotations, so this asserts structure, not pixels.
/// </summary>
public sealed class InkAppearanceTests
{
    private static byte[] CreateOnePagePdf()
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawLine(XPens.LightGray, 50, 100, 300, 100);
        using var stream = new MemoryStream();
        doc.Save(stream);
        return stream.ToArray();
    }

    [Fact]
    public void SavedInk_HasPaintedAppearance()
    {
        var store = new ScribblesPdfStore();
        var model = new NoteDocument("", new[]
        {
            new NotePage(1, 595, 842, new[]
            {
                new InkStroke("big", 1, ToolKind.Pen, 0xFF000000, 4.0,
                    Enumerable.Range(0, 50).Select(i =>
                        new StrokePoint(50 + i * 8, 400 + 100 * Math.Sin(i * 0.3))).ToList()),
            }, Array.Empty<TextBoxItem>()),
        });

        byte[] saved = store.Save(CreateOnePagePdf(), model);

        using var stream = new MemoryStream(saved);
        using var doc = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        var page = doc.Pages[0];
        page.Annotations.Count.ShouldBe(1);
        var annot = page.Annotations[0];
        (annot.Elements["/Subtype"] as PdfName)!.Value.ShouldBe("/Ink");
        var inkList = (annot.Elements["/InkList"] as PdfArray)!;
        ((PdfArray)inkList.Elements[0]).Elements.Count.ShouldBe(100); // 50 pts x/y

        var ap = (annot.Elements["/AP"] as PdfDictionary)!;
        ap.ShouldNotBeNull();
        var normal = ap.Elements["/N"];
        normal.ShouldNotBeNull();
        var form = (normal as PdfReference)?.Value as PdfDictionary ?? normal as PdfDictionary;
        form.ShouldNotBeNull();
        string content = System.Text.Encoding.ASCII.GetString(form!.Stream!.Value!);
        content.ShouldContain("RG");
        content.ShouldContain(" m\n");
        content.ShouldContain(" l\n");
        content.TrimEnd().ShouldEndWith("Q");
    }
}
