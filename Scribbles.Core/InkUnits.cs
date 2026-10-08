namespace Scribbles.Core;

/// <summary>
/// Ink/stroke unit conversions. Strokes are stored in PDF points (y-down);
/// the viewer shows them in DIPs at a fixed base size where 1pt = 96/72 dips
/// (zoom is a pure visual LayoutTransform scale, so mapping is zoom-free).
/// </summary>
public static class InkUnits
{
    public const double DipsPerPoint = 96.0 / 72.0;

    [Obsolete("Renderers fit pages to dimensions instead of exact DPI — use the per-page factors (BaseWidth / WidthPt).")]
    public static double DipToPt(double dip) => dip / DipsPerPoint;

    [Obsolete("Renderers fit pages to dimensions instead of exact DPI — use the per-page factors (BaseWidth / WidthPt).")]
    public static double PtToDip(double pt) => pt * DipsPerPoint;

    /// <summary>PDF user space is y-up; everything else here is y-down.</summary>
    public static double FlipY(double y, double pageHeightPt) => pageHeightPt - y;

    public static uint ToArgb(byte a, byte r, byte g, byte b) =>
        ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

    public static (byte A, byte R, byte G, byte B) FromArgb(uint argb) =>
        ((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}
