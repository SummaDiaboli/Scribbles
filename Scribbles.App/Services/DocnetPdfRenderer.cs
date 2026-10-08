using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Models;
using UglyToad.PdfPig;

namespace Scribbles.App.Services;

/// <summary>
/// M1 PDF renderer: PDFium via Docnet.Core for raster, PdfPig for true page sizes.
/// Returns one image per page.
/// </summary>
public sealed class DocnetPdfRenderer
{
    public sealed record RenderedPage(int Number, double WidthPt, double HeightPt, BitmapSource Image);

    public IReadOnlyList<RenderedPage> Render(string filePath, double renderScale = 2.0)
    {
        byte[] bytes = File.ReadAllBytes(filePath);

        // True page sizes in points (MediaBox), for the Core coordinate transforms.
        double[] widths;
        double[] heights;
        using (var pdf = PdfDocument.Open(bytes))
        {
            widths = new double[pdf.NumberOfPages];
            heights = new double[pdf.NumberOfPages];
            for (int i = 1; i <= pdf.NumberOfPages; i++)
            {
                var page = pdf.GetPage(i);
                widths[i - 1] = page.Width;
                heights[i - 1] = page.Height;
            }
        }

        int targetDim = (int)(1080 * renderScale);
        using var docReader = DocLib.Instance.GetDocReader(bytes, new PageDimensions(targetDim, targetDim));
        int count = docReader.GetPageCount();
        var pages = new List<RenderedPage>(count);

        for (int i = 0; i < count; i++)
        {
            using var pageReader = docReader.GetPageReader(i);
            byte[] raw = pageReader.GetImage(); // BGRA32
            int w = pageReader.GetPageWidth();
            int h = pageReader.GetPageHeight();

            var bitmap = BitmapSource.Create(w, h, 96 * renderScale, 96 * renderScale,
                PixelFormats.Bgra32, null, raw, w * 4);
            bitmap.Freeze();

            double wPt = i < widths.Length ? widths[i] : w * 72.0 / (96 * renderScale);
            double hPt = i < heights.Length ? heights[i] : h * 72.0 / (96 * renderScale);
            pages.Add(new RenderedPage(i + 1, wPt, hPt, bitmap));
        }

        return pages;
    }
}
