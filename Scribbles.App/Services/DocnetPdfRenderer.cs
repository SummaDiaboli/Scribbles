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

    /// <summary>Re-renders a single 1-based page (used after true text edits).</summary>
    public async Task<RenderedPage> RenderSinglePageAsync(byte[] bytes, int pageNumber1, double renderScale = 2.0)
    {
        var content = await Task.Run(() => PdfPageContentReader.Read(bytes)).ConfigureAwait(false);
        int targetDim = (int)(1080 * renderScale);
        await PdfiumGate.Instance.WaitAsync().ConfigureAwait(false);
        try
        {
            using var docReader = DocLib.Instance.GetDocReader(bytes, new PageDimensions(targetDim, targetDim));
            return RenderOne(docReader, content.Sizes, content.Words, pageNumber1 - 1, renderScale);
        }
        finally
        {
            PdfiumGate.Instance.Release();
        }
    }

    /// <summary>Small first-page raster for home-grid thumbnails.</summary>
    public async Task<BitmapSource?> RenderFirstPageAsync(byte[] bytes, int targetDim = 320)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Sync wait: pool thread, never the UI thread.
                PdfiumGate.Instance.Wait();
                try
                {
                    using var docReader = DocLib.Instance.GetDocReader(bytes, new PageDimensions(targetDim, targetDim));
                    if (docReader.GetPageCount() < 1)
                        return null;
                    using var pageReader = docReader.GetPageReader(0);
                    byte[] raw = pageReader.GetImage();
                    int w = pageReader.GetPageWidth();
                    int h = pageReader.GetPageHeight();
                    if (w <= 0 || h <= 0)
                        return null;
                    var bitmap = BitmapSource.Create(w, h, 96, 96,
                        PixelFormats.Bgra32, null, raw, w * 4);
                    bitmap.Freeze();
                    return bitmap;
                }
                finally
                {
                    PdfiumGate.Instance.Release();
                }
            }
            catch
            {
                return null;
            }
        }).ConfigureAwait(false);
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
