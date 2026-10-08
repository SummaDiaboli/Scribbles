using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scribbles.Core;

/// <summary>
/// JSON round-trip for the note model (stored in the PDF /PieceInfo slot)
/// plus SHA-256 hashing of page base content for change detection.
/// </summary>
public static class NoteSerializer
{
    public const int ModelVersion = 1;

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
            if (stored is null || stored.Version != ModelVersion)
                return null;
            return stored.Document;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    public static string Sha256Hex(string text) =>
        Sha256Hex(Encoding.UTF8.GetBytes(text));

    private sealed record StoredModel(int Version, NoteDocument Document);
}
