namespace Scribbles.Core;

/// <summary>
/// Raster dimensions: exact-scale targets (96 * renderScale DPI) so the
/// nominal base size is known before rendering and ink mapping never drifts.
/// </summary>
public static class RenderDims
{
    public const double BaseDpi = 96.0;
    public const double PointsPerInch = 72.0;

    public static (int Width, int Height) TargetPixels(double widthPt, double heightPt, double renderScale = 2.0) => (
        Math.Max(1, (int)(widthPt / PointsPerInch * BaseDpi * renderScale)),
        Math.Max(1, (int)(heightPt / PointsPerInch * BaseDpi * renderScale)));

    public static (double Width, double Height) NominalBaseDips(double widthPt, double heightPt) => (
        widthPt / PointsPerInch * BaseDpi,
        heightPt / PointsPerInch * BaseDpi);
}
