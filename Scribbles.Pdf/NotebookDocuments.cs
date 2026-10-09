using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Scribbles.Pdf;

/// <summary>
/// Blank notebooks are plain PDFs (white pages), so the whole round-trip,
/// save and export pipeline works unchanged. White fills keep pages bright
/// in every theme.
/// </summary>
public static class NotebookDocuments
{
    public static byte[] CreateBlank(IReadOnlyList<(double WidthPt, double HeightPt)> pages)
    {
        using var doc = new PdfDocument();
        foreach (var (w, h) in pages)
            AddWhitePage(doc, w, h);
        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    public static byte[] AddBlankPage(byte[] pdfBytes, double widthPt, double heightPt)
    {
        using var stream = new MemoryStream(pdfBytes);
        using var doc = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
        AddWhitePage(doc, widthPt, heightPt);
        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    private static void AddWhitePage(PdfDocument doc, double widthPt, double heightPt)
    {
        var page = doc.AddPage();
#pragma warning disable CS0618 // XUnit double conversions are behavior-identical here (points in/out).
        page.Width = widthPt;
        page.Height = heightPt;
        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawRectangle(XBrushes.White, 0, 0, widthPt, heightPt);
#pragma warning restore CS0618
    }
}
