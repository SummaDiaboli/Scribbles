namespace Scribbles.Core;

/// <summary>One document-outline entry (PDF bookmark) with nesting.</summary>
public sealed record PdfBookmark(
    string Title,
    int PageNumber,
    IReadOnlyList<PdfBookmark> Children);
