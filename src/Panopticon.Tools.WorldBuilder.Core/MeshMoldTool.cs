using System.Numerics;
using Panopticon.Data.Map;

namespace Panopticon.Tools.WorldBuilder.Core;

/// <summary>
/// Mesh Mold tool — sets terrain to a fixed target height in a circular brush area.
/// Each left-button drag stroke is a single undo entry.
/// </summary>
public sealed class MeshMoldTool : IEditorTool
{
    private const int HorizontalScale = 10;

    private bool _isDragging;
    private bool _strokeWroteGrid;
    private ushort[,]? _regionBefore;
    private int _regionMinX, _regionMinY, _regionMaxX, _regionMaxY;

    public BrushSettings Brush { get; } = new();

    /// <summary>Target elevation (raw units, not world-space height).</summary>
    public ushort TargetElevation { get; set; } = 2000;

    public string Name => "Mesh Mold";

    public void OnPointerDown(ViewportPointerEvent e, EditorMap map)
    {
        if (e.Button != ViewportMouseButton.Left || !e.WorldPosition.HasValue)
        {
            return;
        }

        var heightMapData = map.MapFile.HeightMapData;
        if (heightMapData is null)
        {
            return;
        }

        _isDragging = true;
        _strokeWroteGrid = false;
        _regionBefore = null;

        Stamp(e.WorldPosition.Value, heightMapData);
    }

    public void OnPointerMove(ViewportPointerEvent e, EditorMap map)
    {
        if (!_isDragging || e.Button != ViewportMouseButton.Left || !e.WorldPosition.HasValue)
        {
            return;
        }

        var heightMapData = map.MapFile.HeightMapData;
        if (heightMapData is null)
        {
            return;
        }

        Stamp(e.WorldPosition.Value, heightMapData);
    }

    public void OnPointerUp(ViewportPointerEvent e, EditorMap map)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;

        var heightMapData = map.MapFile.HeightMapData;
        var regionBefore = _regionBefore;
        var strokeWroteGrid = _strokeWroteGrid;
        var minX = _regionMinX;
        var minY = _regionMinY;
        var maxX = _regionMaxX;
        var maxY = _regionMaxY;
        _regionBefore = null;

        if (heightMapData is null || regionBefore is null || !strokeWroteGrid)
        {
            return;
        }

        var elevations = heightMapData.Elevations;
        var regionAfter = new ushort[regionBefore.GetLength(0), regionBefore.GetLength(1)];
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                regionAfter[x - minX, y - minY] = elevations[x, y];
            }
        }

        map.MarkDirty();
        map.UndoStack.Push(HeightMapEdit.FromPatches(heightMapData, minX, minY, regionBefore, regionAfter, Name));
    }

    public object? GetPropertiesViewModel() => Brush;

    private void Stamp(Vector3 worldPos, HeightMapData heightMapData)
    {
        int cx = (int)(worldPos.X / HorizontalScale) + (int)heightMapData.BorderWidth;
        int cy = (int)(worldPos.Y / HorizontalScale) + (int)heightMapData.BorderWidth;
        var elevations = heightMapData.Elevations;
        int r = Brush.RadiusCells;
        int w = elevations.GetLength(0);
        int h = elevations.GetLength(1);

        var minX = Math.Max(0, cx - r);
        var minY = Math.Max(0, cy - r);
        var maxX = Math.Min(w - 1, cx + r);
        var maxY = Math.Min(h - 1, cy + r);
        if (minX > maxX || minY > maxY)
        {
            return;
        }

        IncludeFootprint(minX, minY, maxX, maxY, elevations);
        ApplyBrush(cx, cy, r, elevations);
    }

    private void IncludeFootprint(int minX, int minY, int maxX, int maxY, ushort[,] elevations)
    {
        if (_regionBefore is not null)
        {
            minX = Math.Min(minX, _regionMinX);
            minY = Math.Min(minY, _regionMinY);
            maxX = Math.Max(maxX, _regionMaxX);
            maxY = Math.Max(maxY, _regionMaxY);
            if (minX == _regionMinX && minY == _regionMinY && maxX == _regionMaxX && maxY == _regionMaxY)
            {
                return;
            }
        }

        GrowRegion(minX, minY, maxX, maxY, elevations);
    }

    // Cells outside the previous region have never been written, so their live values are their before values.
    private void GrowRegion(int minX, int minY, int maxX, int maxY, ushort[,] elevations)
    {
        var previous = _regionBefore;
        var grown = new ushort[maxX - minX + 1, maxY - minY + 1];
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                grown[x - minX, y - minY] = previous is not null
                    && x >= _regionMinX && x <= _regionMaxX
                    && y >= _regionMinY && y <= _regionMaxY
                    ? previous[x - _regionMinX, y - _regionMinY]
                    : elevations[x, y];
            }
        }

        _regionBefore = grown;
        _regionMinX = minX;
        _regionMinY = minY;
        _regionMaxX = maxX;
        _regionMaxY = maxY;
    }

    private void ApplyBrush(int cx, int cy, int r, ushort[,] elevations)
    {
        int w = elevations.GetLength(0);
        int h = elevations.GetLength(1);

        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist > r)
                {
                    continue;
                }

                int ex = cx + dx;
                int ey = cy + dy;
                if (ex < 0 || ey < 0 || ex >= w || ey >= h)
                {
                    continue;
                }

                _strokeWroteGrid = true;
                float t = Brush.Strength * (1.0f - dist / r);
                elevations[ex, ey] = (ushort)(elevations[ex, ey] * (1 - t) + TargetElevation * t);
            }
        }
    }
}
