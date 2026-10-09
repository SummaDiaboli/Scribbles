using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using Scribbles.App.Services;
using Scribbles.App.ViewModels;
using Scribbles.Core;
using Scribbles.Pdf;

namespace Scribbles.App;

/// <summary>
/// Open/save/collect/rebuild orchestration plus stroke-change tracking.
/// View access is injected so this stays UI-framework-thin.
/// </summary>
internal sealed class DocumentSession
{
    private readonly ScribblesPdfStore _store;
    private readonly InkModelConverter _converter;
    private readonly StrokeHistory _history;
    private readonly PenToolbarController _pen;
    private readonly Func<IEnumerable<InkCanvas>> _findCanvases;
    private readonly Action<int, List<TextBoxItem>> _setBoxes;

    public string? CurrentPath { get; private set; }
    public byte[]? CurrentBytes { get; private set; }
    public bool IsDirty { get; private set; }

    /// <summary>Raised whenever dirty/history state changes (drives Undo/Redo availability).</summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Model-form strokes for pages whose canvases don't currently exist
    /// (not yet realized, or recycled by virtualization). Seeded at open,
    /// refreshed when a canvas unloads.
    /// </summary>
    private readonly Dictionary<int, List<InkStroke>> _stash = new();

    private readonly Dictionary<int, (double X, double Y)> _factors = new();

    public DocumentSession(
        ScribblesPdfStore store,
        InkModelConverter converter,
        StrokeHistory history,
        PenToolbarController pen,
        Func<IEnumerable<InkCanvas>> findCanvases,
        Action<int, List<TextBoxItem>> setBoxes)
    {
        _store = store;
        _converter = converter;
        _history = history;
        _pen = pen;
        _findCanvases = findCanvases;
        _setBoxes = setBoxes;
    }

