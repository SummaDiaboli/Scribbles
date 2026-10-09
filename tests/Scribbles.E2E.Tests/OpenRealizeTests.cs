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
    private static Func<int, List<TextBoxItem>> BoxesOf(MainViewModel vm) => page =>
        vm.Pages.FirstOrDefault(p => p.Number == page)?.TextBoxes.Select(b => b.Clone()).ToList()
        ?? new List<TextBoxItem>();

    private static Action<int, List<TextBoxItem>> SetBoxesOf(MainViewModel vm) => (page, boxes) =>
    {
        var target = vm.Pages.FirstOrDefault(p => p.Number == page);
        if (target is null)
            return;
        target.TextBoxes.Clear();
        foreach (var box in boxes)
            target.TextBoxes.Add(box);
    };

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
    public void Open_StashesModel_ThenRealizationRestoresStrokesAndHooks() => Sta(async () =>
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
        var vm = new MainViewModel(Scribbles.App.App.Themes);
        var session = new DocumentSession(
            store, converter, new StrokeHistory(converter, BoxesOf(vm)), new PenToolbarController(),
            () => inks, SetBoxesOf(vm));

        await session.OpenDocumentAsync(path, vm);
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

    [Fact]
    public void SaveAs_ReturnsNewPath_AndLeavesOriginalUntouched() => Sta(async () => {        EnsureThemedApp("Light");
        var store = new ScribblesPdfStore();
        string original = WritePdf(store.Save(BasePdf(),
            new NoteDocument("", Array.Empty<NotePage>())));
        long before = new FileInfo(original).Length;

        var converter = new InkModelConverter();
        var inks = new List<InkCanvas>();
        var vm = new MainViewModel(Scribbles.App.App.Themes);
        var session = new DocumentSession(
            store, converter, new StrokeHistory(converter, BoxesOf(vm)), new PenToolbarController(),
            () => inks, SetBoxesOf(vm));
        await session.OpenDocumentAsync(original, vm);

        string target = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
        try
        {
            string? saved = await session.SaveAsync(vm, null, () => target, saveAs: true);
            saved.ShouldBe(target, "Save As must report the new path for recents");
            session.CurrentPath.ShouldBe(target);
            new FileInfo(original).Length.ShouldBe(before, "Save As must not touch the original");
            File.Exists(target).ShouldBeTrue("Save As must write the new file");

            string? cancelled = await session.SaveAsync(vm, null, () => null, saveAs: true);
            cancelled.ShouldBeNull("cancelled Save As reports nothing to record");
        }
        finally
        {
            File.Delete(original);
            if (File.Exists(target))
                File.Delete(target);
        }
    });

    private static string SharpRejectingFixture()
    {
        // A PDF PdfPig/Docnet read but PdfSharp refuses: LF fixture bytes with
        // CR inserted before every LF. The shifted xref offsets are exactly
        // the kind of damage lenient readers exist for (seen live: Pig reads
        // on, Sharp throws 'PdfTrailer'). Constructed in-test so no checkout
        // filter can ever change it.
        // E2E builds into net8.0-windows10.0.19041.
        string dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures");
        string path = Path.GetFullPath(Path.Combine(dir, "sample-text.pdf"));
        byte[] lf = File.ReadAllBytes(path);
        var mangled = new List<byte>(lf.Length + 64);
        foreach (byte b in lf)
        {
            if (b == 10)
                mangled.Add(13);
            mangled.Add(b);
        }
        return WritePdf(mangled.ToArray());
    }

    [Fact]
    public void Open_SharpRejectingPdf_OpensDegraded_WithoutLosingTheSession() => Sta(async () =>
    {
        EnsureThemedApp("Light");
        var store = new ScribblesPdfStore();
        string copy = SharpRejectingFixture();
        var converter = new InkModelConverter();
        var inks = new List<InkCanvas>();
        var vm = new MainViewModel(Scribbles.App.App.Themes);
        var session = new DocumentSession(
            store, converter, new StrokeHistory(converter, BoxesOf(vm)), new PenToolbarController(),
            () => inks, SetBoxesOf(vm));
        try
        {
            await session.OpenDocumentAsync(copy, vm);
            vm.Pages.Count.ShouldBe(1, "degraded open still shows pages");
            session.IsDegraded.ShouldBeTrue("Sharp-rejecting file must flag degraded mode");
            vm.Status.Contains("session only").ShouldBeTrue("status must explain the limitation");

            string? saved = await session.SaveAsync(vm, null, () => copy + ".out.pdf", saveAs: true);
            saved.ShouldBeNull("degraded save must refuse, not throw or overwrite");
            File.Exists(copy + ".out.pdf").ShouldBeFalse("refused save must not write");
            vm.Status.Contains("unreadable").ShouldBeTrue("refusal must name the cause");
        }
        finally
        {
            File.Delete(copy);
        }
    });

    [Fact]
    public void Save_WhenClean_SkipsRewrite() => Sta(async () => {
        EnsureThemedApp("Light");
        var store = new ScribblesPdfStore();
        string path = WritePdf(store.Save(BasePdf(),
            new NoteDocument("", Array.Empty<NotePage>())));
        byte[] before = File.ReadAllBytes(path);

        var converter = new InkModelConverter();
        var inks = new List<InkCanvas>();
        var vm = new MainViewModel(Scribbles.App.App.Themes);
        var session = new DocumentSession(
            store, converter, new StrokeHistory(converter, BoxesOf(vm)), new PenToolbarController(),
            () => inks, SetBoxesOf(vm));
        try
        {
            await session.OpenDocumentAsync(path, vm);
            session.IsDirty.ShouldBeFalse("fresh open is clean");
            string? saved = await session.SaveAsync(vm, null, () => null, saveAs: false);
            saved.ShouldBeNull("clean save reports nothing to record");
            File.ReadAllBytes(path).ShouldBe(before, "clean save must not rewrite the file");
        }
        finally
        {
            File.Delete(path);
        }
    });

    [Fact]
    public void DirtyPages_TrackedPerPage_AndClearedOnSave() => Sta(async () =>
    {
        EnsureThemedApp("Light");
        var store = new ScribblesPdfStore();
        string path = WritePdf(store.Save(BasePdf(),
            new NoteDocument("", Array.Empty<NotePage>())));

        var converter = new InkModelConverter();
        var inks = new List<InkCanvas>();
        var vm = new MainViewModel(Scribbles.App.App.Themes);
        var session = new DocumentSession(
            store, converter, new StrokeHistory(converter, BoxesOf(vm)), new PenToolbarController(),
            () => inks, SetBoxesOf(vm));
        string target = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
        try
        {
            await session.OpenDocumentAsync(path, vm);
            session.DirtyPages.ShouldBeEmpty("fresh open tracks nothing");
            session.Revision(1).ShouldBe(0);
            session.MarkDirty(2);
            session.MarkDirty(2);
            session.DirtyPages.ShouldBe(new HashSet<int> { 2 }, "pages tracked once each");
            session.Revision(2).ShouldBe(2, "every mutation bumps the page revision");
            session.IsDirty.ShouldBeTrue();
            await session.SaveAsync(vm, null, () => target, saveAs: true);
            session.DirtyPages.ShouldBeEmpty("save clears the pages it wrote");
            session.IsDirty.ShouldBeFalse();
        }
        finally
        {
            File.Delete(path);
            if (File.Exists(target))
                File.Delete(target);
        }
    });
}
