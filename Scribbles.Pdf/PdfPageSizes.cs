namespace Scribbles.Pdf;

public sealed record PdfPageSize(double Width, double Height);

/// <summary>True page sizes in points (MediaBox) via PdfPig (single-open).</summary>
public static class PdfPageSizes
{
    public static IReadOnlyList<PdfPageSize> Read(byte[] pdfBytes) =>
        PdfPageContentReader.Read(pdfBytes).Sizes;
}
