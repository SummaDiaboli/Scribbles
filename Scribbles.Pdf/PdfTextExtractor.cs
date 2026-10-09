using Scribbles.Core;

namespace Scribbles.Pdf;

/// <summary>
/// Extracts native PDF text words per page (y-DOWN points, matching the
/// Core coordinate convention). Scanned/image-only pages yield no words.
/// Single-open via <see cref="PdfPageContentReader"/>.
/// </summary>
public static class PdfTextExtractor
{
    public static IReadOnlyList<IReadOnlyList<PdfWord>> ExtractPages(byte[] pdfBytes) =>
        PdfPageContentReader.Read(pdfBytes).Words;
}
