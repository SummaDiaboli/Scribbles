using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using Scribbles.Core;

namespace Scribbles.App;

/// <summary>Active pen tool. Mirrors the pen-toolbar radio group.</summary>
internal enum PenTool
{
    Pen,
    Highlighter,
    StrokeEraser,
    PointEraser,
    Lasso,
    TextSelect,
    TextEdit,
}

/// <summary>Pen toolbar state: tool, color, size → InkCanvas modes + attributes.</summary>
internal sealed class PenToolbarController
{
    public PenTool Tool { get; private set; } = PenTool.Pen;
    public Color InkColor { get; private set; } = Colors.Black;
    public double SizeDip { get; private set; } = 3.5;
    public PenKind PenFamily { get; private set; } = PenKind.Fountain;

    public void SetTool(string? tag) => Tool = tag switch
    {
        "Highlighter" => PenTool.Highlighter,
        "StrokeEraser" => PenTool.StrokeEraser,
        "PointEraser" => PenTool.PointEraser,
        "Lasso" => PenTool.Lasso,
        "TextSelect" => PenTool.TextSelect,
        "TextEdit" => PenTool.TextEdit,
        _ => PenTool.Pen,
    };

    public void SetColor(Color color) => InkColor = color;

    public void SetSize(double dip) => SizeDip = dip;

    public void SetPenFamily(PenKind family) => PenFamily = family;

    public DrawingAttributes CurrentAttributes()
    {
        var spec = Tool == PenTool.Highlighter
            ? PenProfile.Plan(PenKind.Highlighter)
            : PenProfile.Plan(PenFamily);
        return new DrawingAttributes
        {
            Color = InkColor,
            Width = SizeDip,
            Height = SizeDip,
            FitToCurve = true,
            IgnorePressure = spec.IgnorePressure,
            IsHighlighter = spec.IsHighlighter,
            StylusTip = spec.RectangularTip ? StylusTip.Rectangle : StylusTip.Ellipse,
        };
    }

    public void ApplyTo(InkCanvas ink)
    {
        ink.DefaultDrawingAttributes = CurrentAttributes();
        ink.EditingMode = Tool switch
        {
            PenTool.Highlighter => InkCanvasEditingMode.Ink,
            PenTool.StrokeEraser => InkCanvasEditingMode.EraseByStroke,
            PenTool.PointEraser => InkCanvasEditingMode.EraseByPoint,
            PenTool.Lasso => InkCanvasEditingMode.Select,
            PenTool.TextSelect => InkCanvasEditingMode.None, // marquee handled by mouse
            PenTool.TextEdit => InkCanvasEditingMode.None, // clicks handled by mouse
            _ => InkCanvasEditingMode.Ink,
        };
    }

    public void ApplyToAll(IEnumerable<InkCanvas> inks)
    {
        foreach (var ink in inks)
        {
            ApplyTo(ink);
            ink.EditingModeInverted = InkCanvasEditingMode.EraseByStroke;
            double erase = Math.Max(8, SizeDip * 2);
            ink.EraserShape = new EllipseStylusShape(erase, erase);
        }
    }

    /// <summary>Recolors the current lasso selection. Returns true if anything changed.</summary>
    public bool RecolorSelection(IEnumerable<InkCanvas> inks, Action<int> pushHistory)
    {
        bool changed = false;
        foreach (var ink in inks)
        {
            var selected = ink.GetSelectedStrokes();
            if (selected.Count == 0)
                continue;
            pushHistory(CanvasExplorer.GetPageNumber(ink));
            foreach (var s in selected)
            {
                var attrs = s.DrawingAttributes.Clone();
                attrs.Color = InkColor;
                s.DrawingAttributes = attrs;
            }
            changed = true;
        }
        return changed;
    }
}
