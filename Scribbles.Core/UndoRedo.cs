namespace Scribbles.Core;

/// <summary>Bounded undo/redo stacks for stroke edits (M2 toolbar).</summary>
public sealed class UndoRedo<TState>
{
    private readonly Stack<TState> _undo = new();
    private readonly Stack<TState> _redo = new();

    public int Capacity { get; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;

    public UndoRedo(int capacity = 100)
    {
        Capacity = capacity;
    }

    /// <summary>Record the state BEFORE an edit, so Undo can restore it.</summary>
    public void Push(TState priorState)
    {
        _undo.Push(priorState);
        while (_undo.Count > Capacity)
        {
            // Drop oldest: rebuild bottom-up without the bottom element.
            var items = _undo.Reverse().Skip(1).ToArray();
            _undo.Clear();
            foreach (var item in items)
                _undo.Push(item);
        }
        _redo.Clear();
    }

    public bool TryUndo(TState current, out TState prior)
    {
        if (_undo.TryPop(out prior!))
        {
            _redo.Push(current);
            return true;
        }
        prior = current;
        return false;
    }

    public bool TryRedo(TState current, out TState next)
    {
        if (_redo.TryPop(out next!))
        {
            _undo.Push(current);
            return true;
        }
        next = current;
        return false;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
