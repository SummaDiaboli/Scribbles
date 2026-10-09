using Scribbles.Core;
using Shouldly;

namespace Scribbles.Unit.Tests;

/// <summary>JSON drift detector for the note model (plain file comparison —
/// no diff-tool popups by design).</summary>
public sealed class ModelSnapshotTests
{
    [Fact]
    public void RichModel_MatchesSnapshot()
    {
        var doc = new NoteDocument("hash", new[]
        {
            new NotePage(1, 595, 842,
                new[]
                {
                    new InkStroke("s1", 1, ToolKind.Pen, 0xFF112233, 2.5,
                        new[] { new StrokePoint(10, 20, 0.8f), new StrokePoint(30, 40, 0.4f) },
                        Mode: InkHandleMode.Copyable, RecognizedText: "hi"),
                },
                new[]
                {
                    new TextBoxItem { Id = "t1", PageNumber = 1, X = 10, Y = 100, Width = 200, Height = 30, Text = "hello", FontSizePt = 14, ColorArgb = 0xFF000000, Kind = TextBoxKind.Converted },
                }),
        });
        string json = NoteSerializer.ToJson(doc);
        string snapshot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "ModelSnapshot.verified.txt"));
        if (!File.Exists(snapshot))
        {
            File.WriteAllText(snapshot, json);
            throw new Xunit.Sdk.XunitException("New snapshot written — review it, then re-run.");
        }
        // Canonicalize (sorted keys): source-generated member order is not
        // stable across builds, but drift in shape or values must fail.
        Canon(File.ReadAllText(snapshot)).ShouldBe(Canon(json));
    }

    private static string Canon(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json.Replace("\r\n", "\n"));
        return CanonElement(doc.RootElement);
    }

    private static string CanonElement(System.Text.Json.JsonElement element) =>
        element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Object => "{" + string.Join(",",
                    element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)
                        .Select(p => System.Text.Json.JsonSerializer.Serialize(p.Name) + ":" + CanonElement(p.Value))) + "}",
            System.Text.Json.JsonValueKind.Array => "[" + string.Join(",",
                    element.EnumerateArray().Select(CanonElement)) + "]",
            _ => element.GetRawText(),
        };
    }
