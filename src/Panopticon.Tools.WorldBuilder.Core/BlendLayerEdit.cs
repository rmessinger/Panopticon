using Panopticon.Data.Map;

namespace Panopticon.Tools.WorldBuilder.Core;

/// <summary>
/// BlendTileData layers that a <see cref="BlendLayerEdit"/> can address.
/// </summary>
public enum BlendLayer
{
    Tiles,
    Blends,
    Impassability,
    Buildability,
}

/// <summary>
/// Reversible edit that records a dense rectangular before and after patch of one BlendTileData layer.
/// Cells outside the grid are never written.
/// </summary>
public sealed class BlendLayerEdit : IMapEdit
{
    private readonly Action _apply;
    private readonly Action _revert;

    public string Description { get; }

    private BlendLayerEdit(Action apply, Action revert, string description)
    {
        _apply = apply;
        _revert = revert;
        Description = description;
    }

    /// <summary>
    /// Creates a BlendLayerEdit by capturing the selected layer in the given rectangle, running
    /// <paramref name="applyAction"/> on the live layer, then recording the new state.
    /// </summary>
    public static BlendLayerEdit Capture(BlendTileData blendTileData, BlendLayer layer,
        int startX, int startY, int width, int height, Action applyAction, string description)
    {
        ArgumentNullException.ThrowIfNull(blendTileData);
        ArgumentNullException.ThrowIfNull(applyAction);

        return layer switch
        {
            BlendLayer.Tiles => CaptureLayer(() => blendTileData.Tiles, startX, startY, width, height, applyAction, description),
            BlendLayer.Blends => CaptureLayer(() => blendTileData.Blends, startX, startY, width, height, applyAction, description),
            BlendLayer.Impassability => CaptureLayer(() => blendTileData.Impassability, startX, startY, width, height, applyAction, description),
            BlendLayer.Buildability => CaptureLayer(() => blendTileData.Buildability, startX, startY, width, height, applyAction, description),
            _ => throw new ArgumentOutOfRangeException(nameof(layer)),
        };
    }

    public void Apply() => _apply();

    public void Revert() => _revert();

    private static BlendLayerEdit CaptureLayer<T>(Func<T[,]> selectLayer,
        int startX, int startY, int width, int height, Action applyAction, string description)
        where T : struct
    {
        var before = ReadRegion(selectLayer(), startX, startY, width, height);
        applyAction();
        var after = ReadRegion(selectLayer(), startX, startY, width, height);

        return new BlendLayerEdit(
            () => WriteRegion(selectLayer(), startX, startY, after),
            () => WriteRegion(selectLayer(), startX, startY, before),
            description);
    }

    private static T[,] ReadRegion<T>(T[,] layer, int startX, int startY, int width, int height)
        where T : struct
    {
        var region = new T[width, height];
        for (var dy = 0; dy < height; dy++)
        {
            for (var dx = 0; dx < width; dx++)
            {
                int x = startX + dx;
                int y = startY + dy;
                region[dx, dy] = IsInBounds(layer, x, y) ? layer[x, y] : default;
            }
        }

        return region;
    }

    private static void WriteRegion<T>(T[,] layer, int startX, int startY, T[,] region)
    {
        for (var dy = 0; dy < region.GetLength(1); dy++)
        {
            for (var dx = 0; dx < region.GetLength(0); dx++)
            {
                int x = startX + dx;
                int y = startY + dy;
                if (IsInBounds(layer, x, y))
                {
                    layer[x, y] = region[dx, dy];
                }
            }
        }
    }

    private static bool IsInBounds<T>(T[,] layer, int x, int y) =>
        x >= 0 && y >= 0 && x < layer.GetLength(0) && y < layer.GetLength(1);
}
