using PdfSharp;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
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
    /// <param name="dirtyPages">1-based pages to rebuild annotations for;
    /// null rebuilds all (full save). Clean pages keep their on-disk
    /// annotations, which already match the model from the last save.</param>
    public byte[] Save(byte[] originalPdfBytes, NoteDocument model, IReadOnlySet<int>? dirtyPages = null)
    {
        using var stream = new MemoryStream(originalPdfBytes);
        using var doc = PdfSharp.Pdf.IO.PdfReader.Open(stream, PdfDocumentOpenMode.Modify);

        var pages = ReadPages(doc);
        var hashes = pages.Select(p => p.BaseHash).ToList();
        var stamped = model with { SourceHash = NoteSerializer.Sha256Hex(string.Join("|", hashes)) };
        string json = NoteSerializer.ToJson(stamped);

        for (int i = 0; i < doc.PageCount; i++)
        {
            if (dirtyPages is not null && !dirtyPages.Contains(i + 1))
                continue;
            var page = doc.Pages[i];
            InkAnnotationMapper.RemoveOwnAnnotations(page);
            var strokes = stamped.Pages.FirstOrDefault(p => p.Number == i + 1)?.Strokes
                ?? Array.Empty<InkStroke>();
            foreach (var stroke in strokes)
                InkAnnotationMapper.Add(doc, page, stroke, pages[i].HeightPt);
        }

        if (doc.PageCount > 0)
        {
            // Model JSON lives on page 1 only (TryRead reads page 1 only):
            // per-page copies were never read and multiplied file size by N.
            PieceInfoStore.Write(doc.Pages[0], json, hashes);
            // Drop stale copies from older saves on the other pages.
            for (int i = 1; i < doc.PageCount; i++)
                RemoveOwnPieceInfo(doc.Pages[i]);
        }

        using var outStream = new MemoryStream();
        doc.Save(outStream);
        return outStream.ToArray();
    }

    /// <summary>
    /// Flattened export for sharing: base content + burned-in ink paths,
    /// visible converted/edited text, invisible copyable text — no /Ink
    /// annotations, no PieceInfo (not re-editable by design).
    /// </summary>
    public byte[] ExportFlat(byte[] basePdfBytes, NoteDocument model)
    {
        using var stream = new MemoryStream(basePdfBytes);
        using var doc = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
        var pages = ReadPages(doc);

        // Serial DOM prep (PdfSharp objects are not thread-safe).
        var models = new NotePage?[doc.PageCount];
        var heights = new double[doc.PageCount];
        for (int i = 0; i < doc.PageCount; i++)
        {
            var page = doc.Pages[i];
            heights[i] = pages[i].HeightPt;
            InkAnnotationMapper.RemoveOwnAnnotations(page);
            RemoveOwnPieceInfo(page);
            models[i] = model.Pages.FirstOrDefault(p => p.Number == i + 1);
        }

        // Chunk building is pure CPU per page (no shared state) → parallel.
        // The array preserves page order, so output bytes are deterministic.
        var chunks = new string?[doc.PageCount];
        Parallel.For(0, doc.PageCount, i =>
        {
            if (models[i] is not null)
                chunks[i] = BuildFlatChunk(models[i]!, heights[i]);
        });

        // Serial DOM write-back.
        for (int i = 0; i < doc.PageCount; i++)
        {
            if (!string.IsNullOrEmpty(chunks[i]))
            {
                var page = doc.Pages[i];
                EnsureCopyFont(page);
                AppendContentStream(doc, page, System.Text.Encoding.ASCII.GetBytes(chunks[i]!));
            }
        }

        using var outStream = new MemoryStream();
        doc.Save(outStream);
        return outStream.ToArray();
    }

    /// <summary>Pure per-page content builder (thread-safe): ink paths,
    /// visible converted text, invisible copyable text.</summary>
    private static string BuildFlatChunk(NotePage pageModel, double pageHeight)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var stroke in pageModel.Strokes)
        {
            if (stroke.Points.Count == 0)
                continue;
            var (a, r, g, b) = InkUnits.FromArgb(stroke.ColorArgb);
            double alpha = stroke.Tool == ToolKind.Highlighter ? 0.5 : 1.0;
            sb.Append(PdfContentBuilder.FlattenStroke(
                stroke.Points, pageHeight, Math.Max(0.5, stroke.WidthPt),
                r / 255.0 * alpha + (1 - alpha), // highlighter washes toward white
                g / 255.0 * alpha + (1 - alpha),
                b / 255.0 * alpha + (1 - alpha)));
            if (stroke.Mode == InkHandleMode.Copyable && !string.IsNullOrWhiteSpace(stroke.RecognizedText))
            {
                var (minX, minY, _, maxY) = stroke.Bounds();
                double size = Math.Clamp((maxY - minY) / 2.0, 8, 24);
                sb.Append(PdfContentBuilder.InvisibleText(
                    stroke.RecognizedText, minX, minY, size, pageHeight));
            }
        }

        foreach (var box in pageModel.TextBoxes)
        {
            if (box.Kind == TextBoxKind.EditedNative)
                sb.Append(PdfContentBuilder.CoverRect(box.X, box.Y, box.Width, box.Height, pageHeight));
            var (a, r, g, b) = InkUnits.FromArgb(box.ColorArgb);
            sb.Append(PdfContentBuilder.VisibleText(
                box.Text, box.X, box.Y, Math.Max(6, box.FontSizePt), pageHeight,
                r / 255.0, g / 255.0, b / 255.0));
        }

        return sb.ToString();
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

    /// <summary>
    /// Appends a raw content stream to the page: rebuilds /Contents as an
    /// array (existing entries preserved by reference) plus the new stream.
    /// Existing streams stay untouched (no decode/re-encode round-trip).
    /// </summary>
    private static void AppendContentStream(PdfDocument doc, PdfPage page, byte[] bytes)
    {
        var streamDict = new PdfDictionary(doc);
        streamDict.CreateStream(bytes);
        doc.Internals.AddObject(streamDict);
        if (streamDict.Reference is null)
            throw new InvalidOperationException("Could not indirect the appended content stream.");

        var array = new PdfArray();
        var existing = page.Elements["/Contents"];
        if (existing is PdfArray oldArray)
        {
            foreach (var item in oldArray.Elements)
                array.Elements.Add(item);
        }
        else if (existing is not null)
        {
            array.Elements.Add(existing);
        }
        array.Elements.Add(streamDict.Reference);
        page.Elements["/Contents"] = array;
    }

    private static void RemoveOwnPieceInfo(PdfPage page)
    {
        if (page.Elements["/PieceInfo"] is PdfDictionary piece)
            piece.Elements.Remove("/" + PieceInfoStore.AppKey);
    }

    private static PdfDictionary? Deref(PdfItem? item) => item switch
    {
        PdfDictionary dict => dict,
        PdfReference reference => reference.Value as PdfDictionary,
        _ => null,
    };

    /// <summary>Injects a base-14 Helvetica font (no embedding needed).</summary>
    private static void EnsureCopyFont(PdfPage page)
    {
        var resources = Deref(page.Elements["/Resources"]) ?? new PdfDictionary();
        if (page.Elements["/Resources"] is null)
            page.Elements["/Resources"] = resources;
        var fonts = Deref(resources.Elements["/Font"]);
        if (fonts is null)
        {
            fonts = new PdfDictionary();
            resources.Elements["/Font"] = fonts;
        }
        if (fonts.Elements["/" + PdfContentBuilder.CopyFontName] is null)
        {
            var font = new PdfDictionary();
            font.Elements.SetName("/Type", "/Font");
            font.Elements.SetName("/Subtype", "/Type1");
            font.Elements.SetName("/BaseFont", "/Helvetica");
            font.Elements.SetName("/Encoding", "/WinAnsiEncoding");
            fonts.Elements["/" + PdfContentBuilder.CopyFontName] = font;
        }
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
