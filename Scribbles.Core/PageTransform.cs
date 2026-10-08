namespace Scribbles.Core;

/// <summary>
/// Pure ink/page coordinate transforms across zoom, rotation and DPI.
/// Page space is PDF points (1/72 inch, origin top-left, +x right, +y down).
/// Screen space is device-independent pixels (DIPs) at the current zoom/DPI.
/// </summary>
public sealed record PageTransform(
    double PageWidthPt,
    double PageHeightPt,
    double Zoom = 1.0,
    double DpiX = 96.0,
    double DpiY = 96.0,
    int RotationDegrees = 0)
{
    public (double X, double Y) PageToScreen(double pageX, double pageY)
    {
        var (rx, ry) = ApplyRotation(pageX, pageY, PageWidthPt, PageHeightPt, RotationDegrees);
        var (wPt, hPt) = RotatedSize(PageWidthPt, PageHeightPt, RotationDegrees);
        double sx = rx / 72.0 * DpiX * Zoom;
        double sy = ry / 72.0 * DpiY * Zoom;
        return (sx, sy);
    }

    public (double X, double Y) ScreenToPage(double screenX, double screenY)
    {
        double rx = screenX / (DpiX * Zoom) * 72.0;
        double ry = screenY / (DpiY * Zoom) * 72.0;
        var (wPt, hPt) = RotatedSize(PageWidthPt, PageHeightPt, RotationDegrees);
        return ApplyInverseRotation(rx, ry, PageWidthPt, PageHeightPt, RotationDegrees);
    }

    internal static (double W, double H) RotatedSize(double w, double h, int rotation)
    {
        int r = ((rotation % 360) + 360) % 360;
        return r is 90 or 270 ? (h, w) : (w, h);
    }

    internal static (double X, double Y) ApplyRotation(double x, double y, double w, double h, int rotation)
    {
        int r = ((rotation % 360) + 360) % 360;
        return r switch
        {
            90 => (h - y, x),
            180 => (w - x, h - y),
            270 => (y, w - x),
            _ => (x, y),
        };
    }

    internal static (double X, double Y) ApplyInverseRotation(double x, double y, double w, double h, int rotation)
    {
        int r = ((rotation % 360) + 360) % 360;
        return r switch
        {
            90 => (y, h - x),
            180 => (w - x, h - y),
            270 => (w - y, x),
            _ => (x, y),
        };
    }
}
