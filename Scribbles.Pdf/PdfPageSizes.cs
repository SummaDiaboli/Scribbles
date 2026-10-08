using UglyToad.PdfPig;

namespace Scribbles.Pdf;

public sealed record PdfPageSize(double Width, double Height);

/// <summary>True page sizes in points (MediaBox) via PdfPig.</summary>
public static class PdfPageSizes
{
    public static IReadOnlyList<PdfPageSize> Read(byte[] pdfBytes)
    {
        var sizes = new List<PdfPageSize>();
        try
        {
            using var pdf = PdfDocument.Open(pdfBytes);
            foreach (var page in pdf.GetPages())
                sizes.Add(new PdfPageSize(page.Width, page.Height));
        }
        catch
        {
            // Encrypted or unreadable.
        }
        return sizes;
    }
}
