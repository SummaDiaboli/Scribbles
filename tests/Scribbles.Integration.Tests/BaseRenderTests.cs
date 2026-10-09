using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Scribbles.Core;
using Scribbles.Pdf;
using Shouldly;

namespace Scribbles.Integration.Tests;

/// <summary>
/// M4 cleanup proof: the viewer renders base pages WITHOUT annotations
/// (default Docnet flags), so saved /Ink never double-draws under our ink.
/// Our InkCanvas is the single ink source on screen.
/// </summary>
public sealed class BaseRenderTests
{
    private static int OpaqueDarkPixels(byte[] pdf, Docnet.Core.Models.RenderFlags flags)
    {
        using var reader = Docnet.Core.DocLib.Instance.GetDocReader(
            pdf, new Docnet.Core.Models.PageDimensions(800, 800));
        using var page = reader.GetPageReader(0);
        byte[] bgra = page.GetImage(flags);
        int dark = 0;
        for (int i = 0; i < page.GetPageWidth() * page.GetPageHeight(); i++)
        {
            if (bgra[i * 4 + 3] >= 200
                && bgra[i * 4] + bgra[i * 4 + 1] + bgra[i * 4 + 2] < 500)
                dark++;
        }
        return dark;
    }

    [Fact]
    public void SavedAnnotations_HiddenFromBaseRender()
    {
        using var doc = new PdfDocument();
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        using (var gfx = XGraphics.FromPdfPage(page))
            gfx.DrawLine(XPens.LightGray, 50, 100, 300, 100);
        using var ms = new MemoryStream();
        doc.Save(ms);
        byte[] original = ms.ToArray();

        var model = new NoteDocument("", new[]
        {
            new NotePage(1, 595, 842,
                new[] { new InkStroke("s", 1, ToolKind.Pen, 0xFF000000, 8.0,
                    Enumerable.Range(0, 30).Select(i =>
                        new StrokePoint(50 + i * 10, 400)).ToList()) },
                Array.Empty<TextBoxItem>()),
        });
        byte[] saved = new ScribblesPdfStore().Save(original, model);

        int base_ = OpaqueDarkPixels(original, 0);
        int savedBase = OpaqueDarkPixels(saved, 0);
        savedBase.ShouldBe(base_, "embedded /Ink must stay out of the base render");

        int withAnnots = OpaqueDarkPixels(saved, Docnet.Core.Models.RenderFlags.RenderAnnotations);
        withAnnots.ShouldBeGreaterThan(base_, "sanity: annotations do render when asked");
    }
}
