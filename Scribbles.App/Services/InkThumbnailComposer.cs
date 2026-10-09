using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scribbles.Core;

namespace Scribbles.App.Services;

/// <summary>
/// Bakes ink strokes onto a page raster for thumbnails (home grid, sidebar).
/// Thumbnails otherwise show base pages only, hiding all handwriting.
/// </summary>
public static class InkThumbnailComposer
{
    /// <param name="strokes">Model strokes in PDF points (y-down).</param>
    public static BitmapSource Compose(
        ImageSource baseImage, double pageWidthPt, double pageHeightPt,
        IReadOnlyList<InkStroke> strokes, double thumbWidth = 168)
    {
        double nominalDipW = pageWidthPt * InkUnits.DipsPerPoint;
        double nominalDipH = pageHeightPt * InkUnits.DipsPerPoint;
        double scale = nominalDipW > 0 ? thumbWidth / nominalDipW : 1.0;
        int thumbW = Math.Max(1, (int)(nominalDipW * scale));
        int thumbH = Math.Max(1, (int)(nominalDipH * scale));

        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            ctx.DrawImage(baseImage, new Rect(0, 0, thumbW, thumbH));
            foreach (var stroke in strokes)
            {
                if (stroke.Points.Count == 0)
                    continue;
                var geo = new StreamGeometry();
                using (var path = geo.Open())
                {
                    bool first = true;
                    foreach (var p in stroke.Points)
                    {
                        var pt = new Point(p.X * InkUnits.DipsPerPoint * scale, p.Y * InkUnits.DipsPerPoint * scale);
                        if (first)
                            path.BeginFigure(pt, false, false);
                        else
                            path.LineTo(pt, true, false);
                        first = false;
                    }
                }
                geo.Freeze();
                var (a, r, g, b) = InkUnits.FromArgb(stroke.ColorArgb);
                if (stroke.Tool == ToolKind.Highlighter)
                    a = (byte)(a * 0.5);
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(a, r, g, b)),
                    Math.Max(0.75, stroke.WidthPt * InkUnits.DipsPerPoint * scale));
                pen.Freeze();
                ctx.DrawGeometry(null, pen, geo);
            }
        }
        var thumb = new RenderTargetBitmap(thumbW, thumbH, 96, 96, PixelFormats.Pbgra32);
        thumb.Render(visual);
        thumb.Freeze();
        return thumb;
    }
}