    public void MarkDirty()
    {
        IsDirty = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Per-stroke recognition state by converter id: (mode, recognized text).</summary>
    private readonly Dictionary<string, (InkHandleMode Mode, string Text)> _copyMeta = new();

    public void MarkCopyable(IEnumerable<(int Page, Stroke Stroke, string Id)> strokes, string text)
    {
        foreach (var (_, _, id) in strokes)
            _copyMeta[id] = (InkHandleMode.Copyable, text);
        MarkDirty();
    }

    private (InkHandleMode Mode, string Text) MetaFor(string id) =>
        _copyMeta.TryGetValue(id, out var meta) ? meta : (InkHandleMode.Keep, "");

    private InkStroke ApplyMeta(InkStroke stroke)
    {
        var (mode, text) = MetaFor(stroke.Id);
        return stroke with { Mode = mode, RecognizedText = text };
    }

    public bool CanUndoAny() => _history.Pages.Any(_history.CanUndo);

    public bool CanRedoAny() => _history.Pages.Any(_history.CanRedo);

    /// <summary>Point future saves at a different path (used by autosave recovery).</summary>
    public void RetargetCurrentPath(string path) => CurrentPath = path;

    /// <summary>Swaps the base bytes (used after true text-layer edits).</summary>
    public void ReplaceBaseBytes(byte[] bytes)
    {
        CurrentBytes = bytes;
        MarkDirty();
    }

    /// <summary>Re-renders one page's image and words after a true text edit.</summary>
    public async Task RefreshPageAsync(MainViewModel vm, int pageNumber)
    {
        if (CurrentBytes is null)
            return;
        var page = vm.Pages.FirstOrDefault(p => p.Number == pageNumber);
        if (page is null)
            return;
        var rendered = await new DocnetPdfRenderer().RenderSinglePageAsync(CurrentBytes, pageNumber).ConfigureAwait(true);
        page.WidthPt = rendered.WidthPt;
        page.HeightPt = rendered.HeightPt;
        page.Image = rendered.Image;
        page.Words = rendered.Words;
        page.BaseWidth = rendered.Image.Width / 2.0;
        page.BaseHeight = rendered.Image.Height / 2.0;
    }

    public InkCanvas? CanvasForPage(int page)
    {
        foreach (var ink in _findCanvases())
            if (CanvasExplorer.GetPageNumber(ink) == page)
                return ink;
        return null;
    }

    public void HookCanvases()
    {
        foreach (var ink in _findCanvases())
            HookOne(ink);
    }

    private void HookOne(InkCanvas ink)
    {
        ink.Strokes.StrokesChanged -= HandleStrokesChanged;
        ink.Strokes.StrokesChanged += HandleStrokesChanged;
        ink.Unloaded -= HandleCanvasUnloaded;
        ink.Unloaded += HandleCanvasUnloaded;
        _history.EnsureBaseline(CanvasExplorer.GetPageNumber(ink), ink.Strokes);
    }

    /// <summary>
    /// Syncs freshly realized containers: hooks, restores stashed strokes,
    /// applies the tool. Called when the generator reports new containers
    /// (and after open). Strokes of recycled pages live in the stash.
    /// </summary>
    public void SyncContainers()
    {
        foreach (var ink in _findCanvases())
        {
            int page = CanvasExplorer.GetPageNumber(ink);
            if (_stash.TryGetValue(page, out var stashed))
            {
                _stash.Remove(page);
                var (fx, fy) = FactorsFor(page);
                var strokes = stashed.Select(s => _converter.FromModel(s, fx, fy)).ToList();
                _history.Suspended = true;
                try
                {
                    // Replace first: HookOne must latch onto the live collection.
                    ink.Strokes = new StrokeCollection(strokes);
                }
                finally
                {
                    _history.Suspended = false;
                }
            }
            HookOne(ink);
            _pen.ApplyTo(ink);
        }
    }

    private void HandleCanvasUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not InkCanvas ink)
            return;
        int page = CanvasExplorer.GetPageNumber(ink);
        ink.Strokes.StrokesChanged -= HandleStrokesChanged;
        ink.Unloaded -= HandleCanvasUnloaded;
        // Convert live strokes back to model form before the visual dies.
        var (fx, fy) = FactorsFor(page);
        var models = new List<InkStroke>();
        foreach (var stroke in ink.Strokes)
        {
            models.Add(ApplyMeta(_converter.ToModel(stroke, page, fx, fy)));
            _converter.Forget(stroke);
        }
        _stash[page] = models;
    }

    private (double X, double Y) FactorsFor(int pageNumber) =>
        _factors.TryGetValue(pageNumber, out var factors)
            ? factors
            : (Core.InkUnits.DipsPerPoint, Core.InkUnits.DipsPerPoint);

    private void HandleStrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (sender is StrokeCollection strokes)
            OnStrokesChanged(strokes, e.Added.Cast<Stroke>(), e.Removed.Cast<Stroke>());
    }

    public void ApplyToolToAll() => _pen.ApplyToAll(_findCanvases());

    public void OnStrokesChanged(StrokeCollection strokes, IEnumerable<Stroke> added, IEnumerable<Stroke> removed)
    {
        if (_history.Suspended)
            return;
        foreach (var ink in _findCanvases())
        {
            if (!ReferenceEquals(ink.Strokes, strokes))
                continue;
            _history.TrackChange(CanvasExplorer.GetPageNumber(ink), strokes, added, removed);
            MarkDirty();
            break;
        }
    }

    /// <summary>Model strokes for one page (live canvas or stash) for thumbnails.</summary>
    public IReadOnlyList<InkStroke> CollectStrokes(PageViewModel page)
    {
        var canvas = CanvasForPage(page.Number);
        if (canvas is null)
        {
            return _stash.TryGetValue(page.Number, out var stashed)
                ? stashed.Select(ApplyMeta).ToList()
                : Array.Empty<InkStroke>();
        }
        return canvas.Strokes
            .Select(s =>
            {
                var (mode, text) = MetaFor(_converter.GetId(s));
                return _converter.ToModel(s, page.Number, page.DipPerPtX, page.DipPerPtY)
                    with { Mode = mode, RecognizedText = text };
            })
            .ToList();
    }

    public NoteDocument CollectModel(IEnumerable<PageViewModel> pages)
    {
        var result = new List<NotePage>();
        foreach (var vm in pages)
        {
            var canvas = CanvasForPage(vm.Number);
            List<InkStroke> strokes;
            if (canvas is null)
            {
                // Unrealized (virtualized away): fall back to the stash.
                strokes = _stash.TryGetValue(vm.Number, out var stashed)
                    ? stashed.Select(ApplyMeta).ToList()
                    : new List<InkStroke>();
            }
            else
            {
                strokes = canvas.Strokes
                    .Select(s =>
                    {
                        var (mode, text) = MetaFor(_converter.GetId(s));
                        return _converter.ToModel(s, vm.Number, vm.DipPerPtX, vm.DipPerPtY)
                            with { Mode = mode, RecognizedText = text };
                    })
                    .ToList();
            }
            result.Add(new NotePage(vm.Number, vm.WidthPt, vm.HeightPt, strokes, vm.TextBoxes.ToList()));
        }
        return new NoteDocument("", result);
    }

    /// <summary>
    /// Async open: file read + PdfSharp parse + PdfPig sizes/words run on pool
    /// threads; stash seeding touches view-models back on the UI thread.
    /// </summary>
    public async Task OpenDocumentAsync(string path, MainViewModel vm)
    {
        var (bytes, loaded) = await Task.Run(() =>
        {
            byte[] raw = File.ReadAllBytes(path);
            return (raw, _store.Load(raw));
        }).ConfigureAwait(true);

        await vm.LoadPdfAsync(path, bytes).ConfigureAwait(true);
        _converter.Clear();
        _history.Clear();
        _stash.Clear();
        _factors.Clear();
        foreach (var page in vm.Pages)
            _factors[page.Number] = (page.DipPerPtX, page.DipPerPtY);

        // Seed the stash in model form; SyncContainers converts for canvases
        // as they realize (containers materialize asynchronously).
        _copyMeta.Clear();
        if (loaded is { HashesMatch: true, Model: not null })
        {
            foreach (var page in loaded.Model.Pages)
            {
                _stash[page.Number] = page.Strokes.ToList();
                foreach (var stroke in page.Strokes)
                {
                    if (stroke.Mode != InkHandleMode.Keep || stroke.RecognizedText.Length > 0)
                        _copyMeta[stroke.Id] = (stroke.Mode, stroke.RecognizedText);
                }
                var vmPage = vm.Pages.FirstOrDefault(p => p.Number == page.Number);
                if (vmPage is not null)
                {
                    vmPage.TextBoxes.Clear();
                    foreach (var box in page.TextBoxes)
                    {
                        SetDisplay(box, vmPage.DipPerPtX, vmPage.DipPerPtY);
                        vmPage.TextBoxes.Add(box);
                    }
                }
            }
            vm.Status = $"{loaded.Pages.Count} page(s) reopened — annotations editable.";
        }
        else if (loaded.FallbackInk.Count > 0)
        {
            foreach (var group in loaded.FallbackInk.GroupBy(s => s.PageNumber))
                _stash[group.Key] = group.ToList();
            vm.Status = $"{loaded.Pages.Count} page(s). PDF was changed by another tool — ink imported (pressure approximate).";
        }
        SyncContainers();

        CurrentPath = path;
        CurrentBytes = bytes;
        SetClean();
    }

    public async Task ExportFlatCopy(
        MainViewModel vm, string targetPath, Func<NoteDocument, Task<NoteDocument>>? enrichAsync = null)
    {
        if (CurrentBytes is null)
        {
            vm.Status = "Nothing to export — open a PDF first.";
            return;
        }
        var model = CollectModel(vm.Pages);
        if (enrichAsync is not null)
            model = await enrichAsync(model);
        byte[] current = CurrentBytes;
        byte[] exported = await Task.Run(() => _store.ExportFlat(current, model)).ConfigureAwait(true);
        File.WriteAllBytes(targetPath, exported);
        vm.Status = $"Exported flattened copy to {Path.GetFileName(targetPath)}.";
    }

    internal static void SetDisplay(TextBoxItem box, double fx, double fy)
    {
        box.DisplayX = box.X * fx;
        box.DisplayY = box.Y * fy;
        box.DisplayWidth = Math.Max(20, box.Width * fx);
        box.DisplayHeight = Math.Max(16, box.Height * fy);
        box.DisplayFontSize = Math.Clamp(box.FontSizePt * fx, 8, 96);
    }

    /// <summary>
    /// Async save: model collection stays on the UI thread (live ink is
    /// thread-affine); serialize + write run on a pool thread.
    /// </summary>
    public async Task SaveAsync(MainViewModel vm, string? path, Func<string?> promptSaveAs)
    {
        if (CurrentBytes is null)
        {
            vm.Status = "Nothing to save — open a PDF first.";
            return;
        }
        path ??= CurrentPath;
        if (path is null)
        {
            path = promptSaveAs();
            if (path is null)
                return;
        }
        var model = CollectModel(vm.Pages);
        byte[] current = CurrentBytes;
        string target = path;
        byte[] saved = await Task.Run(() =>
        {
            byte[] bytes = _store.Save(current, model);
            File.WriteAllBytes(target, bytes);
            return bytes;
        }).ConfigureAwait(true);
        CurrentPath = target;
        CurrentBytes = saved;
        SetClean();
        vm.Status = $"Saved {Path.GetFileName(target)}.";
    }

    public void RestoreSnapshot(int page, PageState state)
    {
        var canvas = CanvasForPage(page);
        if (canvas is null)
            return;
        _history.Suspended = true;
        try
        {
            var strokes = new StrokeCollection(new MemoryStream(state.Ink));
            foreach (var s in strokes)
                _converter.GetId(s);
            // Replace first, then re-hook: the hook belongs to the live collection.
            canvas.Strokes = strokes;
            HookOne(canvas);
            _history.Snapshot(page, strokes);
            _setBoxes(page, state.Boxes.Select(b => b.Clone()).ToList());
            _pen.ApplyTo(canvas);
        }
        finally
        {
            _history.Suspended = false;
        }
        MarkDirty();
    }

    public void Undo(IEnumerable<int> pages)
    {
        foreach (int page in pages)
            if (_history.TryUndo(page, out var state, out _))
                RestoreSnapshot(page, state);
    }

    public void Redo(IEnumerable<int> pages)
    {
        foreach (int page in pages)
            if (_history.TryRedo(page, out var state, out _))
                RestoreSnapshot(page, state);
    }

    public void DeleteSelection()
    {
        foreach (var ink in _findCanvases())
        {
            var selected = ink.GetSelectedStrokes();
            if (selected.Count == 0)
                continue;
            int page = CanvasExplorer.GetPageNumber(ink);
            _history.Push(page, p => CanvasForPage(p)?.Strokes);
            foreach (var s in selected.Cast<Stroke>().ToList())
            {
                ink.Strokes.Remove(s);
                _converter.Forget(s);
            }
            MarkDirty();
        }
    }

    public async Task<byte[]> SaveWorkingCopyAsync(MainViewModel vm, AutosaveService autosave)
    {
        if (CurrentBytes is null)
            return Array.Empty<byte>();
        var model = CollectModel(vm.Pages);
        byte[] current = CurrentBytes;
        string? source = CurrentPath;
        byte[] saved = await Task.Run(() =>
        {
            byte[] bytes = _store.Save(current, model);
            autosave.Save(AutosaveService.KeyFor(source), bytes, source);
            return bytes;
        }).ConfigureAwait(true);
        SetClean();
        return saved;
    }

    /// <summary>Appends a blank page (same size as the last) to the open document.</summary>
    public async Task AddBlankPageAsync(MainViewModel vm)
    {
        if (CurrentBytes is null)
        {
            vm.Status = "Open a document first.";
            return;
        }
        var last = vm.Pages.LastOrDefault();
        if (last is null)
        {
            vm.Status = "Open a document first.";
            return;
        }
        CurrentBytes = NotebookDocuments.AddBlankPage(CurrentBytes, last.WidthPt, last.HeightPt);
        MarkDirty();
        var rendered = await new DocnetPdfRenderer().RenderSinglePageAsync(CurrentBytes, vm.Pages.Count + 1).ConfigureAwait(true);
        vm.Pages.Add(new PageViewModel
        {
            Number = vm.Pages.Count + 1,
            WidthPt = rendered.WidthPt,
            HeightPt = rendered.HeightPt,
            Image = rendered.Image,
            BaseWidth = rendered.Image.Width / 2.0,
            BaseHeight = rendered.Image.Height / 2.0,
            DisplayScale = vm.Zoom,
            Words = rendered.Words,
        });
        vm.Status = $"Page {vm.Pages.Count} added.";
    }

    private void SetClean()
    {
        IsDirty = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
