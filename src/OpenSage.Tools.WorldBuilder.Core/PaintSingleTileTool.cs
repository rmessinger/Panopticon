using System.Numerics;

namespace OpenSage.Tools.WorldBuilder.Core;

/// <summary>
/// Paints a single terrain tile at the cursor position.
/// </summary>
public sealed class PaintSingleTileTool : IEditorTool
{
    private const int HorizontalScale = 10;

    public string Name => "Paint Single Tile";

    /// <summary>Index into <see cref="OpenSage.Data.Map.BlendTileData.Tiles"/> to paint.</summary>
    public ushort SelectedTileIndex { get; set; }

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

        var edit = BlendTileEdit.CreateSingle(blendTileData, tx, ty, SelectedTileIndex);
        map.UndoStack.Push(edit);
    }
}
