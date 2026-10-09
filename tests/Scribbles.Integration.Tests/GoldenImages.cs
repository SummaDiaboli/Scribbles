using System.Drawing;
using System.Drawing.Imaging;
using Docnet.Core;
using Docnet.Core.Models;
using Shouldly;

namespace Scribbles.Integration.Tests;

/// <summary>
/// Golden-image comparison for exported renders: deterministic (pinned
/// PDFium via Docnet), tolerance-based, diff artifact on failure.
/// First run for a new golden writes it and fails so a human reviews it.
/// </summary>
internal static class GoldenImages
{
    public static byte[] RenderFirstPage(byte[] pdf)
    {
        using var reader = DocLib.Instance.GetDocReader(pdf, new PageDimensions(800, 800));
        using var page = reader.GetPageReader(0);
        byte[] bgra = page.GetImage(RenderFlags.RenderAnnotations);
        int w = page.GetPageWidth(), h = page.GetPageHeight();
        // Composite over white: raw pixels are transparent-backed.
        byte[] composited = new byte[bgra.Length];
        for (int i = 0; i < w * h; i++)
        {
            double a = bgra[i * 4 + 3] / 255.0;
            composited[i * 4] = (byte)(bgra[i * 4] * a + 255 * (1 - a));
            composited[i * 4 + 1] = (byte)(bgra[i * 4 + 1] * a + 255 * (1 - a));
            composited[i * 4 + 2] = (byte)(bgra[i * 4 + 2] * a + 255 * (1 - a));
            composited[i * 4 + 3] = 255;
        }
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, bmp.PixelFormat);
        try
        {
            System.Runtime.InteropServices.Marshal.Copy(composited, 0, data.Scan0, composited.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    public static void AssertMatches(string name, byte[] actualPng, double maxFraction = 0.01)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "goldens");
        string goldenPath = Path.GetFullPath(Path.Combine(dir, name + ".png"));
        if (!File.Exists(goldenPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllBytes(goldenPath, actualPng);
            throw new Xunit.Sdk.XunitException(
                $"New golden '{name}.png' written — review it, then re-run.");
        }

        using var golden = new Bitmap(goldenPath);
        using var actualStream = new MemoryStream(actualPng);
        using var actual = new Bitmap(actualStream);
        if (golden.Width != actual.Width || golden.Height != actual.Height)
            throw new Xunit.Sdk.XunitException(
                $"Golden '{name}' size mismatch: {golden.Width}x{golden.Height} vs {actual.Width}x{actual.Height}.");

        int differing = 0, total = golden.Width * golden.Height;
        using var diff = new Bitmap(golden.Width, golden.Height, PixelFormat.Format32bppArgb);
        for (int y = 0; y < golden.Height; y++)
        {
            for (int x = 0; x < golden.Width; x++)
            {
                Color a = golden.GetPixel(x, y);
                Color b = actual.GetPixel(x, y);
                int spread = Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                if (spread > 48)
                {
                    differing++;
                    diff.SetPixel(x, y, Color.Magenta);
                }
                else
                {
                    diff.SetPixel(x, y, Color.FromArgb(b.R, b.G, b.B));
                }
            }
        }
        double fraction = (double)differing / total;
        if (fraction > maxFraction)
        {
            string diffPath = Path.Combine(Path.GetTempPath(), $"scribbles-{name}.diff.png");
            diff.Save(diffPath, ImageFormat.Png);
            throw new Xunit.Sdk.XunitException(
                $"Golden '{name}' differs by {fraction:P2} (>{maxFraction:P2}). Diff: {diffPath}");
        }
    }
}
