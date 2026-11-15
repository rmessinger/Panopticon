using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// Places multiple objects at the specified positions in a single bulk operation.
    /// Core/editor-API only — no interactive widget work.
    /// </summary>
    public void PlaceObjects(EditorMap map, IEnumerable<Vector3> positions)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(positions);

        if (string.IsNullOrEmpty(SelectedObjectType))
        {
            return;
        }

        var objectsList = map.MapFile.ObjectsList;
        if (objectsList is null)
        {
            return;
        }

        var posArray = positions.ToArray();
        if (posArray.Length == 0)
        {
            return;
        }

        var objects = new MapObject[posArray.Length];
        for (var i = 0; i < posArray.Length; i++)
        {
            objects[i] = new MapObject(
                position: posArray[i],
                angle: 0f,
                roadType: RoadType.None,
                typeName: SelectedObjectType);
        }

        var edit = BulkObjectPlacementEdit.CreatePlacement(objectsList, objects);
        map.UndoStack.Push(edit);
        map.MarkDirty();
    }

    public void OnPointerMove(ViewportPointerEvent e, EditorMap map) { }

    public void OnPointerUp(ViewportPointerEvent e, EditorMap map) { }

    public object? GetPropertiesViewModel() => null;
}
