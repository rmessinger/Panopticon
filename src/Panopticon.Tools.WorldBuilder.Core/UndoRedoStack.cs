namespace Panopticon.Tools.WorldBuilder.Core;

/// <summary>
/// A typed undo/redo stack for reversible map edits.
/// </summary>
public sealed class UndoRedoStack<T> where T : IMapEdit
{
    private readonly Stack<T> _undoStack = new();
    private readonly Stack<T> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public event EventHandler? StateChanged;

    public void Push(T edit)
    {
        edit.Apply();
        _undoStack.Push(edit);
        _redoStack.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        var edit = _undoStack.Pop();
        edit.Revert();
        _redoStack.Push(edit);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        var edit = _redoStack.Pop();
        edit.Apply();
        _undoStack.Push(edit);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
