namespace Scribbles.Core;

/// <summary>Rules for the user-saved color palette (most-recent-first, max 12).</summary>
public static class ColorPalette
{
    public const int MaxColors = 12;

    /// <summary>Preset grid for the picker: 8 grays + 4 levels × 8 hues.</summary>
    public static string[] Presets { get; } = BuildPresets();

    private static string[] BuildPresets()
    {
        var list = new List<string>();
        foreach (byte v in new byte[] { 0, 36, 73, 109, 146, 182, 219, 255 })
            list.Add($"#FF{v:X2}{v:X2}{v:X2}");
        double[] hues = { 0, 20, 40, 60, 130, 180, 215, 275 };
        (double S, double V)[] levels = { (1, 1), (0.75, 1), (0.6, 0.85), (0.45, 0.65) };
        foreach (var (s, v) in levels)
        {
            foreach (double h in hues)
            {
                var (r, g, b) = new HsvColor(h, s, v).ToRgb();
                list.Add($"#FF{r:X2}{g:X2}{b:X2}");
            }
        }
        return list.Distinct().ToArray();
    }

    public static string Normalize(string hex)
    {
        if (!TryNormalize(hex, out string? normalized))
            throw new ArgumentException($"Not a color: '{hex}'", nameof(hex));
        return normalized!;
    }

    public static bool TryNormalize(string? hex, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(hex))
            return false;
        string h = hex.Trim().TrimStart('#');
        if (h.Length == 3 && h.All(IsHex))
            h = $"FF{h[0]}{h[0]}{h[1]}{h[1]}{h[2]}{h[2]}";
        else if (h.Length == 6 && h.All(IsHex))
            h = "FF" + h;
        else if (!(h.Length == 8 && h.All(IsHex)))
            return false;
        normalized = "#" + h.ToUpperInvariant();
        return true;
    }

    public static string[] Add(string[] palette, string hex)
    {
        string color = Normalize(hex);
        return palette.Where(c => !string.Equals(c, color, StringComparison.OrdinalIgnoreCase))
            .Prepend(color)
            .Take(MaxColors)
            .ToArray();
    }

    public static string[] Remove(string[] palette, string hex) =>
        palette.Where(c => !string.Equals(c, hex, StringComparison.OrdinalIgnoreCase)).ToArray();

    private static bool IsHex(char c) => Uri.IsHexDigit(c);
}
