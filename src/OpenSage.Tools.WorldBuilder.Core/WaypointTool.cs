using System.Numerics;

namespace OpenSage.Tools.WorldBuilder.Core;

/// <summary>
/// Waypoint placement tool. Waypoints are stored as MapObjects with the type name "Waypoints/Waypoint".
/// </summary>
public sealed class WaypointTool : IEditorTool
{
    public string Name => "Waypoint";

    public string WaypointName { get; set; } = "Waypoint";

    public void OnPointerDown(ViewportPointerEvent e, EditorMap map)
    {
        if (e.Button != ViewportMouseButton.Left || !e.WorldPosition.HasValue)
        {
            return;
        }

        var objectsList = map.MapFile.ObjectsList;
        if (objectsList is null)
        {
            return;
        }

        var mapObject = new OpenSage.Data.Map.MapObject(
            position: e.WorldPosition.Value,
            angle: 0f,
            roadType: OpenSage.Data.Map.RoadType.None,
            typeName: "Waypoints/Waypoint");

        var edit = ObjectPlacementEdit.CreatePlacement(objectsList, mapObject);
        map.UndoStack.Push(edit);
        map.MarkDirty();
    }

    public void OnPointerMove(ViewportPointerEvent e, EditorMap map) { }

    public void OnPointerUp(ViewportPointerEvent e, EditorMap map) { }

    public object? GetPropertiesViewModel() => null;
}
