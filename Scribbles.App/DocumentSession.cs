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
        Func<IEnumerable<InkCanvas>> findCanvases)
    {
        _store = store;
        _converter = converter;
        _history = history;
        _pen = pen;
        _findCanvases = findCanvases;
    }

    public void MarkDirty()
    {
        IsDirty = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool CanUndoAny() => _history.Pages.Any(_history.CanUndo);

    public bool CanRedoAny() => _history.Pages.Any(_history.CanRedo);

    /// <summary>Point future saves at a different path (used by autosave recovery).</summary>
    public void RetargetCurrentPath(string path) => CurrentPath = path;

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
            models.Add(_converter.ToModel(stroke, page, fx, fy));
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
                    ? stashed
                    : new List<InkStroke>();
            }
            else
            {
                strokes = canvas.Strokes
                    .Select(s => _converter.ToModel(s, vm.Number, vm.DipPerPtX, vm.DipPerPtY))
                    .ToList();
            }
            result.Add(new NotePage(vm.Number, vm.WidthPt, vm.HeightPt, strokes));
        }
        return new NoteDocument("", result);
    }

    public void OpenDocument(string path, MainViewModel vm)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var loaded = _store.Load(bytes);

        vm.LoadPdf(path);
        _converter.Clear();
        _history.Clear();
        _stash.Clear();
        _factors.Clear();
        foreach (var page in vm.Pages)
            _factors[page.Number] = (page.DipPerPtX, page.DipPerPtY);

        // Seed the stash in model form; SyncContainers converts for canvases
        // as they realize (containers materialize asynchronously).
        if (loaded is { HashesMatch: true, Model: not null })
        {
            foreach (var page in loaded.Model.Pages)
                _stash[page.Number] = page.Strokes.ToList();
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

    public void Save(MainViewModel vm, string? path, Func<string?> promptSaveAs)
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
        byte[] saved = _store.Save(CurrentBytes, CollectModel(vm.Pages));
        File.WriteAllBytes(path, saved);
        CurrentPath = path;
        CurrentBytes = saved;
        SetClean();
        vm.Status = $"Saved {Path.GetFileName(path)}.";
    }

    public void RestoreSnapshot(int page, byte[] state)
    {
        var canvas = CanvasForPage(page);
        if (canvas is null)
            return;
        _history.Suspended = true;
        try
        {
            var strokes = new StrokeCollection(new MemoryStream(state));
            foreach (var s in strokes)
                _converter.GetId(s);
            // Replace first, then re-hook: the hook belongs to the live collection.
            canvas.Strokes = strokes;
            HookOne(canvas);
            _history.Snapshot(page, strokes);
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

    public byte[] SaveWorkingCopy(MainViewModel vm, AutosaveService autosave)
    {
        if (CurrentBytes is null)
            return Array.Empty<byte>();
        byte[] saved = _store.Save(CurrentBytes, CollectModel(vm.Pages));
        autosave.Save(AutosaveService.KeyFor(CurrentPath), saved, CurrentPath);
        SetClean();
        return saved;
    }

    private void SetClean()
    {
        IsDirty = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
