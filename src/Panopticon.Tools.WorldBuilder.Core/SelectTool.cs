using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Data.Map;

namespace Panopticon.Tools.WorldBuilder.Core;

/// <summary>
/// Arrow / Select tool — selects objects by single-click or region drag-rectangle.
/// Single-click selection (SelectedObject and SelectionChanged) occurs at pointer down and
/// is preserved during region drag gestures; region drag results are published to SelectedObjects
/// on pointer up without modifying SelectedObject.
/// </summary>
public sealed class SelectTool : IEditorTool
{
    private const float SelectionRadius = 20f;

    private Vector3? _pointerDownWorldPos;
    private Vector3? _lastDragWorldPos;

    public string Name => "Select";

    public MapObject? SelectedObject { get; private set; }

    /// <summary>Objects selected by the most recent region drag rectangle.</summary>
    public IReadOnlyList<MapObject> SelectedObjects { get; private set; } = [];

    /// <summary>Whether a region drag gesture exceeding the selection radius is currently active.</summary>
    public bool IsRegionDragActive { get; private set; }

    /// <summary>The current normalized drag rectangle in world X/Y space, or null if no drag is active.</summary>
    public (Vector2 Min, Vector2 Max)? CurrentDragRectangle { get; private set; }

    /// <summary>Raised when the selected object changes.</summary>
    public event Action<MapObject?>? SelectionChanged;

    /// <summary>
    /// Queries map objects whose world X/Y coordinates lie within the specified inclusive bounds.
    /// Preserves map order; returns empty if the rectangle has zero width or zero height.
    /// </summary>
    public static IReadOnlyList<MapObject> QueryInRectangle(ObjectsList objectsList, Vector2 min, Vector2 max)
    {
        ArgumentNullException.ThrowIfNull(objectsList);

        if (objectsList.Objects is null || objectsList.Objects.Length == 0)
        {
            return [];
        }

        var minX = MathF.Min(min.X, max.X);
        var maxX = MathF.Max(min.X, max.X);
        var minY = MathF.Min(min.Y, max.Y);
        var maxY = MathF.Max(min.Y, max.Y);

        if (minX == maxX || minY == maxY)
        {
            return [];
        }

        var result = new List<MapObject>();
        var objects = objectsList.Objects;
        for (var i = 0; i < objects.Length; i++)
        {
            var obj = objects[i];
            if (obj is not null &&
                obj.Position.X >= minX && obj.Position.X <= maxX &&
                obj.Position.Y >= minY && obj.Position.Y <= maxY)
            {
                result.Add(obj);
            }
        }

        return result;
    }

    public void OnPointerDown(ViewportPointerEvent e, EditorMap map)
    {
        if (e.Button != ViewportMouseButton.Left || !e.WorldPosition.HasValue)
        {
            return;
        }

        _pointerDownWorldPos = e.WorldPosition.Value;
        _lastDragWorldPos = e.WorldPosition.Value;
        IsRegionDragActive = false;
        CurrentDragRectangle = null;
        SelectedObjects = [];

        var objectsList = map.MapFile.ObjectsList;
        if (objectsList?.Objects is null)
        {
            return;
        }

        var clickPos = e.WorldPosition.Value;
        MapObject? closest = null;
        float closestDist = SelectionRadius;

        foreach (var obj in objectsList.Objects)
        {
            float dist = Vector3.Distance(clickPos, obj.Position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = obj;
            }
        }

        SelectedObject = closest;
        SelectionChanged?.Invoke(SelectedObject);
    }

    public void OnPointerMove(ViewportPointerEvent e, EditorMap map)
    {
        if (!_pointerDownWorldPos.HasValue || !e.WorldPosition.HasValue)
        {
            return;
        }

        var startPos = _pointerDownWorldPos.Value;
        var currentPos = e.WorldPosition.Value;
        _lastDragWorldPos = currentPos;

        if (IsRegionDragActive || Vector3.Distance(startPos, currentPos) > SelectionRadius)
        {
            IsRegionDragActive = true;
            var start2D = new Vector2(startPos.X, startPos.Y);
            var current2D = new Vector2(currentPos.X, currentPos.Y);
            CurrentDragRectangle = (Vector2.Min(start2D, current2D), Vector2.Max(start2D, current2D));
        }
    }

    public void OnPointerUp(ViewportPointerEvent e, EditorMap map)
    {
        if (IsRegionDragActive && _pointerDownWorldPos.HasValue)
        {
            var endPos = e.WorldPosition ?? _lastDragWorldPos ?? _pointerDownWorldPos.Value;
            var start2D = new Vector2(_pointerDownWorldPos.Value.X, _pointerDownWorldPos.Value.Y);
            var end2D = new Vector2(endPos.X, endPos.Y);
            var min = Vector2.Min(start2D, end2D);
            var max = Vector2.Max(start2D, end2D);

            var objectsList = map.MapFile.ObjectsList;
            if (objectsList is not null)
            {
                SelectedObjects = QueryInRectangle(objectsList, min, max);
            }
            else
            {
                SelectedObjects = [];
            }
        }

        IsRegionDragActive = false;
        CurrentDragRectangle = null;
        _pointerDownWorldPos = null;
        _lastDragWorldPos = null;
    }

    /// <summary>
    /// Moves all currently selected objects by the specified delta.
    /// Pushes a single reversible edit and marks the map dirty.
    /// No-ops if SelectedObjects is empty or delta is zero.
    /// </summary>
    public void MoveSelectionBy(EditorMap map, Vector3 delta)
    {
        ArgumentNullException.ThrowIfNull(map);

        if (SelectedObjects.Count == 0 || delta == Vector3.Zero)
        {
            return;
        }

        var edit = new RegionObjectMoveEdit(SelectedObjects, delta);
        map.UndoStack.Push(edit);
        map.MarkDirty();
    }

    /// <summary>
    /// Deletes all currently selected objects from the map.
    /// Pushes a single reversible edit, marks the map dirty, and clears SelectedObjects.
    /// No-ops if SelectedObjects is empty.
    /// </summary>
    public void DeleteSelection(EditorMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        if (SelectedObjects.Count == 0)
        {
            return;
        }

        var objectsList = map.MapFile.ObjectsList;
        if (objectsList is null)
        {
            return;
        }

        var edit = new RegionObjectDeleteEdit(objectsList, SelectedObjects);
        map.UndoStack.Push(edit);
        map.MarkDirty();
        SelectedObjects = [];
    }

    public object? GetPropertiesViewModel() => SelectedObject;
}
