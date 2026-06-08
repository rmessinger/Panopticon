using OpenSage.Data.Map;

namespace OpenSage.Tools.WorldBuilder.Core;

/// <summary>
/// Reversible edit that records a patch of tile index changes.
/// </summary>
public sealed class BlendTileEdit : IMapEdit
{
    private readonly BlendTileData _blendTileData;
    private readonly int _startX;
    private readonly int _startY;
    private readonly ushort[,] _before;
    private readonly ushort[,] _after;

    public string Description { get; }

    private BlendTileEdit(BlendTileData blendTileData, int startX, int startY,
        ushort[,] before, ushort[,] after, string description)
    {
        _blendTileData = blendTileData;
        _startX = startX;
        _startY = startY;
        _before = before;
        _after = after;
        Description = description;
    }

    /// <summary>
    /// Creates a BlendTileEdit at a single tile position.
    /// </summary>
    public static BlendTileEdit CreateSingle(BlendTileData blendTileData,
        int tileX, int tileY, ushort newTile)
    {
        var tiles = blendTileData.Tiles;
        var before = new ushort[1, 1];
        var after = new ushort[1, 1];

        before[0, 0] = IsInBounds(tiles, tileX, tileY) ? tiles[tileX, tileY] : (ushort)0;
        after[0, 0] = newTile;

        var edit = new BlendTileEdit(blendTileData, tileX, tileY, before, after, "Paint Tile");
        edit.Apply();
        return edit;
    }

    public void Apply() => CopyPatch(_after);

    public void Revert() => CopyPatch(_before);

    private void CopyPatch(ushort[,] source)
    {
        var tiles = _blendTileData.Tiles;
        var w = source.GetLength(0);
        var h = source.GetLength(1);
        for (var dy = 0; dy < h; dy++)
        {
            for (var dx = 0; dx < w; dx++)
            {
                int tx = _startX + dx;
                int ty = _startY + dy;
                if (IsInBounds(tiles, tx, ty))
                {
                    tiles[tx, ty] = source[dx, dy];
                }
            }
        }
    }

    private static bool IsInBounds(ushort[,] tiles, int x, int y) =>
        x >= 0 && y >= 0 && x < tiles.GetLength(0) && y < tiles.GetLength(1);
}
