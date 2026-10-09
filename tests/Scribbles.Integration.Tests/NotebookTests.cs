using Scribbles.Core;
using Scribbles.Pdf;
using Shouldly;

namespace Scribbles.Integration.Tests;

/// <summary>Blank notebooks round-trip through the real store.</summary>
public sealed class NotebookTests
{
    [Fact]
    public void BlankNotebook_SavesAndReopensWithStrokes()
    {
        byte[] blank = NotebookDocuments.CreateBlank(new[]
        {
            (StandardPageSizes.A4WidthPt, StandardPageSizes.A4HeightPt),
            (StandardPageSizes.A4WidthPt, StandardPageSizes.A4HeightPt),
        });
        var store = new ScribblesPdfStore();
        var model = new NoteDocument("", new[]
        {
            new NotePage(1, StandardPageSizes.A4WidthPt, StandardPageSizes.A4HeightPt,
                new[] { new InkStroke("n1", 1, ToolKind.Pen, 0xFF000000, 2.5,
                    new[] { new StrokePoint(100, 100), new StrokePoint(200, 150) }) },
                Array.Empty<TextBoxItem>()),
            new NotePage(2, StandardPageSizes.A4WidthPt, StandardPageSizes.A4HeightPt,
                Array.Empty<InkStroke>(), Array.Empty<TextBoxItem>()),
        });
        var loaded = store.Load(store.Save(blank, model));
        loaded.HashesMatch.ShouldBeTrue();
        loaded.Pages.Count.ShouldBe(2);
        loaded.Model!.Pages[0].Strokes.Count.ShouldBe(1);
    }

    [Fact]
    public void AddBlankPage_GrowsDocument()
    {
        byte[] one = NotebookDocuments.CreateBlank(new[]
        {
            (StandardPageSizes.A4WidthPt, StandardPageSizes.A4HeightPt),
        });
        byte[] two = NotebookDocuments.AddBlankPage(
            one, StandardPageSizes.A4WidthPt, StandardPageSizes.A4HeightPt);
        new ScribblesPdfStore().ReadPages(two).Count.ShouldBe(2);
    }
}
