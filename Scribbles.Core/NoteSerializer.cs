using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scribbles.Core;

/// <summary>
/// JSON round-trip for the note model (stored in the PDF /PieceInfo slot)
/// plus SHA-256 hashing of page base content for change detection.
/// v1 payloads migrate (empty text boxes, default stroke modes).
/// </summary>
public static class NoteSerializer
{
    public const int ModelVersion = 2;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string ToJson(NoteDocument doc) =>
        JsonSerializer.Serialize(new StoredModel(ModelVersion, doc), Options);

    public static NoteDocument? FromJson(string json)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<StoredModel>(json, Options);
            if (stored is not null)
            {
                if (stored.Version == ModelVersion && stored.Document is not null)
                    return Normalize(stored.Document);
                if (stored.Version == 1)
                    return MigrateV1(json);
            }
            return MigrateV1(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static NoteDocument? MigrateV1(string json)
    {
        try
        {
            var stored = JsonSerializer.Deserialize<StoredModelV1>(json, Options);
            if (stored is null || stored.Version != 1 || stored.Document is null)
                return null;
            var pages = stored.Document.Pages.Select(p => new NotePage(
                p.Number, p.WidthPt, p.HeightPt,
                p.Strokes.Select(s => new InkStroke(
                    s.Id, s.PageNumber, s.Tool, s.ColorArgb, s.WidthPt, s.Points,
                    InkHandleMode.Keep, "")).ToList(),
                Array.Empty<TextBoxItem>())).ToList();
            return new NoteDocument(stored.Document.SourceHash, pages);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static NoteDocument Normalize(NoteDocument doc) => doc with
    {
        Pages = doc.Pages.Select(p => p with
        {
            Strokes = p.Strokes.Select(s => s with
            {
                Mode = s.Mode,
                RecognizedText = s.RecognizedText ?? "",
            }).ToList(),
            TextBoxes = p.TextBoxes ?? Array.Empty<TextBoxItem>(),
        }).ToList(),
    };

    public static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    public static string Sha256Hex(string text) =>
        Sha256Hex(Encoding.UTF8.GetBytes(text));

    private sealed record StoredModel(int Version, NoteDocument? Document);

    private sealed record StoredModelV1(int Version, NoteDocumentV1? Document);
    private sealed record NoteDocumentV1(string SourceHash, List<NotePageV1> Pages);
    private sealed record NotePageV1(int Number, double WidthPt, double HeightPt, List<InkStrokeV1> Strokes);
    private sealed record InkStrokeV1(string Id, int PageNumber, ToolKind Tool, uint ColorArgb, double WidthPt, List<StrokePoint> Points);
}
