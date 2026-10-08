namespace Scribbles.Core;

/// <summary>How a pen family maps to ink attributes (pure, UI renders it).</summary>
public sealed record PenSpec(bool IsHighlighter, bool IgnorePressure, bool RectangularTip, double Opacity);

public static class PenProfile
{
    public static PenSpec Plan(PenKind kind) => kind switch
    {
        PenKind.Ballpoint => new PenSpec(IsHighlighter: false, IgnorePressure: true, RectangularTip: false, Opacity: 1.0),
        PenKind.Highlighter => new PenSpec(IsHighlighter: true, IgnorePressure: false, RectangularTip: true, Opacity: 0.5),
        _ => new PenSpec(IsHighlighter: false, IgnorePressure: false, RectangularTip: false, Opacity: 1.0), // Fountain
    };
}
