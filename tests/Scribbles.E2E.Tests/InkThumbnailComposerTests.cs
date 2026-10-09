using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scribbles.App.Services;
using Scribbles.Core;
using Shouldly;
using static Scribbles.E2E.Tests.WpfTestHost;

namespace Scribbles.E2E.Tests;

/// <summary>
/// Ink thumbnail composer proof (own STA thread): a red stroke baked onto a
/// white raster must leave red pixels; an empty stroke list leaves none.
/// </summary>
public sealed class InkThumbnailComposerTests
{
    private static BitmapSource WhiteRaster(int w = 120, int h = 160)
    {
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var visual = new System.Windows.Media.DrawingVisual();
        using (var ctx = visual.RenderOpen())
            ctx.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, w, h));
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static int CountNear(ImageSource bmp, byte r, byte g, byte b, int tolerance)
    {
        var bs = (BitmapSource)bmp;
        int w = bs.PixelWidth, h = bs.PixelHeight;
        byte[] pixels = new byte[w * h * 4];
        bs.CopyPixels(pixels, w * 4, 0);
        int count = 0;
        for (int i = 0; i < w * h; i++)
        {
            if (Math.Abs(pixels[i * 4 + 2] - r) <= tolerance
                && Math.Abs(pixels[i * 4 + 1] - g) <= tolerance
                && Math.Abs(pixels[i * 4] - b) <= tolerance)
                count++;
        }
        return count;
    }

    [Fact]
    public void Compose_BakesRedStroke_OntoThumb() => Sta(() =>
    {
        var stroke = new InkStroke(
            "s1", 1, ToolKind.Pen, 0xFFFF0000u, 6.0,
            new[] { new StrokePoint(10, 10), new StrokePoint(200, 300) });
        var thumb = InkThumbnailComposer.Compose(WhiteRaster(), 612, 792, new[] { stroke });
        CountNear(thumb, 255, 0, 0, 60).ShouldBeGreaterThan(20);
    });

    [Fact]
    public void Compose_NoStrokes_LeavesWhite() => Sta(() =>
    {
        var thumb = InkThumbnailComposer.Compose(
            WhiteRaster(), 612, 792, Array.Empty<InkStroke>());
        CountNear(thumb, 255, 0, 0, 60).ShouldBe(0);
    });

    [Fact]
    public void Compose_MagnifierWidth_RendersNear600() => Sta(() =>
    {
        // The hover preview shows at 300px: composing at 600 means text
        // downscales (sharp) instead of stretching the 168px thumb (mush).
        var preview = InkThumbnailComposer.Compose(
            WhiteRaster(), 612, 792, Array.Empty<InkStroke>(), thumbWidth: 600);
        preview.PixelWidth.ShouldBe(600);
        preview.PixelHeight.ShouldBeGreaterThan(700);
    });
}
