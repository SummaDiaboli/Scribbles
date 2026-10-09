using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Scribbles.Core;
using Scribbles.Pdf;
using Shouldly;

namespace Scribbles.Integration.Tests;

/// <summary>M4 golden renders: export output must stay pixel-stable.</summary>
public sealed class GoldenExportTests
{
    [Fact]
    public void ExportBasic_MatchesGolden()
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawLine(XPens.LightGray, 50, 100, 300, 100);
        using var ms = new MemoryStream();
        doc.Save(ms);

        var model = new NoteDocument("", new[]
        {
            new NotePage(1, 595, 842,
                new[]
                {
                    new InkStroke("wave", 1, ToolKind.Pen, 0xFF000000, 4.0,
                        Enumerable.Range(0, 50).Select(i =>
                            new StrokePoint(50 + i * 8, 400 + 100 * Math.Sin(i * 0.3))).ToList()),
                },
                new[]
                {
                    new TextBoxItem { Id = "t", PageNumber = 1, X = 50, Y = 600, Width = 300, Height = 30, Text = "Golden hello", FontSizePt = 16, ColorArgb = 0xFF000000, Kind = TextBoxKind.Converted },
                }),
        });

        byte[] exported = new ScribblesPdfStore().ExportFlat(ms.ToArray(), model);
        GoldenImages.AssertMatches("export-basic", GoldenImages.RenderFirstPage(exported));
    }
}
