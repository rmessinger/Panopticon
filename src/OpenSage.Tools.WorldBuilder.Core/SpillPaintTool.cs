using System.Collections.Generic;
using System.Numerics;

namespace OpenSage.Tools.WorldBuilder.Core;

/// <summary>
/// Flood-fill tool that paints all contiguous tiles with the same index as the
/// tile under the cursor, replacing them with <see cref="SelectedTileIndex"/>.
/// </summary>
public sealed class SpillPaintTool : IEditorTool
{
    private const int HorizontalScale = 10;

    public string Name => "Spill Paint";

    public ushort SelectedTileIndex { get; set; }

    public void OnPointerDown(ViewportPointerEvent e, EditorMap map)
    {
        if (e.Button != ViewportMouseButton.Left || !e.WorldPosition.HasValue)
        {
            return;
        }

        FloodFill(e.WorldPosition.Value, map);
        map.MarkDirty();
    }

    public void OnPointerMove(ViewportPointerEvent e, EditorMap map) { }

    public void OnPointerUp(ViewportPointerEvent e, EditorMap map) { }

    public object? GetPropertiesViewModel() => null;

    private void FloodFill(Vector3 worldPos, EditorMap map)
    {
        var blendTileData = map.MapFile.BlendTileData;
        if (blendTileData?.Tiles is null)
        {
            return;
        }

        var borderWidth = (int)(map.MapFile.HeightMapData?.BorderWidth ?? 0);
        int startX = (int)(worldPos.X / HorizontalScale) + borderWidth;
        int startY = (int)(worldPos.Y / HorizontalScale) + borderWidth;
        var tiles = blendTileData.Tiles;
        int w = tiles.GetLength(0);
        int h = tiles.GetLength(1);

        if (startX < 0 || startY < 0 || startX >= w || startY >= h)
        {
            return;
        }

        ushort targetTile = tiles[startX, startY];
        if (targetTile == SelectedTileIndex)
        {
            return;
        }

        // BFS flood fill — capped at 10000 tiles to avoid freezing
        const int maxTiles = 10_000;
        var queue = new Queue<(int, int)>();
        var visited = new HashSet<(int, int)>();
        queue.Enqueue((startX, startY));

        while (queue.Count > 0 && visited.Count < maxTiles)
        {
            var (cx, cy) = queue.Dequeue();
            if (cx < 0 || cy < 0 || cx >= w || cy >= h)
            {
                continue;
            }

            if (visited.Contains((cx, cy)))
            {
                continue;
            }

            if (tiles[cx, cy] != targetTile)
            {
                continue;
            }

            visited.Add((cx, cy));
            var edit = BlendTileEdit.CreateSingle(blendTileData, cx, cy, SelectedTileIndex);
            map.UndoStack.Push(edit);

            queue.Enqueue((cx - 1, cy));
            queue.Enqueue((cx + 1, cy));
            queue.Enqueue((cx, cy - 1));
            queue.Enqueue((cx, cy + 1));
        }
    }
}
