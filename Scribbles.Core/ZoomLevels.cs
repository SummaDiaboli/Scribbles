namespace Scribbles.Core;

/// <summary>Zoom preset stepping, label parsing and pinch math (pure).</summary>
public static class ZoomLevels
{
    public const double Min = 0.25;
    public const double Max = 4.0;

    public static readonly double[] Presets = { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0 };

    public static double Clamp(double zoom) => Math.Clamp(zoom, Min, Max);

    public static double Next(double current, bool up)
    {
        if (up)
        {
            foreach (double p in Presets)
                if (p > current + 1e-9)
                    return p;
            return Math.Min(current * 1.25, Max);
        }
        for (int i = Presets.Length - 1; i >= 0; i--)
            if (Presets[i] < current - 1e-9)
                return Presets[i];
        return Math.Max(current / 1.25, Min);
    }

    /// <summary>Parses "150%", "150" (percent) or "1.5" (raw factor).</summary>
    public static bool TryParseLabel(string? label, out double zoom)
    {
        zoom = 0;
        if (string.IsNullOrWhiteSpace(label))
            return false;
        string text = label.Trim();
        if (text.EndsWith('%'))
        {
            if (!double.TryParse(text.TrimEnd('%', ' '),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double pct))
                return false;
            if (pct is < 25 or > 400)
                return false;
            zoom = pct / 100.0;
            return true;
        }
        if (!double.TryParse(text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double value))
            return false;
        if (value is >= 25 and <= 400)
        {
            zoom = value / 100.0;
            return true;
        }
        if (value is >= Min and <= Max)
        {
            zoom = value;
            return true;
        }
        return false;
    }

    /// <summary>Pinch zoom anchored at the gesture start (clamped, degenerate-safe).</summary>
    public static double ApplyPinch(double currentZoom, double startDistance, double currentDistance)
    {
        if (startDistance <= 0 || currentDistance <= 0)
            return currentZoom;
        return Clamp(currentZoom * (currentDistance / startDistance));
    }
}
