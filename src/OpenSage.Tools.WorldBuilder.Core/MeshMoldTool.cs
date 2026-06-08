using System.Numerics;
using OpenSage.Data.Map;

namespace OpenSage.Tools.WorldBuilder.Core;

/// <summary>
/// Mesh Mold tool — sets terrain to a fixed target height in a circular brush area.
/// </summary>
public sealed class MeshMoldTool : IEditorTool
{
    private const int HorizontalScale = 10;

    private bool _isDragging;

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

        _isDragging = true;
        ApplyBrush(e.WorldPosition.Value, map);
        map.MarkDirty();
    }

    public void OnPointerMove(ViewportPointerEvent e, EditorMap map)
    {
        if (!_isDragging || e.Button != ViewportMouseButton.Left || !e.WorldPosition.HasValue)
        {
            return;
        }

        ApplyBrush(e.WorldPosition.Value, map);
    }

    public void OnPointerUp(ViewportPointerEvent e, EditorMap map)
    {
        _isDragging = false;
    }

    public object? GetPropertiesViewModel() => Brush;

    private void ApplyBrush(Vector3 worldPos, EditorMap map)
    {
        var heightMapData = map.MapFile.HeightMapData;
        if (heightMapData is null)
        {
            return;
        }

        int cx = (int)(worldPos.X / HorizontalScale) + (int)heightMapData.BorderWidth;
        int cy = (int)(worldPos.Y / HorizontalScale) + (int)heightMapData.BorderWidth;
        var elevations = heightMapData.Elevations;
        int r = Brush.RadiusCells;
        int w = elevations.GetLength(0);
        int h = elevations.GetLength(1);

        var edit = HeightMapEdit.Capture(
            heightMapData,
            Math.Max(0, cx - r),
            Math.Max(0, cy - r),
            Math.Min(2 * r + 1, w - Math.Max(0, cx - r)),
            Math.Min(2 * r + 1, h - Math.Max(0, cy - r)),
            e =>
            {
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
                        if (ex >= 0 && ey >= 0 && ex < w && ey < h)
                        {
                            float t = Brush.Strength * (1.0f - dist / r);
                            e[ex, ey] = (ushort)(e[ex, ey] * (1 - t) + TargetElevation * t);
                        }
                    }
                }
            },
            "Mesh Mold");

        edit.Apply();
        map.UndoStack.Push(edit);
    }
}
