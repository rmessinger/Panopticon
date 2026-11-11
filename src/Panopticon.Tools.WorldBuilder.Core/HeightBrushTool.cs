using System.Numerics;
using Panopticon.Data.Map;

namespace Panopticon.Tools.WorldBuilder.Core;

/// <summary>
/// Height editing tool supporting Mound, Dig, and Smooth modes.
/// Each left-button drag stroke is a single undo entry.
/// </summary>
public sealed class HeightBrushTool : IEditorTool
{
    private const int HorizontalScale = 10;

    private bool _isDragging;
    private bool _strokeWroteGrid;
    private ushort[,]? _regionBefore;
    private int _regionMinX, _regionMinY, _regionMaxX, _regionMaxY;
    private int _centreMinX, _centreMinY, _centreMaxX, _centreMaxY;

    public HeightBrushMode Mode { get; set; }
    public BrushSettings Brush { get; } = new();

    public string Name => Mode switch
    {
        HeightBrushMode.Mound => "Mound",
        HeightBrushMode.Dig => "Dig",
        HeightBrushMode.Smooth => "Smooth",
        _ => "Height Brush",
    };

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
        map.MarkDirty();
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

        map.UndoStack.Push(HeightMapEdit.FromPatches(heightMapData, minX, minY, regionBefore, regionAfter, Name));
    }

    public object? GetPropertiesViewModel() => Brush;

    private void Stamp(Vector3 worldPos, HeightMapData heightMapData)
    {
        var (cx, cy) = WorldToCell(worldPos, heightMapData.BorderWidth);
        var elevations = heightMapData.Elevations;
        var reach = Brush.RadiusCells + 1;
        if (cx + reach < 0 || cy + reach < 0 || cx - reach >= elevations.GetLength(0) || cy - reach >= elevations.GetLength(1))
        {
            return;
        }

        IncludeCentre(cx, cy, elevations);
        ApplyBrush(cx, cy, elevations);
    }

    private void IncludeCentre(int cx, int cy, ushort[,] elevations)
    {
        if (_regionBefore is null)
        {
            _centreMinX = _centreMaxX = cx;
            _centreMinY = _centreMaxY = cy;
        }
        else
        {
            _centreMinX = Math.Min(_centreMinX, cx);
            _centreMinY = Math.Min(_centreMinY, cy);
            _centreMaxX = Math.Max(_centreMaxX, cx);
            _centreMaxY = Math.Max(_centreMaxY, cy);
        }

        var reach = Brush.RadiusCells + 1;
        var minX = Math.Max(0, _centreMinX - reach);
        var minY = Math.Max(0, _centreMinY - reach);
        var maxX = Math.Min(elevations.GetLength(0) - 1, _centreMaxX + reach);
        var maxY = Math.Min(elevations.GetLength(1) - 1, _centreMaxY + reach);

        if (_regionBefore is not null && minX == _regionMinX && minY == _regionMinY && maxX == _regionMaxX && maxY == _regionMaxY)
        {
            return;
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

    private void ApplyBrush(int cx, int cy, ushort[,] elevations)
    {
        int r = Brush.RadiusCells;
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

                float falloff = 1.0f - (dist / r);
                float strength = Brush.Strength * falloff;

                switch (Mode)
                {
                    case HeightBrushMode.Mound:
                        elevations[ex, ey] = ClampElevation(elevations[ex, ey] + (int)(Brush.HeightDelta * strength));
                        break;

                    case HeightBrushMode.Dig:
                        elevations[ex, ey] = ClampElevation(elevations[ex, ey] - (int)(Brush.HeightDelta * strength));
                        break;

                    case HeightBrushMode.Smooth:
                        elevations[ex, ey] = ComputeSmoothed(elevations, ex, ey, r, strength);
                        break;
                }
            }
        }
    }

    private static ushort ComputeSmoothed(ushort[,] elevations, int cx, int cy, int radius, float strength)
    {
        int w = elevations.GetLength(0);
        int h = elevations.GetLength(1);
        float sum = 0;
        int count = 0;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = cx + dx;
                int ny = cy + dy;
                if (nx >= 0 && ny >= 0 && nx < w && ny < h)
                {
                    sum += elevations[nx, ny];
                    count++;
                }
            }
        }

        float avg = count > 0 ? sum / count : elevations[cx, cy];
        float current = elevations[cx, cy];
        return ClampElevation(current + (avg - current) * strength);
    }

    private static (int x, int y) WorldToCell(Vector3 worldPos, uint borderWidth)
    {
        int cx = (int)(worldPos.X / HorizontalScale) + (int)borderWidth;
        int cy = (int)(worldPos.Y / HorizontalScale) + (int)borderWidth;
        return (cx, cy);
    }

    private static ushort ClampElevation(float value) =>
        (ushort)Math.Clamp((int)value, 0, ushort.MaxValue);

    private static ushort ClampElevation(int value) =>
        (ushort)Math.Clamp(value, 0, ushort.MaxValue);
}
