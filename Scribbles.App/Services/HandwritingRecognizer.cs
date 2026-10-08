using System.Numerics;
using Windows.Foundation;
using Windows.UI.Input.Inking;
using Scribbles.Core;

namespace Scribbles.App.Services;

/// <summary>
/// Windows handwriting recognition (WinRT InkRecognizerContainer, offline,
/// uses installed language packs). Returns null when no recognizer exists.
/// </summary>
public sealed class HandwritingRecognizer
{
    private InkRecognizerContainer? _container;
    private bool? _available;

    /// <summary>True when at least one handwriting recognizer is installed.</summary>
    public bool IsAvailable
    {
        get
        {
            _available ??= CheckAvailable();
            return _available.Value;
        }
    }

    private bool CheckAvailable()
    {
        try
        {
            _container = new InkRecognizerContainer();
            return _container.GetRecognizers().Count > 0;
        }
        catch
        {
            _container = null;
            return false;
        }
    }

    /// <summary>Recognizes strokes, joining line results with spaces. Null when unavailable.</summary>
    public async Task<string?> RecognizeAsync(
        IReadOnlyList<Core.InkStroke> strokes, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || _container is null || strokes.Count == 0)
            return null;
        try
        {
            var strokeContainer = new InkStrokeContainer();
            var builder = new InkStrokeBuilder();
            foreach (var stroke in strokes)
            {
                if (stroke.Points.Count == 0)
                    continue;
                var points = stroke.Points.Select(p =>
                    new InkPoint(new Point(p.X, p.Y), p.Pressure));
                strokeContainer.AddStroke(builder.CreateStrokeFromInkPoints(
                    points, Matrix3x2.Identity));
            }
            if ((uint)strokeContainer.GetStrokes().Count == 0)
                return null;
            var results = await _container
                .RecognizeAsync(strokeContainer, InkRecognitionTarget.All)
                .AsTask(cancellationToken);
            var texts = results
                .Select(r => r.GetTextCandidates().FirstOrDefault())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();
            return texts.Count == 0 ? null : string.Join(" ", texts);
        }
        catch
        {
            return null;
        }
    }
}
