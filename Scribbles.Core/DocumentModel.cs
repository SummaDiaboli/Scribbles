namespace Scribbles.Core;

/// <summary>Minimal M1 document model: an opened PDF and its page sizes.</summary>
public sealed record DocumentPage(int Number, double WidthPt, double HeightPt, int Rotation = 0);

public sealed record OpenDocument(string FilePath, IReadOnlyList<DocumentPage> Pages)
{
    public int PageCount => Pages.Count;
}
