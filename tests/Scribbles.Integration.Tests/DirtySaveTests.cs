using Scribbles.Core;
using Scribbles.Pdf;
using Shouldly;

namespace Scribbles.Integration.Tests;

/// <summary>Dirty-page saves: untouched pages keep their annotations and the
/// merged model still round-trips.</summary>
public sealed class DirtySaveTests
{
    private static byte[] TwoPageBase()
    {
        using var doc = new PdfSharp.Pdf.PdfDocument();
        doc.AddPage().Size = PdfSharp.PageSize.A4;
        doc.AddPage().Size = PdfSharp.PageSize.A4;
        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    private static InkStroke Stroke(string id, int page, double x) => new(
        id, page, ToolKind.Pen, 0xFF000000u, 2.0,
        new[] { new StrokePoint(x, 100, 0.5f), new StrokePoint(x + 50, 150, 0.5f) });

    private static NoteDocument Model(params NotePage[] pages) => new("", pages);

    [Fact]
    public void Save_DirtyPageOnly_MergesModel()
    {
        var store = new ScribblesPdfStore();
        byte[] blank = TwoPageBase();
        byte[] saved1 = store.Save(blank, Model(
            new NotePage(1, 595, 842, new[] { Stroke("a1", 1, 100) }, Array.Empty<TextBoxItem>()),
            new NotePage(2, 595, 842, new[] { Stroke("b1", 2, 100) }, Array.Empty<TextBoxItem>())));

        byte[] saved2 = store.Save(saved1, Model(
            new NotePage(1, 595, 842, new[] { Stroke("a1", 1, 100) }, Array.Empty<TextBoxItem>()),
            new NotePage(2, 595, 842, new[] { Stroke("b2", 2, 300) }, Array.Empty<TextBoxItem>())),
            new HashSet<int> { 2 });

        var loaded = store.Load(saved2);
        loaded.HashesMatch.ShouldBeTrue("model must survive a partial save");
        loaded.Model!.Pages[0].Strokes.Count.ShouldBe(1);
        loaded.Model.Pages[0].Strokes[0].Id.ShouldBe("a1", "clean page keeps its stroke");
        loaded.Model.Pages[1].Strokes[0].Id.ShouldBe("b2", "dirty page picks up the edit");
    }

    [Fact]
    public void Save_NullDirty_RebuildsAll_AsBefore()
    {
        var store = new ScribblesPdfStore();
        byte[] saved = store.Save(TwoPageBase(), Model(
            new NotePage(1, 595, 842, new[] { Stroke("a1", 1, 100) }, Array.Empty<TextBoxItem>()),
            new NotePage(2, 595, 842, Array.Empty<InkStroke>(), Array.Empty<TextBoxItem>())));
        var loaded = store.Load(saved);
        loaded.HashesMatch.ShouldBeTrue();
        loaded.Model!.Pages[0].Strokes.Count.ShouldBe(1);
    }
}
