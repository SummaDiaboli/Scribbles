using System.IO;
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
        {
            ink.Strokes.StrokesChanged -= HandleStrokesChanged;
            ink.Strokes.StrokesChanged += HandleStrokesChanged;
            _history.EnsureBaseline(CanvasExplorer.GetPageNumber(ink), ink.Strokes);
        }
    }

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
            var strokes = canvas is null
                ? new List<InkStroke>()
                : canvas.Strokes.Select(s => _converter.ToModel(s, vm.Number)).ToList();
            result.Add(new NotePage(vm.Number, vm.WidthPt, vm.HeightPt, strokes));
        }
        return new NoteDocument("", result);
    }

    public void OpenDocument(string path, MainViewModel vm)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var loaded = _store.Load(bytes);

        vm.LoadPdf(path);
        HookCanvases();
        ApplyToolToAll();
        _converter.Clear();
        _history.Clear();

        var strokesByPage = new Dictionary<int, List<Stroke>>();
        if (loaded is { HashesMatch: true, Model: not null })
        {
            foreach (var page in loaded.Model.Pages)
                StrokesFor(strokesByPage, page.Number).AddRange(page.Strokes.Select(_converter.FromModel));
            vm.Status = $"{loaded.Pages.Count} page(s) reopened — annotations editable.";
        }
        else if (loaded.FallbackInk.Count > 0)
        {
            foreach (var s in loaded.FallbackInk)
                StrokesFor(strokesByPage, s.PageNumber).Add(_converter.FromModel(s));
            vm.Status = $"{loaded.Pages.Count} page(s). PDF was changed by another tool — ink imported (pressure approximate).";
        }

        _history.Suspended = true;
        try
        {
            foreach (var (pageNumber, strokes) in strokesByPage)
            {
                var canvas = CanvasForPage(pageNumber);
                if (canvas is null)
                    continue;
                var collection = new StrokeCollection(strokes);
                canvas.Strokes = collection;
                _history.Snapshot(pageNumber, collection);
            }
        }
        finally
        {
            _history.Suspended = false;
        }

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
            canvas.Strokes = strokes;
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

    private static List<Stroke> StrokesFor(Dictionary<int, List<Stroke>> map, int page)
    {
        if (!map.TryGetValue(page, out var list))
            map[page] = list = new List<Stroke>();
        return list;
    }
}
