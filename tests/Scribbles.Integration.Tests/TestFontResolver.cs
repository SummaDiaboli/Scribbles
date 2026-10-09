using PdfSharp.Fonts;

namespace Scribbles.Integration.Tests;

/// <summary>
/// Test-only font resolver (Windows Arial) so PdfSharp can read/write
/// artifacts involving fonts. Never shipped in the app.
/// </summary>
internal sealed class TestFontResolver : IFontResolver
{
    public static readonly TestFontResolver Instance = new();

    private static byte[] ArialBytes()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        return File.ReadAllBytes(path);
    }

    public byte[]? GetFont(string faceName) => ArialBytes();

    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) =>
        new("Arial#");
}
