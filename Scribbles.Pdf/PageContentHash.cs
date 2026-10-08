using PdfSharp;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using Scribbles.Core;

namespace Scribbles.Pdf;

/// <summary>Stable hash of a page's base content for change detection.</summary>
internal static class PageContentHash
{
    internal static int RotationDegrees(PdfPage page) => page.Rotation switch
    {
        PageRotation.Rotate90DegreesRight => 90,
        PageRotation.RotateUpsideDown => 180,
        PageRotation.Rotate90DegreesLeft => 270,
        _ => 0,
    };

    internal static string Compute(PdfPage page)
    {
        try
        {
            using var hash = System.Security.Cryptography.SHA256.Create();
            string header = $"w{page.Width.Point:0.###}h{page.Height.Point:0.###}r{RotationDegrees(page)};";
            byte[] headerBytes = System.Text.Encoding.UTF8.GetBytes(header);
            hash.TransformBlock(headerBytes, 0, headerBytes.Length, null, 0);
            if (page.Contents is PdfContents contents)
            {
                foreach (PdfContent content in contents)
                {
                    byte[]? bytes = ((PdfDictionary)content).Stream?.Value;
                    if (bytes is not null && bytes.Length > 0)
                        hash.TransformBlock(bytes, 0, bytes.Length, null, 0);
                }
            }
            hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToHexString(hash.Hash!);
        }
        catch
        {
            return $"fallback-w{page.Width.Point:0.###}h{page.Height.Point:0.###}r{RotationDegrees(page)}";
        }
    }
}
