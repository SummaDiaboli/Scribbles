using Scribbles.Core;
using UglyToad.PdfPig;

namespace Scribbles.Pdf;

/// <summary>
/// Extracts native PDF text words per page (y-DOWN points, matching the
/// Core coordinate convention). Scanned/image-only pages yield no words.
/// </summary>
public static class PdfTextExtractor
{
    public static IReadOnlyList<IReadOnlyList<PdfWord>> ExtractPages(byte[] pdfBytes)
    {
        var pages = new List<IReadOnlyList<PdfWord>>();
        try
        {
            using var document = PdfDocument.Open(pdfBytes);
            foreach (var page in document.GetPages())
            {
                var words = ExtractWords(page);
                if (words.Count == 0)
                    words = ExtractFromLetters(page); // subset/odd fonts expose no words
                pages.Add(words);
            }
        }
        catch
        {
            // Encrypted or unreadable: no selectable text.
        }
        return pages;
    }

    private static List<PdfWord> ExtractWords(UglyToad.PdfPig.Content.Page page)
    {
        var words = new List<PdfWord>();
        foreach (var word in page.GetWords())
        {
            var box = word.BoundingBox;
            double height = box.TopRight.Y - box.BottomLeft.Y;
            if (height <= 0 || string.IsNullOrWhiteSpace(word.Text))
                continue;
            words.Add(new PdfWord(
                word.Text,
                box.BottomLeft.X,
                InkUnits.FlipY(box.TopRight.Y, page.Height),
                box.TopRight.X - box.BottomLeft.X,
                height));
        }
        return words;
    }

    private static List<PdfWord> ExtractFromLetters(UglyToad.PdfPig.Content.Page page)
    {
        var glyphs = new List<TextGlyph>();
        foreach (var letter in page.Letters)
        {
            var box = letter.GlyphRectangle;
            double height = box.TopRight.Y - box.BottomLeft.Y;
            double width = box.TopRight.X - box.BottomLeft.X;
            if (height <= 0 || width <= 0 || string.IsNullOrEmpty(letter.Value))
                continue;
            glyphs.Add(new TextGlyph(
                letter.Value,
                box.BottomLeft.X,
                InkUnits.FlipY(box.TopRight.Y, page.Height),
                width,
                height));
        }
        return LetterGrouping.Group(glyphs).ToList();
    }
}
