namespace Scribbles.Core;

/// <summary>
/// Pure pen vs touch vs palm classification (M1).
/// WPF mapping happens in the App layer via StylusDevice.TabletDevice.Type;
/// this keeps the decision logic unit-testable.
/// </summary>
public enum InputKind
{
    Pen,
    Touch,
    Palm,
    Mouse,
    Unknown,
}

public static class InputClassification
{
    /// <param name="tabletDeviceType">
    /// Value of StylusDevice.TabletDevice.Type.ToString(): "Stylus", "Touch", "Mouse".
    /// </param>
    /// <param name="contactWidthMm">Touch contact diameter; large contacts are palms.</param>
    public static InputKind Classify(string? tabletDeviceType, double contactWidthMm = 0)
    {
        return tabletDeviceType switch
        {
            "Stylus" => InputKind.Pen,
            "Mouse" => InputKind.Mouse,
            "Touch" => contactWidthMm >= 20.0 ? InputKind.Palm : InputKind.Touch,
            _ => InputKind.Unknown,
        };
    }

    /// <summary>True when the input should draw ink (pen only in M1).</summary>
    public static bool ShouldDraw(InputKind kind) => kind == InputKind.Pen;

    /// <summary>True when the input should scroll/zoom instead of drawing.</summary>
    public static bool ShouldScroll(InputKind kind) =>
        kind is InputKind.Touch or InputKind.Palm or InputKind.Mouse;
}
