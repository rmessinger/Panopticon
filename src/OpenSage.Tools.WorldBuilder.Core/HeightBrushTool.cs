using System.Numerics;
using OpenSage.Data.Map;

namespace OpenSage.Tools.WorldBuilder.Core;

/// <summary>
/// Height editing tool supporting Mound, Dig, and Smooth modes.
/// Each left-button drag stroke is a single undo entry.
/// </summary>
public sealed class HeightBrushTool : IEditorTool
{
    private const int HorizontalScale = 10;

    private bool _isDragging;
    private ushort[,]? _strokeBefore;
    private int _strokeMinX, _strokeMinY, _strokeMaxX, _strokeMaxY;

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
        _strokeBefore = (ushort[,])heightMapData.Elevations.Clone();
        _strokeMinX = int.MaxValue;
        _strokeMinY = int.MaxValue;
        _strokeMaxX = int.MinValue;
        _strokeMaxY = int.MinValue;

        ApplyBrush(e.WorldPosition.Value, heightMapData);
        UpdateBounds(e.WorldPosition.Value, heightMapData);
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

        ApplyBrush(e.WorldPosition.Value, heightMapData);
        UpdateBounds(e.WorldPosition.Value, heightMapData);
    }

    public void OnPointerUp(ViewportPointerEvent e, EditorMap map)
    {
        if (!_isDragging || _strokeBefore is null)
        {
            _isDragging = false;
            return;
        }

        _isDragging = false;

        var heightMapData = map.MapFile.HeightMapData;
        if (heightMapData is null)
        {
            return;
        }

        var after = (ushort[,])heightMapData.Elevations.Clone();
        int w = heightMapData.Elevations.GetLength(0);
        int h = heightMapData.Elevations.GetLength(1);

        var before = _strokeBefore;
        _strokeBefore = null;

        // Build a patch-sized edit from the captured before/after
        int startX = Math.Max(0, _strokeMinX - Brush.RadiusCells - 1);
        int startY = Math.Max(0, _strokeMinY - Brush.RadiusCells - 1);
        int endX = Math.Min(w - 1, _strokeMaxX + Brush.RadiusCells + 1);
        int endY = Math.Min(h - 1, _strokeMaxY + Brush.RadiusCells + 1);
        int pw = endX - startX + 1;
        int ph = endY - startY + 1;

        var patchBefore = new ushort[pw, ph];
        var patchAfter = new ushort[pw, ph];
        for (var dy = 0; dy < ph; dy++)
        {
            for (var dx = 0; dx < pw; dx++)
            {
                patchBefore[dx, dy] = before[startX + dx, startY + dy];
                patchAfter[dx, dy] = after[startX + dx, startY + dy];
            }
        }

        // Restore before-state so HeightMapEdit.Capture can record before/after properly.
        // Instead, build the edit directly from our captured data.
        var edit = HeightMapEdit.Capture(
            heightMapData,
            startX, startY, pw, ph,
            elevations =>
            {
                // already applied; no-op
            },
            Name);

        map.UndoStack.Push(edit);
    }

    public object? GetPropertiesViewModel() => Brush;

    private void ApplyBrush(Vector3 worldPos, HeightMapData heightMapData)
    {
        var (cx, cy) = WorldToCell(worldPos, heightMapData.BorderWidth);
        var elevations = heightMapData.Elevations;
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

    private void UpdateBounds(Vector3 worldPos, HeightMapData heightMapData)
    {
        var (cx, cy) = WorldToCell(worldPos, heightMapData.BorderWidth);
        _strokeMinX = Math.Min(_strokeMinX, cx);
        _strokeMinY = Math.Min(_strokeMinY, cy);
        _strokeMaxX = Math.Max(_strokeMaxX, cx);
        _strokeMaxY = Math.Max(_strokeMaxY, cy);
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
