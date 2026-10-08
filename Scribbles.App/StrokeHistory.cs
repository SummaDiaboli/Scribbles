using System.IO;
using System.Windows.Ink;
using Scribbles.App.Services;
using Scribbles.Core;

namespace Scribbles.App;

/// <summary>Per-page undo/redo over ISF snapshots, plus stroke-id bookkeeping.</summary>
internal sealed class StrokeHistory
{
    private readonly InkModelConverter _converter;
    private readonly Dictionary<int, UndoRedo<byte[]>> _histories = new();
    private readonly Dictionary<int, byte[]> _lastStates = new();

    public bool Suspended { get; set; }

    public StrokeHistory(InkModelConverter converter)
    {
        _converter = converter;
    }

    public void Clear()
    {
        _histories.Clear();
        _lastStates.Clear();
    }

    public void Snapshot(int page, StrokeCollection strokes) =>
        _lastStates[page] = Serialize(strokes);

    /// <summary>
    /// Records the pre-edit baseline for a freshly hooked canvas. Without
    /// this, the first edit's "prior state" would be itself and undoing the
    /// first stroke on a page would be a no-op.
    /// </summary>
    public void EnsureBaseline(int page, StrokeCollection strokes)
    {
        if (!_lastStates.ContainsKey(page))
            Snapshot(page, strokes);
    }

    public void Push(int page, Func<int, StrokeCollection?> strokesFor)
    {
        if (!_histories.TryGetValue(page, out var history))
        {
            history = new UndoRedo<byte[]>();
            _histories[page] = history;
        }
        if (_lastStates.TryGetValue(page, out var last))
            history.Push(last);
        var current = strokesFor(page);
        if (current is not null)
            Snapshot(page, current);
    }

    public void TrackChange(int page, StrokeCollection current, IEnumerable<Stroke> added, IEnumerable<Stroke> removed)
    {
        if (!_lastStates.ContainsKey(page))
        {
            // No hook-time baseline (shouldn't happen): assume the canvas was
            // empty before this first change rather than making undo a no-op.
            if (!_histories.TryGetValue(page, out var fresh))
            {
                fresh = new UndoRedo<byte[]>();
                _histories[page] = fresh;
            }
            fresh.Push(Serialize(new StrokeCollection()));
        }
        Push(page, _ => current);
        foreach (var s in added)
            _converter.GetId(s);
        foreach (var s in removed)
            _converter.Forget(s);
    }

    public bool TryUndo(int page, out byte[] prior, out byte[] current) =>
        TryStep(page, undo: true, out prior, out current);

    public bool TryRedo(int page, out byte[] next, out byte[] current) =>
        TryStep(page, undo: false, out next, out current);

    public bool CanUndo(int page) =>
        _histories.TryGetValue(page, out var history) && history.CanUndo;

    public bool CanRedo(int page) =>
        _histories.TryGetValue(page, out var history) && history.CanRedo;

    private bool TryStep(int page, bool undo, out byte[] state, out byte[] current)
    {
        state = Array.Empty<byte>();
        current = Array.Empty<byte>();
        if (!_histories.TryGetValue(page, out var history)
            || !_lastStates.TryGetValue(page, out byte[]? last))
            return false;
        current = last;
        return undo ? history.TryUndo(current, out state) : history.TryRedo(current, out state);
    }

    public IEnumerable<int> Pages => _histories.Keys;

    private static byte[] Serialize(StrokeCollection strokes)
    {
        using var ms = new MemoryStream();
        strokes.Save(ms);
        return ms.ToArray();
    }
}
