using System.Numerics;

namespace Panopticon.Tools.WorldBuilder.Core;

/// <summary>
/// Paints a single terrain tile at the cursor position.
/// Each left-button drag stroke is a single undo entry.
/// </summary>
public sealed class PaintSingleTileTool : IEditorTool
{
    private const int HorizontalScale = 10;

    private readonly Dictionary<(int X, int Y), ushort> _beforeByCell = new();

    public string Name => "Paint Single Tile";

    /// <summary>Index into <see cref="Panopticon.Data.Map.BlendTileData.Tiles"/> to paint.</summary>
    public ushort SelectedTileIndex { get; set; }

    public void OnPointerDown(ViewportPointerEvent e, EditorMap map)
    {
        if (e.Button != ViewportMouseButton.Left || !e.WorldPosition.HasValue)
        {
            return;
        }

        _beforeByCell.Clear();
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

    public void OnPointerUp(ViewportPointerEvent e, EditorMap map)
    {
        if (_beforeByCell.Count == 0)
        {
            return;
        }

        var edit = BlendTileEdit.FromTouchedCells(map.MapFile.BlendTileData, _beforeByCell, "Paint Tile");
        _beforeByCell.Clear();
        if (edit is not null)
        {
            map.UndoStack.Push(edit);
        }
    }

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
        if (tx < 0 || ty < 0 || tx >= tiles.GetLength(0) || ty >= tiles.GetLength(1))
        {
            return;
        }

        _beforeByCell.TryAdd((tx, ty), tiles[tx, ty]);
        tiles[tx, ty] = SelectedTileIndex;
    }
}
