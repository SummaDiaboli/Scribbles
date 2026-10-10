namespace Scribbles.Core;

/// <summary>Sidecar location for degraded-mode ink (Sharp-rejecting PDFs).</summary>
public static class SidecarPath
{
    public const string Extension = ".scribbles.json";

    public static string ForPdf(string pdfPath) =>
        Path.ChangeExtension(pdfPath, Extension);

    public static bool IsSidecar(string path) =>
        path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);
}
