using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Models;
using Scribbles.Pdf;

namespace Scribbles.App.Services;

/// <summary>
/// M1 PDF renderer: PDFium via Docnet.Core for raster, PdfPig (via
/// Scribbles.Pdf) for true page sizes. Returns one image per page.
/// </summary>
public sealed class DocnetPdfRenderer
{
    public sealed record RenderedPage(
        int Number, double WidthPt, double HeightPt, BitmapSource Image,
        IReadOnlyList<Core.PdfWord> Words);

    public IReadOnlyList<RenderedPage> Render(string filePath, double renderScale = 2.0) =>
        RenderBytes(File.ReadAllBytes(filePath), renderScale);

    public IReadOnlyList<RenderedPage> RenderBytes(byte[] bytes, double renderScale = 2.0)
    {
        // True page sizes in points (MediaBox), for the Core coordinate transforms.
        var sizes = PdfPageSizes.Read(bytes);
        var words = PdfTextExtractor.ExtractPages(bytes);

        int targetDim = (int)(1080 * renderScale);
        using var docReader = DocLib.Instance.GetDocReader(bytes, new PageDimensions(targetDim, targetDim));
        int count = docReader.GetPageCount();
        var pages = new List<RenderedPage>(count);

        for (int i = 0; i < count; i++)
            pages.Add(RenderOne(docReader, sizes, words, i, renderScale));

        return pages;
    }

    /// <summary>Re-renders a single 1-based page (used after true text edits).</summary>
    public RenderedPage RenderSinglePage(byte[] bytes, int pageNumber1, double renderScale = 2.0)
    {
        var sizes = PdfPageSizes.Read(bytes);
        var words = PdfTextExtractor.ExtractPages(bytes);
        int targetDim = (int)(1080 * renderScale);
        using var docReader = DocLib.Instance.GetDocReader(bytes, new PageDimensions(targetDim, targetDim));
        return RenderOne(docReader, sizes, words, pageNumber1 - 1, renderScale);
    }

    private static RenderedPage RenderOne(
        Docnet.Core.Readers.IDocReader docReader,
        IReadOnlyList<PdfPageSize> sizes,
        IReadOnlyList<IReadOnlyList<Core.PdfWord>> words,
        int index0, double renderScale)
    {
        using var pageReader = docReader.GetPageReader(index0);
        byte[] raw = pageReader.GetImage(); // BGRA32
        int w = pageReader.GetPageWidth();
        int h = pageReader.GetPageHeight();

        var bitmap = BitmapSource.Create(w, h, 96 * renderScale, 96 * renderScale,
            PixelFormats.Bgra32, null, raw, w * 4);
        bitmap.Freeze();

        double wPt = index0 < sizes.Count ? sizes[index0].Width : w * 72.0 / (96 * renderScale);
        double hPt = index0 < sizes.Count ? sizes[index0].Height : h * 72.0 / (96 * renderScale);
        var pageWords = index0 < words.Count ? words[index0] : Array.Empty<Core.PdfWord>();
        return new RenderedPage(index0 + 1, wPt, hPt, bitmap, pageWords);
    }
}
