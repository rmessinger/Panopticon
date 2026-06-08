using OpenSage.Data.Map;

namespace OpenSage.Tools.WorldBuilder.Core;

/// <summary>
/// Reversible edit that records a patch of elevation changes.
/// </summary>
public sealed class HeightMapEdit : IMapEdit
{
    private readonly HeightMapData _heightMapData;
    private readonly int _startX;
    private readonly int _startY;
    private readonly ushort[,] _before;
    private readonly ushort[,] _after;

    public string Description { get; }

    private HeightMapEdit(HeightMapData heightMapData, int startX, int startY,
        ushort[,] before, ushort[,] after, string description)
    {
        _heightMapData = heightMapData;
        _startX = startX;
        _startY = startY;
        _before = before;
        _after = after;
        Description = description;
    }

    /// <summary>
    /// Creates a HeightMapEdit by capturing the current elevation in the given region,
    /// applying <paramref name="applyAction"/>, then recording the new state.
    /// </summary>
    public static HeightMapEdit Capture(HeightMapData heightMapData,
        int startX, int startY, int width, int height,
        Action<ushort[,]> applyAction,
        string description)
    {
        var elevations = heightMapData.Elevations;
        var before = new ushort[width, height];
        for (var dy = 0; dy < height; dy++)
        {
            for (var dx = 0; dx < width; dx++)
            {
                int ex = startX + dx;
                int ey = startY + dy;
                before[dx, dy] = IsInBounds(elevations, ex, ey) ? elevations[ex, ey] : (ushort)0;
            }
        }

        applyAction(elevations);

        var after = new ushort[width, height];
        for (var dy = 0; dy < height; dy++)
        {
            for (var dx = 0; dx < width; dx++)
            {
                int ex = startX + dx;
                int ey = startY + dy;
                after[dx, dy] = IsInBounds(elevations, ex, ey) ? elevations[ex, ey] : (ushort)0;
            }
        }

        return new HeightMapEdit(heightMapData, startX, startY, before, after, description);
    }

    public void Apply()
    {
        CopyPatch(_after);
    }

    public void Revert()
    {
        CopyPatch(_before);
    }

    private void CopyPatch(ushort[,] source)
    {
        var elevations = _heightMapData.Elevations;
        var w = source.GetLength(0);
        var h = source.GetLength(1);
        for (var dy = 0; dy < h; dy++)
        {
            for (var dx = 0; dx < w; dx++)
            {
                int ex = _startX + dx;
                int ey = _startY + dy;
                if (IsInBounds(elevations, ex, ey))
                {
                    elevations[ex, ey] = source[dx, dy];
                }
            }
        }
    }

    private static bool IsInBounds(ushort[,] elevations, int x, int y)
    {
        return x >= 0 && y >= 0 && x < elevations.GetLength(0) && y < elevations.GetLength(1);
    }
}
