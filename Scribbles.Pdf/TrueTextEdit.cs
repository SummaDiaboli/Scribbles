using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;

namespace Scribbles.Pdf;

/// <summary>
/// Edits the ORIGINAL text layer in place: parses page content, matches the
/// old line/words against decoded show-strings, splices the new bytes, and
/// writes the sequence back. Only simple (printable ASCII) text; anything
/// else returns false so the caller can fall back to cover-overlay.
/// </summary>
public static class TrueTextEdit
{
    public static bool TryReplaceLine(
        byte[] pdfBytes, int pageNumber1, string oldLineText, string newLineText, out byte[] updated)
    {
        updated = pdfBytes;
        if (string.IsNullOrWhiteSpace(oldLineText) || newLineText is null)
            return false;
        if (!IsSimpleText(oldLineText) || !IsSimpleText(newLineText))
            return false;
        try
        {
            using var stream = new MemoryStream(pdfBytes);
            using var doc = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
            if (pageNumber1 < 1 || pageNumber1 > doc.PageCount)
                return false;
            var page = doc.Pages[pageNumber1 - 1];
            var sequence = ContentReader.ReadContent(page);
            var strings = CollectStrings(sequence);
            if (!TryPlan(strings, oldLineText.Trim(), newLineText.Trim(), out var edits))
                return false;
            foreach (var (node, value) in edits)
                node.Value = value;
            page.Contents.ReplaceContent(sequence);
            using var outStream = new MemoryStream();
            doc.Save(outStream);
            updated = outStream.ToArray();
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsSimpleText(string text) =>
        text.Length > 0 && text.All(c => c is >= ' ' and <= '~');

    internal static List<TextRun> CollectStrings(CSequence sequence)
    {
        var found = new List<TextRun>();
        foreach (var obj in sequence)
            CollectFrom(obj, found);
        return found;
    }

    /// <summary>One show-operation's text with the nodes backing it.</summary>
    internal sealed record TextRun(List<CString> Nodes, string Value);

    private static void CollectFrom(CObject obj, List<TextRun> found)
    {
        switch (obj)
        {
            case CString single when IsSimpleText(single.Value):
                found.Add(new TextRun(new List<CString> { single }, single.Value));
                break;
            case COperator op when op.Name is "Tj" or "'" or "\"":
                if (op.Operands.Count > 0 && op.Operands[0] is CString str
                    && IsSimpleText(str.Value))
                    found.Add(new TextRun(new List<CString> { str }, str.Value));
                break;
            case COperator op when op.Name == "TJ":
                if (op.Operands.Count > 0 && op.Operands[0] is CArray array)
                    CollectKerned(array, found);
                break;
        }
    }

    /// <summary>Joins TJ pieces; a large kern gap becomes a space.</summary>
    private static void CollectKerned(CArray array, List<TextRun> found)
    {
        var nodes = new List<CString>();
        var text = new System.Text.StringBuilder();
        foreach (var element in array)
        {
            if (element is CString str && IsSimpleText(str.Value))
            {
                nodes.Add(str);
                text.Append(str.Value);
            }
            else if (element is CNumber
                && double.TryParse(element.ToString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double kern)
                && nodes.Count > 0
                && Math.Abs(kern) >= 200
                && text.Length > 0 && !text.ToString().EndsWith(' '))
            {
                text.Append(' ');
            }
        }
        if (nodes.Count > 0)
            found.Add(new TextRun(nodes, text.ToString()));
    }

    internal static bool TryPlan(
        List<TextRun> strings, string oldLine, string newLine,
        out List<(CString Node, string Value)> edits)
    {
        edits = new List<(CString, string)>();

        // 1. Whole line in a single run (possibly kern-split across nodes).
        var whole = strings.Where(t => t.Value == oldLine).ToList();
        if (whole.Count == 1)
        {
            edits.Add((whole[0].Nodes[0], newLine));
            foreach (var extra in whole[0].Nodes.Skip(1))
                edits.Add((extra, ""));
            return true;
        }

        // 2. Per-word unique matches (same word count required; single-node runs only).
        var oldWords = oldLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var newWords = newLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (oldWords.Length == 0 || oldWords.Length != newWords.Length)
            return false;
        var used = new HashSet<CString>();
        var planned = new List<(CString, string)>();
        for (int i = 0; i < oldWords.Length; i++)
        {
            CString? node = null;
            string? replacement = null;
            var exact = strings
                .Where(t => t.Nodes.Count == 1 && t.Value == oldWords[i] && !used.Contains(t.Nodes[0]))
                .ToList();
            if (exact.Count == 1)
            {
                node = exact[0].Nodes[0];
                replacement = newWords[i];
            }
            else
            {
                var partial = strings
                    .Where(t => t.Nodes.Count == 1 && !used.Contains(t.Nodes[0])
                        && CountOccurrences(t.Value, oldWords[i]) == 1)
                    .ToList();
                if (partial.Count != 1)
                    return false;
                node = partial[0].Nodes[0];
                replacement = partial[0].Value.Replace(oldWords[i], newWords[i], StringComparison.Ordinal);
            }
            used.Add(node);
            planned.Add((node, replacement));
        }
        edits = planned;
        return true;
    }

    private static int CountOccurrences(string text, string word)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(word, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += word.Length;
        }
        return count;
    }
}
