using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scribbles.App.Services;
using Shouldly;
using static Scribbles.E2E.Tests.WpfTestHost;

namespace Scribbles.E2E.Tests;

/// <summary>Page raster disk cache: round-trip, miss, corruption, pruning.</summary>
public sealed class PageRasterCacheTests
{
    private static string NewDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "scribbles-cache-" + Guid.NewGuid().ToString("N"));
        return Directory.CreateDirectory(dir).FullName;
    }

    private static BitmapSource Swatch(byte r, byte g, byte b, int w = 32, int h = 32)
    {
        BitmapSource? result = null;
        Sta(() =>
        {
            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var ctx = visual.RenderOpen())
                ctx.DrawRectangle(new SolidColorBrush(Color.FromRgb(r, g, b)), null,
                    new System.Windows.Rect(0, 0, w, h));
            bmp.Render(visual);
            bmp.Freeze();
            result = bmp;
        });
        return result!;
    }

    private static int CountNear(BitmapSource bmp, byte r, byte g, byte b, int tolerance)
    {
        int w = bmp.PixelWidth, h = bmp.PixelHeight;
        byte[] pixels = new byte[w * h * 4];
        bmp.CopyPixels(pixels, w * 4, 0);
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
    public void Put_ThenGet_RoundTripsPixels()
    {
        var cache = new PageRasterCache(NewDir());
        cache.Put("docA", 1, Swatch(200, 30, 30));
        var hit = cache.TryGet("docA", 1);
        hit.ShouldNotBeNull("stored raster must come back");
        CountNear(hit!, 200, 30, 30, 24).ShouldBeGreaterThan(900, "round-tripped pixels must match");
        hit!.PixelWidth.ShouldBe(32);
    }

    [Fact]
    public void Miss_And_Corruption_ReturnNull()
    {
        string dir = NewDir();
        var cache = new PageRasterCache(dir);
        cache.TryGet("docA", 9).ShouldBeNull("nothing stored yet");
        cache.Put("docA", 1, Swatch(30, 30, 200));
        cache.TryGet("docB", 1).ShouldBeNull("different document must not hit");
        cache.TryGet("docA", 2).ShouldBeNull("different page must not hit");
        File.WriteAllBytes(
            Directory.GetFiles(dir, "*.png").First(), new byte[] { 1, 2, 3, 4 });
        cache.TryGet("docA", 1).ShouldBeNull("corrupt entry must fail soft");
    }

    [Fact]
    public void Prune_EvictsOldestFirst()
    {
        var cache = new PageRasterCache(NewDir(), maxFiles: 3, maxBytes: long.MaxValue);
        for (int p = 1; p <= 5; p++)
        {
            cache.Put("docA", p, Swatch((byte)(p * 40), 30, 30));
            new FileInfo(new DirectoryInfo(cache.Directory).GetFiles("*.png")
                    .OrderByDescending(f => f.LastWriteTimeUtc).First().FullName)
                .LastWriteTimeUtc = DateTime.UtcNow.AddMinutes(-(5 - p));
        }
        cache.TryGet("docA", 1).ShouldBeNull("oldest must be evicted");
        cache.TryGet("docA", 2).ShouldBeNull("second oldest must be evicted");
        cache.TryGet("docA", 5).ShouldNotBeNull("newest must survive");
        Directory.GetFiles(cache.Directory, "*.png").Length.ShouldBe(3);
    }
}
