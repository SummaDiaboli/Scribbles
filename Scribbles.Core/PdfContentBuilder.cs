using System.Globalization;

namespace Scribbles.Core;

/// <summary>
/// Pure PDF content-stream builders (ASCII-only, y-up user space).
/// The store wraps these in streams; tests assert the operators.
/// </summary>
public static class PdfContentBuilder
{
    public const string CopyFontName = "ScribblesH1";

    internal static string F(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Escapes a string for a PDF literal: parens, backslash, non-ASCII → ?.</summary>
    public static string EscapePdfString(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c is '(' or ')' or '\\')
            {
                sb.Append('\\');
                sb.Append(c);
            }
            else if (c is < ' ' or > '~')
            {
                sb.Append('?');
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>Burns a stroke path into content (flattened ink look).</summary>
    public static string FlattenStroke(
        IReadOnlyList<StrokePoint> points, double pageHeightPt, double widthPt,
        double r, double g, double b)
    {
        if (points.Count == 0)
            return "";
        var sb = new System.Text.StringBuilder();
        sb.Append("q\n");
        sb.Append($"{F(r)} {F(g)} {F(b)} RG\n");
        sb.Append($"{F(Math.Max(0.5, widthPt))} w\n");
        sb.Append("1 J\n1 j\n");
        bool first = true;
        foreach (var p in points)
        {
            double y = InkUnits.FlipY(p.Y, pageHeightPt);
            sb.Append(first ? $"{F(p.X)} {F(y)} m\n" : $"{F(p.X)} {F(y)} l\n");
            first = false;
        }
        sb.Append("S\nQ\n");
        return sb.ToString();
    }

    /// <summary>Invisible but copyable text (render mode 3). y-DOWN top.</summary>
    public static string InvisibleText(string text, double xPt, double yDownTopPt, double fontSizePt, double pageHeightPt,
        string fontName = CopyFontName)
    {
        return TextBlock(text, xPt, yDownTopPt, fontSizePt, pageHeightPt, fontName, invisible: true, 0, 0, 0);
    }

    /// <summary>Visible text (converted boxes, native edits). y-DOWN top.</summary>
    public static string VisibleText(string text, double xPt, double yDownTopPt, double fontSizePt, double pageHeightPt,
        double r = 0, double g = 0, double b = 0, string fontName = CopyFontName)
    {
        return TextBlock(text, xPt, yDownTopPt, fontSizePt, pageHeightPt, fontName, invisible: false, r, g, b);
    }

    private static string TextBlock(string text, double xPt, double yDownTopPt, double fontSizePt, double pageHeightPt,
        string fontName, bool invisible, double r, double g, double b)
    {
        var sb = new System.Text.StringBuilder();
        double y = pageHeightPt - yDownTopPt - fontSizePt; // first baseline, y-up
        foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
        {
            sb.Append($"BT /{fontName} {F(fontSizePt)} Tf ");
            if (invisible)
                sb.Append("3 Tr ");
            else
                sb.Append($"{F(r)} {F(g)} {F(b)} rg ");
            sb.Append($"{F(xPt)} {F(y)} Td ({EscapePdfString(line)}) Tj ET\n");
            y -= fontSizePt * 1.2;
        }
        return sb.ToString();
    }

    /// <summary>Opaque cover rectangle (hides native text being edited).</summary>
    public static string CoverRect(double xPt, double yDownTopPt, double widthPt, double heightPt, double pageHeightPt)
    {
        double yUp = pageHeightPt - (yDownTopPt + heightPt);
        return $"q\n1 g\n{F(xPt)} {F(yUp)} {F(widthPt)} {F(heightPt)} re\nf\nQ\n";
    }
}
