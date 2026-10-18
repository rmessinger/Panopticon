using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Scripting;

namespace Panopticon.Tools.WorldBuilder.Core;

/// <summary>
/// Waypoint placement tool. Waypoints are stored as map objects with the engine's starred
/// waypoint type name and the required waypointID/waypointName properties
/// (<see cref="Waypoint.CreateWaypointMapObject"/>, WI-A5-35).
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

        var mapObject = Waypoint.CreateWaypointMapObject(
            e.WorldPosition.Value,
            NextWaypointId(objectsList),
            WaypointName);

        var edit = ObjectPlacementEdit.CreatePlacement(objectsList, mapObject);
        map.UndoStack.Push(edit);
        map.MarkDirty();
    }

    private static int NextWaypointId(ObjectsList objectsList) =>
        1 + objectsList.Objects.Count(o => o.TypeName == Waypoint.ObjectTypeName);

    public void OnPointerMove(ViewportPointerEvent e, EditorMap map) { }

    public void OnPointerUp(ViewportPointerEvent e, EditorMap map) { }

    public object? GetPropertiesViewModel() => null;
}
