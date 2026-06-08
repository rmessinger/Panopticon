using System.Numerics;

namespace OpenSage.Tools.WorldBuilder.Core;

/// <summary>
/// Eye Dropper tool — samples the tile under the cursor and sets it as the selected tile
/// in the texture brush.
/// </summary>
public sealed class EyeDropperTool : IEditorTool
{
    private const int HorizontalScale = 10;

    public string Name => "Eye Dropper";

    /// <summary>Raised when a tile is sampled; payload is the tile index.</summary>
    public event Action<ushort>? TileSampled;

    public void OnPointerDown(ViewportPointerEvent e, EditorMap map)
    {
        if (e.Button != ViewportMouseButton.Left || !e.WorldPosition.HasValue)
        {
            return;
        }

        SampleAt(e.WorldPosition.Value, map);
    }

    public void OnPointerMove(ViewportPointerEvent e, EditorMap map) { }

    public void OnPointerUp(ViewportPointerEvent e, EditorMap map) { }

    public object? GetPropertiesViewModel() => null;

    private void SampleAt(Vector3 worldPos, EditorMap map)
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

        if (tx >= 0 && ty >= 0 && tx < tiles.GetLength(0) && ty < tiles.GetLength(1))
        {
            TileSampled?.Invoke(tiles[tx, ty]);
        }
    }
}
