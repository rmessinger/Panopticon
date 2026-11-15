using Panopticon.Data.Map;

namespace Panopticon.Tools.WorldBuilder.Core;

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
    /// Creates a BlendTileEdit at a single tile position; application is owned by the undo
    /// stack's Push (WI-A5-27).
    /// </summary>
    public static BlendTileEdit CreateSingle(BlendTileData blendTileData,
        int tileX, int tileY, ushort newTile)
    {
        var tiles = blendTileData.Tiles;
        var before = new ushort[1, 1];
        var after = new ushort[1, 1];

        before[0, 0] = IsInBounds(tiles, tileX, tileY) ? tiles[tileX, tileY] : (ushort)0;
        after[0, 0] = newTile;

        return new BlendTileEdit(blendTileData, tileX, tileY, before, after, "Paint Tile");
    }

    /// <summary>
    /// Creates a BlendTileEdit from caller-supplied patches without reading or writing the grid.
    /// </summary>
    public static BlendTileEdit FromPatches(BlendTileData blendTileData,
        int startX, int startY, ushort[,] before, ushort[,] after, string description)
    {
        ArgumentNullException.ThrowIfNull(blendTileData);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (before.GetLength(0) != after.GetLength(0) || before.GetLength(1) != after.GetLength(1))
        {
            throw new ArgumentException("Before and after patches must have the same extents.", nameof(after));
        }

        return new BlendTileEdit(blendTileData, startX, startY, before, after, description);
    }

    /// <summary>
    /// Creates one dense edit over the bounding rectangle of <paramref name="beforeByCell"/>, which maps each
    /// touched cell to its value before the stroke. Cells in the rectangle that were not touched keep their
    /// live value as both before and after. Returns null when no touched cell changed value.
    /// </summary>
    public static BlendTileEdit? FromTouchedCells(BlendTileData blendTileData,
        IReadOnlyDictionary<(int X, int Y), ushort> beforeByCell, string description)
    {
        ArgumentNullException.ThrowIfNull(blendTileData);
        ArgumentNullException.ThrowIfNull(beforeByCell);
        if (beforeByCell.Count == 0)
        {
            return null;
        }

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        foreach (var (x, y) in beforeByCell.Keys)
        {
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        var tiles = blendTileData.Tiles;
        var before = new ushort[maxX - minX + 1, maxY - minY + 1];
        var after = new ushort[maxX - minX + 1, maxY - minY + 1];
        var changed = false;
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var current = tiles[x, y];
                var original = beforeByCell.TryGetValue((x, y), out var recorded) ? recorded : current;
                before[x - minX, y - minY] = original;
                after[x - minX, y - minY] = current;
                changed |= original != current;
            }
        }

        return changed ? FromPatches(blendTileData, minX, minY, before, after, description) : null;
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
