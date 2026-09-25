using System.Numerics;
using Panopticon.Data.Map;

namespace Panopticon.Tools.WorldBuilder.Core;

/// <summary>
/// Tool for placing objects on the map.
/// </summary>
public sealed class PlaceObjectTool : IEditorTool
{
    public string Name => "Place Object";

    /// <summary>Type name of the object definition to place (e.g. "AmericaVehicleHumvee").</summary>
    public string? SelectedObjectType { get; set; }

    public void OnPointerDown(ViewportPointerEvent e, EditorMap map)
    {
        if (e.Button != ViewportMouseButton.Left
            || !e.WorldPosition.HasValue
            || string.IsNullOrEmpty(SelectedObjectType))
        {
            return;
        }

        var objectsList = map.MapFile.ObjectsList;
        if (objectsList is null)
        {
            return;
        }

        var mapObject = new MapObject(
            position: e.WorldPosition.Value,
            angle: 0f,
            roadType: RoadType.None,
            typeName: SelectedObjectType);

        var edit = ObjectPlacementEdit.CreatePlacement(objectsList, mapObject);
        map.UndoStack.Push(edit);
        map.MarkDirty();
    }

    public void OnPointerMove(ViewportPointerEvent e, EditorMap map) { }

    public void OnPointerUp(ViewportPointerEvent e, EditorMap map) { }

    public object? GetPropertiesViewModel() => null;
}
