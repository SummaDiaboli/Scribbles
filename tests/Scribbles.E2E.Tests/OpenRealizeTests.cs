using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using Scribbles.App;
using Scribbles.App.Services;
using Scribbles.App.ViewModels;
using Scribbles.Core;
using Scribbles.Pdf;
using Shouldly;
using static Scribbles.E2E.Tests.WpfTestHost;

namespace Scribbles.E2E.Tests;

/// <summary>
/// Regression for the reported "reopened doc is blank + undo dead" bug:
/// containers materialize asynchronously, so the session must stash model
/// strokes at open and restore/hook them as canvases realize.
/// Real store + real renderer, canvases never shown.
/// </summary>
public sealed class OpenRealizeTests
{
    private static string WritePdf(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] BasePdf()
    {
        using var doc = new PdfSharp.Pdf.PdfDocument();
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
        gfx.DrawLine(PdfSharp.Drawing.XPens.Black, 50, 100, 300, 100);
        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    private static InkStroke SavedStroke() => new(
        "s1", 1, ToolKind.Pen, 0xFF112233, 2.5,
        new[] { new StrokePoint(100, 200, 0.8f), new StrokePoint(150, 220, 0.4f) });

    [Fact]
    public void Open_StashesModel_ThenRealizationRestoresStrokesAndHooks() => Sta(() =>
    {
        EnsureThemedApp("Light");
        var store = new ScribblesPdfStore();
        var model = new NoteDocument("", new[]
        {
            new NotePage(1, 595, 842, new[] { SavedStroke() }, Array.Empty<TextBoxItem>()),
        });
        string path = WritePdf(store.Save(BasePdf(), model));

        var converter = new InkModelConverter();
        var inks = new List<InkCanvas>();
        var session = new DocumentSession(
            store, converter, new StrokeHistory(converter),
            new PenToolbarController(), () => inks);
        var vm = new MainViewModel(Scribbles.App.App.Themes);

        session.OpenDocument(path, vm);
        vm.Pages.Count.ShouldBe(1);

        // Containers don't exist yet: nothing may be lost, nothing hooked.
        inks.ShouldBeEmpty();

        // Realization (asynchronous in the app): canvas appears later.
        var canvas = new InkCanvas { DataContext = vm.Pages[0] };
        inks.Add(canvas);
        session.SyncContainers();

        canvas.Strokes.Count.ShouldBe(1, "stashed stroke must be restored on realize");
        var pt = canvas.Strokes[0].StylusPoints[0];
        double fx = vm.Pages[0].DipPerPtX;
        pt.X.ShouldBe(100 * fx, tolerance: 0.5);
        pt.Y.ShouldBe(200 * vm.Pages[0].DipPerPtY, tolerance: 0.5);

        // Hooks must be alive: drawing now arms undo (was dead after reopen).
        session.CanUndoAny().ShouldBeFalse();
        canvas.Strokes.Add(new Stroke(new StylusPointCollection(new[]
        {
            new StylusPoint(10, 10),
            new StylusPoint(60, 60),
        })));
        session.CanUndoAny().ShouldBeTrue("post-reopen strokes must be tracked");

        // Undo/redo replace the collection: tracking must survive the swap.
        session.Undo(new[] { 1 });
        canvas.Strokes.Add(new Stroke(new StylusPointCollection(new[]
        {
            new StylusPoint(5, 5),
            new StylusPoint(15, 15),
        })));
        session.CanUndoAny().ShouldBeTrue("tracking must survive collection replacement");

        // And everything still round-trips byte-identical in model terms.
        var roundTripped = store.Load(store.Save(File.ReadAllBytes(path), session.CollectModel(vm.Pages)));
        roundTripped.HashesMatch.ShouldBeTrue();
        roundTripped.Model!.Pages[0].Strokes.Count.ShouldBe(2);
    });
}
