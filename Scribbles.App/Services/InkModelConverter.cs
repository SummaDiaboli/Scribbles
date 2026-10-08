using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using Scribbles.Core;

namespace Scribbles.App.Services;

/// <summary>
/// Converts between WPF InkCanvas strokes (DIPs, y-down) and the Core note
/// model (PDF points, y-down). Zoom-independent: pages have a fixed base size.
/// </summary>
public sealed class InkModelConverter
{
    private readonly Dictionary<Stroke, string> _ids = new(ReferenceEqualityComparer.Instance);

    public string GetId(Stroke stroke)
    {
        if (!_ids.TryGetValue(stroke, out var id))
        {
            id = Guid.NewGuid().ToString("N");
            _ids[stroke] = id;
        }
        return id;
    }

    public void Register(Stroke stroke, string id) => _ids[stroke] = id;

    public void Forget(Stroke stroke) => _ids.Remove(stroke);

    public void Clear() => _ids.Clear();

    public InkStroke ToModel(Stroke stroke, int pageNumber, double dipPerPtX, double dipPerPtY)
    {
        var attrs = stroke.DrawingAttributes;
        var (a, r, g, b) = (attrs.Color.A, attrs.Color.R, attrs.Color.G, attrs.Color.B);
        var points = new List<StrokePoint>(stroke.StylusPoints.Count);
        foreach (var sp in stroke.StylusPoints)
        {
            float pressure = 0.5f;
            try { pressure = (float)sp.PressureFactor; } catch { /* default */ }
            points.Add(new StrokePoint(sp.X / dipPerPtX, sp.Y / dipPerPtY, pressure));
        }
        return new InkStroke(
            GetId(stroke),
            pageNumber,
            attrs.IsHighlighter ? ToolKind.Highlighter : ToolKind.Pen,
            InkUnits.ToArgb(a, r, g, b),
            attrs.Width / dipPerPtX,
            points);
    }

    public Stroke FromModel(InkStroke model, double dipPerPtX, double dipPerPtY)
    {
        var points = new StylusPointCollection(model.Points.Count);
        foreach (var p in model.Points)
            points.Add(new StylusPoint(p.X * dipPerPtX, p.Y * dipPerPtY, p.Pressure));
        var (a, r, g, b) = InkUnits.FromArgb(model.ColorArgb);
        var attrs = new DrawingAttributes
        {
            Color = Color.FromArgb(a, r, g, b),
            Width = Math.Max(1, model.WidthPt * dipPerPtX),
            Height = Math.Max(1, model.WidthPt * dipPerPtY),
            FitToCurve = true,
            IgnorePressure = false,
            IsHighlighter = model.Tool == ToolKind.Highlighter,
            StylusTip = model.Tool == ToolKind.Highlighter ? StylusTip.Rectangle : StylusTip.Ellipse,
        };
        var stroke = new Stroke(points, attrs);
        Register(stroke, model.Id);
        return stroke;
    }
}
