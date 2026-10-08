using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Annotations;
using Scribbles.Core;

namespace Scribbles.Pdf;

/// <summary>
/// Reads/writes standard /Ink annotations (one path per stroke, y-up user
/// space) with a painted /AP appearance Form XObject.
/// </summary>
internal static class InkAnnotationMapper
{
    internal const string NamePrefix = "Scribbles-";

    internal static void RemoveOwnAnnotations(PdfPage page)
    {
        var toRemove = new List<PdfAnnotation>();
        for (int i = 0; i < page.Annotations.Count; i++)
        {
            var annot = page.Annotations[i];
            if (annot.Elements["/NM"] is PdfString name && name.Value.StartsWith(NamePrefix))
                toRemove.Add(annot);
        }
        foreach (var annot in toRemove)
            page.Annotations.Remove(annot);
    }

    internal static void Add(PdfDocument doc, PdfPage page, InkStroke stroke, double pageHeightPt)
    {
        if (stroke.Points.Count == 0)
            return;
        var (minX, minY, maxX, maxY) = stroke.Bounds();
        double pad = stroke.WidthPt / 2.0 + 1.0;

        var dict = new PdfDictionary();
        dict.Elements.SetName("/Type", "/Annot");
        dict.Elements.SetName("/Subtype", "/Ink");
        dict.Elements.SetRectangle("/Rect", new PdfRectangle(
            new XPoint(minX - pad, InkUnits.FlipY(maxY + pad, pageHeightPt)),
            new XPoint(maxX + pad, InkUnits.FlipY(minY - pad, pageHeightPt))));
        dict.Elements["/NM"] = new PdfString(NamePrefix + stroke.Id);
        dict.Elements.SetInteger("/F", 4); // print flag
        var (a, r, g, b) = InkUnits.FromArgb(stroke.ColorArgb);
        var color = new PdfArray();
        color.Elements.Add(new PdfReal(r / 255.0));
        color.Elements.Add(new PdfReal(g / 255.0));
        color.Elements.Add(new PdfReal(b / 255.0));
        dict.Elements["/C"] = color;
        var border = new PdfArray();
        border.Elements.Add(new PdfInteger(0));
        border.Elements.Add(new PdfInteger(0));
        border.Elements.Add(new PdfReal(Math.Max(0.5, stroke.WidthPt)));
        dict.Elements["/Border"] = border;

        // /InkList: array of paths; one path per stroke, y-up user space.
        var inkList = new PdfArray();
        var path = new PdfArray();
        foreach (var p in stroke.Points)
        {
            path.Elements.Add(new PdfReal(p.X));
            path.Elements.Add(new PdfReal(InkUnits.FlipY(p.Y, pageHeightPt)));
        }
        inkList.Elements.Add(path);
        dict.Elements["/InkList"] = inkList;
        AddAppearance(doc, dict, stroke, pageHeightPt);

        var annot = new PdfInkAnnotation(page.Owner);
        foreach (var kv in dict.Elements)
            annot.Elements[kv.Key] = kv.Value;
        page.Annotations.Add(annot);
    }

    /// <summary>
    /// Minimal /AP appearance: strokes the ink path into a Form XObject so
    /// viewers that don't synthesize ink appearance paint the stroke.
    /// Coordinates are page user space (/BBox = /Rect).
    /// </summary>
    private static void AddAppearance(PdfDocument doc, PdfDictionary dict, InkStroke stroke, double pageHeightPt)
    {
        try
        {
            static string F(double d) => d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var (a, r, g, b) = InkUnits.FromArgb(stroke.ColorArgb);
            var sb = new System.Text.StringBuilder();
            sb.Append("q\n");
            sb.Append($"{F(r / 255.0)} {F(g / 255.0)} {F(b / 255.0)} RG\n");
            sb.Append($"{F(Math.Max(0.5, stroke.WidthPt))} w\n");
            sb.Append("1 J\n1 j\n");
            bool first = true;
            foreach (var p in stroke.Points)
            {
                double y = InkUnits.FlipY(p.Y, pageHeightPt);
                sb.Append(first ? $"{F(p.X)} {F(y)} m\n" : $"{F(p.X)} {F(y)} l\n");
                first = false;
            }
            sb.Append("S\nQ\n");

            var form = new PdfDictionary(doc);
            form.Elements.SetName("/Type", "/XObject");
            form.Elements.SetName("/Subtype", "/Form");
            form.Elements["/BBox"] = dict.Elements["/Rect"];
            form.Elements["/Resources"] = new PdfDictionary();
            form.CreateStream(System.Text.Encoding.ASCII.GetBytes(sb.ToString()));
            doc.Internals.AddObject(form); // make indirect so /AP can reference it

            var ap = new PdfDictionary();
            ap.Elements.SetReference("/N", form);
            dict.Elements["/AP"] = ap;
        }
        catch (Exception ex)
        {
            // Appearance is best-effort; /InkList remains the source of truth.
            System.Diagnostics.Debug.WriteLine($"AP failed: {ex}");
            dict.Elements.Remove("/AP");
        }
    }

    internal static IReadOnlyList<InkStroke> Read(PdfPage page, int pageNumber, double pageHeightPt)
    {
        var strokes = new List<InkStroke>();
        for (int i = 0; i < page.Annotations.Count; i++)
        {
            var annot = page.Annotations[i];
            if (annot.Elements["/Subtype"] is not PdfName subtype || subtype.Value != "/Ink")
                continue;
            if (annot.Elements["/InkList"] is not PdfArray inkList)
                continue;

            uint color = 0xFF000000;
            if (annot.Elements["/C"] is PdfArray c && c.Elements.Count >= 3)
            {
                byte R(int idx) => (byte)Math.Clamp(Math.Round(ToDouble(c.Elements[idx]) * 255.0), 0, 255);
                color = InkUnits.ToArgb(0xFF, R(0), R(1), R(2));
            }
            double width = 2.0;
            if (annot.Elements["/Border"] is PdfArray border && border.Elements.Count >= 3)
                width = Math.Max(0.5, ToDouble(border.Elements[2]));

            foreach (var pathElement in inkList.Elements)
            {
                if (pathElement is not PdfArray path || path.Elements.Count < 4)
                    continue;
                var points = new List<StrokePoint>();
                for (int k = 0; k + 1 < path.Elements.Count; k += 2)
                {
                    double x = ToDouble(path.Elements[k]);
                    double y = InkUnits.FlipY(ToDouble(path.Elements[k + 1]), pageHeightPt);
                    points.Add(new StrokePoint(x, y));
                }
                if (points.Count >= 2)
                {
                    string name = (annot.Elements["/NM"] as PdfString)?.Value ?? Guid.NewGuid().ToString();
                    strokes.Add(new InkStroke(
                        name.Replace(NamePrefix, ""), pageNumber, ToolKind.Pen, color, width, points));
                }
            }
        }
        return strokes;
    }

    private static double ToDouble(PdfItem item) => item switch
    {
        PdfReal real => real.Value,
        PdfInteger integer => integer.Value,
        _ => 0.0,
    };

    /// <summary>Generic /Ink annotation (PdfSharp ships no ink class).</summary>
    private sealed class PdfInkAnnotation : PdfAnnotation
    {
        public PdfInkAnnotation(PdfDocument document) : base(document) { }
    }
}
