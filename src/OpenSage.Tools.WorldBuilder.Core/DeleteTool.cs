namespace OpenSage.Tools.WorldBuilder.Core;

/// <summary>
/// Deletes the currently selected object from the map.
/// </summary>
public sealed class DeleteTool : IEditorTool
{
    private readonly SelectTool _selectTool;

    public DeleteTool(SelectTool selectTool)
    {
        _selectTool = selectTool;
    }

    public string Name => "Delete Selected";

    public void OnPointerDown(ViewportPointerEvent e, EditorMap map) { }

    public void OnPointerMove(ViewportPointerEvent e, EditorMap map) { }

    public void OnPointerUp(ViewportPointerEvent e, EditorMap map) { }

    public object? GetPropertiesViewModel() => null;

    /// <summary>
    /// Deletes the object currently selected in the companion <see cref="SelectTool"/>.
    /// Call this when the user presses Delete or activates the delete action.
    /// </summary>
    public void DeleteSelected(EditorMap map)
    {
        var obj = _selectTool.SelectedObject;
        var objectsList = map.MapFile.ObjectsList;

        if (obj is null || objectsList is null)
        {
            return;
        }

        var edit = ObjectPlacementEdit.CreateDeletion(objectsList, obj);
        map.UndoStack.Push(edit);
        map.MarkDirty();
    }
}
