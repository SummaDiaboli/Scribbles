namespace Scribbles.Core;

/// <summary>Stroke family for the pen toolbar (user requirement).</summary>
public enum PenKind
{
    Ballpoint,
    Fountain,
    Highlighter,
}

/// <summary>Persisted window bounds (null Window = first launch, use defaults).</summary>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized)
{
    public static WindowPlacement ClampToVisible(WindowPlacement placement, ScreenBounds screen)
    {
        double width = Math.Min(Math.Max(placement.Width, 200), screen.Width);
        double height = Math.Min(Math.Max(placement.Height, 150), screen.Height);
        double left = Math.Clamp(placement.Left, screen.Left, screen.Left + screen.Width - 200);
        double top = Math.Clamp(placement.Top, screen.Top, screen.Top + screen.Height - 100);
        return placement with { Left = left, Top = top, Width = width, Height = height };
    }
}

/// <summary>Visible screen area used for placement clamping (test seam).</summary>
public sealed record ScreenBounds(double Left, double Top, double Width, double Height);

/// <summary>User settings, persisted as JSON under %LocalAppData%/Scribbles.</summary>
public sealed record AppSettings(
    AppTheme Theme,
    uint PenColorArgb,
    double PenSizeDip,
    PenKind PenType,
    string[] Palette,
    int AutosaveSeconds,
    bool ExportCopyableDefault,
    WindowPlacement? Window,
    string[] RecentFiles,
    List<LastPage> LastPages)
{
    public bool Equals(AppSettings? other) =>
        other is not null
        && Theme == other.Theme
        && PenColorArgb == other.PenColorArgb
        && PenSizeDip.Equals(other.PenSizeDip)
        && PenType == other.PenType
        && Palette.SequenceEqual(other.Palette)
        && AutosaveSeconds == other.AutosaveSeconds
        && ExportCopyableDefault == other.ExportCopyableDefault
        && Equals(Window, other.Window)
        && RecentFiles.SequenceEqual(other.RecentFiles)
        && LastPages.SequenceEqual(other.LastPages);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Theme);
        hash.Add(PenColorArgb);
        hash.Add(PenSizeDip);
        hash.Add(PenType);
        foreach (var c in Palette)
            hash.Add(c);
        hash.Add(AutosaveSeconds);
        hash.Add(ExportCopyableDefault);
        hash.Add(Window);
        foreach (var f in RecentFiles)
            hash.Add(f);
        foreach (var l in LastPages)
        {
            hash.Add(l.Path);
            hash.Add(l.Page);
        }
        return hash.ToHashCode();
    }

    public static AppSettings Default { get; } = new(
        AppTheme.System,
        0xFF000000,
        3.5,
        PenKind.Fountain,
        new[] { "#FF000000", "#FF005FB8", "#FFD13438", "#FF107C10" },
        30,
        false,
        new WindowPlacement(100, 100, 1100, 750, false),
        Array.Empty<string>(),
        new List<LastPage>());
}
