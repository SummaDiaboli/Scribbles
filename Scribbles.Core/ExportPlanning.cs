namespace Scribbles.Core;

/// <summary>
/// M2/M3 export planning: which layers get written for keep / convert /
/// copyable modes (pure, unit-tested; M3 wires the UI).
/// </summary>
public enum InkHandleMode
{
    /// <summary>Keep as ink (default).</summary>
    Keep,
    /// <summary>Replace ink with an editable text box.</summary>
    Convert,
    /// <summary>Keep ink + invisible copyable text layer.</summary>
    Copyable,
}

public sealed record ExportPlan(
    bool WriteInkAnnotations,
    bool WriteTextBoxes,
    bool WriteInvisibleTextLayer);

public static class ExportPlanner
{
    public static ExportPlan Plan(InkHandleMode mode) => mode switch
    {
        InkHandleMode.Keep => new ExportPlan(WriteInkAnnotations: true, WriteTextBoxes: false, WriteInvisibleTextLayer: false),
        InkHandleMode.Convert => new ExportPlan(WriteInkAnnotations: false, WriteTextBoxes: true, WriteInvisibleTextLayer: false),
        InkHandleMode.Copyable => new ExportPlan(WriteInkAnnotations: true, WriteTextBoxes: false, WriteInvisibleTextLayer: true),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}
