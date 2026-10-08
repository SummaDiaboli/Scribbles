using Scribbles.App.Services;
using Scribbles.Core;
using Shouldly;
using Xunit.Abstractions;
using static Scribbles.E2E.Tests.WpfTestHost;

namespace Scribbles.E2E.Tests;

/// <summary>
/// WinRT handwriting recognition smoke: skipped with a clear reason when no
/// recognizer (language pack) is installed, otherwise must not throw.
/// </summary>
public sealed class RecognitionTests
{
    private readonly ITestOutputHelper _out;

    public RecognitionTests(ITestOutputHelper output) => _out = output;

    private static InkStroke Scribble() => new(
        "s", 1, ToolKind.Pen, 0xFF000000, 2.0,
        new[]
        {
            new StrokePoint(10, 50), new StrokePoint(10, 20), new StrokePoint(10, 50),
            new StrokePoint(30, 50), new StrokePoint(30, 20), new StrokePoint(30, 50),
        });

    [Fact]
    public void Recognize_Smoke()
    {
        Sta(async () =>
        {
            var recognizer = new HandwritingRecognizer();
            if (!recognizer.IsAvailable)
            {
                _out.WriteLine("SKIPPED: no handwriting recognizer installed (Windows Settings → Language → Handwriting).");
                return;
            }
            string? text = await recognizer.RecognizeAsync(new[] { Scribble() });
            _out.WriteLine($"Recognized: {text ?? "<null>"}");
            // Smoke only: must not throw; quality depends on the engine.
            (text is null || text.Length >= 0).ShouldBeTrue();
        });
    }
}
