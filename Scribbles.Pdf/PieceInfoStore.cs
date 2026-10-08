using PdfSharp.Pdf;

namespace Scribbles.Pdf;

/// <summary>Reads/writes our app-private model JSON in a page's /PieceInfo dict.</summary>
internal static class PieceInfoStore
{
    internal const string AppKey = "Scribbles";

    internal static void Write(PdfPage page, string modelJson, IReadOnlyList<string> pageHashes)
    {
        var app = new PdfDictionary();
        app.Elements.SetInteger("/Version", Core.NoteSerializer.ModelVersion);
        app.Elements["/Model"] = new PdfString(modelJson);
        var hashArray = new PdfArray();
        foreach (var h in pageHashes)
            hashArray.Elements.Add(new PdfString(h));
        app.Elements["/PageHashes"] = hashArray;
        var piece = new PdfDictionary();
        piece.Elements["/" + AppKey] = app;
        page.Elements["/PieceInfo"] = piece;
    }

    internal static bool TryRead(PdfPage page, out string? modelJson, out List<string>? pageHashes)
    {
        modelJson = null;
        pageHashes = null;
        if (page.Elements["/PieceInfo"] is PdfDictionary piece
            && piece.Elements["/" + AppKey] is PdfDictionary app
            && app.Elements["/Model"] is PdfString modelString
            && app.Elements["/PageHashes"] is PdfArray stored)
        {
            modelJson = modelString.Value;
            pageHashes = stored.Elements.OfType<PdfString>().Select(s => s.Value).ToList();
            return true;
        }
        return false;
    }
}
