using PdfSharp;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Scribbles.Core;

namespace Scribbles.Pdf;

/// <summary>Base page info: size plus hash of the original page content.</summary>
public sealed record StoredPage(int Number, double WidthPt, double HeightPt, int Rotation, string BaseHash);

/// <summary>Result of opening a PDF: pages, plus either our note model
/// (hashes match) or fallback-imported /Ink strokes.</summary>
public sealed record LoadedPdf(
    IReadOnlyList<StoredPage> Pages,
    NoteDocument? Model,
    bool HashesMatch,
    IReadOnlyList<InkStroke> FallbackInk);

/// <summary>
/// M2 round-trip store. Writes three layers into one PDF:
/// 1. Ink as standard /Ink annotations with painted /AP appearance streams,
/// 2. (M4) converted text / copyable layer — not written yet,
/// 3. Full note model JSON in each page's /PieceInfo /Scribbles dict.
/// </summary>
public sealed class ScribblesPdfStore
{
    public byte[] Save(byte[] originalPdfBytes, NoteDocument model)
    {
        using var stream = new MemoryStream(originalPdfBytes);
        using var doc = PdfSharp.Pdf.IO.PdfReader.Open(stream, PdfDocumentOpenMode.Modify);

        var pages = ReadPages(doc);
        var hashes = pages.Select(p => p.BaseHash).ToList();
        var stamped = model with { SourceHash = NoteSerializer.Sha256Hex(string.Join("|", hashes)) };
        string json = NoteSerializer.ToJson(stamped);

        for (int i = 0; i < doc.PageCount; i++)
        {
            var page = doc.Pages[i];
            InkAnnotationMapper.RemoveOwnAnnotations(page);
            var strokes = stamped.Pages.FirstOrDefault(p => p.Number == i + 1)?.Strokes
                ?? Array.Empty<InkStroke>();
            foreach (var stroke in strokes)
                InkAnnotationMapper.Add(doc, page, stroke, pages[i].HeightPt);
            PieceInfoStore.Write(page, json, hashes);
        }

        using var outStream = new MemoryStream();
        doc.Save(outStream);
        return outStream.ToArray();
    }

    public LoadedPdf Load(byte[] pdfBytes)
    {
        using var stream = new MemoryStream(pdfBytes);
        using var doc = PdfSharp.Pdf.IO.PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        var pages = ReadPages(doc);

        NoteDocument? model = null;
        bool hashesMatch = false;
        if (doc.PageCount > 0 && PieceInfoStore.TryRead(doc.Pages[0], out string? json, out var storedHashes))
        {
            var candidate = NoteSerializer.FromJson(json!);
            if (candidate is not null
                && storedHashes!.Count == pages.Count
                && storedHashes.Zip(pages, (s, p) => s == p.BaseHash).All(b => b))
            {
                model = candidate;
                hashesMatch = true;
            }
        }

        var fallback = new List<InkStroke>();
        if (!hashesMatch)
        {
            for (int i = 0; i < doc.PageCount; i++)
                fallback.AddRange(InkAnnotationMapper.Read(doc.Pages[i], i + 1, pages[i].HeightPt));
        }

        return new LoadedPdf(pages, model, hashesMatch, fallback);
    }

    public IReadOnlyList<StoredPage> ReadPages(byte[] pdfBytes)
    {
        using var stream = new MemoryStream(pdfBytes);
        using var doc = PdfSharp.Pdf.IO.PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        return ReadPages(doc);
    }

    private static List<StoredPage> ReadPages(PdfDocument doc)
    {
        var pages = new List<StoredPage>(doc.PageCount);
        for (int i = 0; i < doc.PageCount; i++)
        {
            var page = doc.Pages[i];
            pages.Add(new StoredPage(
                i + 1, page.Width.Point, page.Height.Point, PageContentHash.RotationDegrees(page),
                PageContentHash.Compute(page)));
        }
        return pages;
    }
}
