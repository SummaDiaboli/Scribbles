namespace Scribbles.Core;

/// <summary>
/// HSV color (H: 0–360, S/V: 0–1) backing the color-sea picker.
/// </summary>
public sealed record HsvColor(double H, double S, double V)
{
    public static HsvColor FromRgb(byte r, byte g, byte b)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        double h = 0;
        if (delta > 1e-9)
        {
            if (max == rd) h = 60 * (((gd - bd) / delta) % 6);
            else if (max == gd) h = 60 * (((bd - rd) / delta) + 2);
            else h = 60 * (((rd - gd) / delta) + 4);
            if (h < 0) h += 360;
        }
        double s = max < 1e-9 ? 0 : delta / max;
        return new HsvColor(h, s, max);
    }

    public (byte R, byte G, byte B) ToRgb()
    {
        double c = V * S;
        double x = c * (1 - Math.Abs(((H / 60) % 2) - 1));
        double m = V - c;
        (double r, double g, double b) = (H % 360) switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return ((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    /// <summary>Position of this color in the SV square: x = S, y = 1 - V.</summary>
    public (double X, double Y) FieldPosition() => (S, 1 - V);

    /// <summary>Color at a position in the SV square for the given hue (clamped).</summary>
    public static HsvColor FromField(double hue, double x, double y) =>
        new(((hue % 360) + 360) % 360, Math.Clamp(x, 0, 1), Math.Clamp(1 - y, 0, 1));

    public HsvColor WithHue(double hue) => this with { H = ((hue % 360) + 360) % 360 };
}
