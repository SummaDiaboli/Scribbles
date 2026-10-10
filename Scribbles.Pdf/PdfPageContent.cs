using UglyToad.PdfPig;
using Scribbles.Core;

namespace Scribbles.Pdf;

/// <summary>One PdfPig open yielding sizes, text words, and bookmarks.</summary>
public sealed record PdfPageContent(
    IReadOnlyList<PdfPageSize> Sizes,
    IReadOnlyList<IReadOnlyList<PdfWord>> Words,
    IReadOnlyList<PdfBookmark> Bookmarks);

/// <summary>
/// Single-open page read: sizes + words in one <c>PdfDocument.Open</c>.
/// Documents with heavy fonts pay the parse cost once instead of twice.
/// </summary>
public static class PdfPageContentReader
{
    public static PdfPageContent Read(byte[] pdfBytes)
    {
        var sizes = new List<PdfPageSize>();
        var words = new List<IReadOnlyList<PdfWord>>();
        var bookmarks = new List<PdfBookmark>();
        try
        {
            using var document = PdfDocument.Open(pdfBytes);
            foreach (var page in document.GetPages())
            {
                sizes.Add(new PdfPageSize(page.Width, page.Height));
                var pageWords = ExtractWords(page);
                if (pageWords.Count == 0)
                    pageWords = ExtractFromLetters(page);
                words.Add(pageWords);
            }
            if (document.TryGetBookmarks(out var outline) && outline?.Roots is not null)
            {
                foreach (var root in outline.Roots)
                {
                    var mapped = MapBookmark(root);
                    if (mapped is not null)
                        bookmarks.Add(mapped);
                }
            }
        }
        catch
        {
            // Encrypted or unreadable: empty content.
        }
        return new PdfPageContent(sizes, words, bookmarks);
    }

    /// <summary>Document bookmarks map to pages; external/embedded ones are skipped.</summary>
    private static PdfBookmark? MapBookmark(UglyToad.PdfPig.Outline.BookmarkNode node)
    {
        var children = new List<PdfBookmark>();
        foreach (var child in node.Children)
        {
            var mapped = MapBookmark(child);
            if (mapped is not null)
                children.Add(mapped);
        }
        if (node is UglyToad.PdfPig.Outline.DocumentBookmarkNode doc)
            return new PdfBookmark(doc.Title, doc.PageNumber, children);
        return children.Count > 0 ? new PdfBookmark(node.Title, children[0].PageNumber, children) : null;
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
