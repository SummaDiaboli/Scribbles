namespace Scribbles.Core;

/// <summary>Caret placement within a reading-ordered line ("w1 w2 …").</summary>
public static class TextCaret
{
    /// <summary>Character offset for an x position (clamped to the line text).</summary>
    public static int OffsetForX(IReadOnlyList<PdfWord> lineLeftToRight, double x)
    {
        int offset = 0;
        for (int i = 0; i < lineLeftToRight.Count; i++)
        {
            var word = lineLeftToRight[i];
            if (word.Text.Length == 0 || word.Width <= 0)
                continue;
            if (x < word.X)
                return offset;
            if (x <= word.X + word.Width)
            {
                double frac = (x - word.X) / word.Width;
                return offset + Math.Clamp((int)Math.Round(frac * word.Text.Length), 0, word.Text.Length);
            }
            offset += word.Text.Length;
            if (i < lineLeftToRight.Count - 1)
                offset++; // the joining space
        }
        return offset;
    }
}
