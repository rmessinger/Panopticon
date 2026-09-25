using System.Collections.Generic;
using System.Numerics;

namespace Panopticon.Tools.WorldBuilder.Core;

/// <summary>
/// Paints a large texture tile (2×2 or 4×4 cells) starting from the cursor position.
/// </summary>
public sealed class PaintLargeTileTool : IEditorTool
{
    private const int HorizontalScale = 10;

    public string Name => "Paint Large Tile";

    /// <summary>Tile index to paint.</summary>
    public ushort SelectedTileIndex { get; set; }

    /// <summary>Size of the large tile in tile cells (typically 2 or 4).</summary>
    public int TileSize { get; set; } = 2;

    public void OnPointerDown(ViewportPointerEvent e, EditorMap map)
    {
        if (e.Button != ViewportMouseButton.Left || !e.WorldPosition.HasValue)
        {
            return;
        }

        PaintAt(e.WorldPosition.Value, map);
        map.MarkDirty();
    }

    public void OnPointerMove(ViewportPointerEvent e, EditorMap map)
    {
        if (e.Button != ViewportMouseButton.Left || !e.WorldPosition.HasValue)
        {
            return;
        }

        PaintAt(e.WorldPosition.Value, map);
    }

    public void OnPointerUp(ViewportPointerEvent e, EditorMap map) { }

    public object? GetPropertiesViewModel() => null;

    private void PaintAt(Vector3 worldPos, EditorMap map)
    {
        var blendTileData = map.MapFile.BlendTileData;
        if (blendTileData?.Tiles is null)
        {
            return;
        }

        var borderWidth = (int)(map.MapFile.HeightMapData?.BorderWidth ?? 0);
        int tx = (int)(worldPos.X / HorizontalScale) + borderWidth;
        int ty = (int)(worldPos.Y / HorizontalScale) + borderWidth;
        var tiles = blendTileData.Tiles;
        int w = tiles.GetLength(0);
        int h = tiles.GetLength(1);

        var edits = new List<IMapEdit>();
        for (int dy = 0; dy < TileSize; dy++)
        {
            for (int dx = 0; dx < TileSize; dx++)
            {
                int ex = tx + dx;
                int ey = ty + dy;
                if (ex >= 0 && ey >= 0 && ex < w && ey < h)
                {
                    edits.Add(BlendTileEdit.CreateSingle(blendTileData, ex, ey, SelectedTileIndex));
                }
            }
        }

        if (edits.Count > 0)
        {
            foreach (var edit in edits)
            {
                map.UndoStack.Push(edit);
            }
        }
    }
}
