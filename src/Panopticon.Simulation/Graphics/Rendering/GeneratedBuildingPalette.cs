using System;
using System.Numerics;

namespace Panopticon.Graphics.Rendering;

internal static class GeneratedBuildingPalette
{
    // Variants 0..3 are wall and gable-end materials; 4..7 are roof materials. Colours are sRGB.
    private static readonly Vector3[] Colours =
    [
        new(0.72f, 0.64f, 0.55f),
        new(0.55f, 0.32f, 0.24f),
        new(0.48f, 0.47f, 0.45f),
        new(0.62f, 0.52f, 0.38f),
        new(0.25f, 0.26f, 0.30f),
        new(0.50f, 0.30f, 0.20f),
        new(0.32f, 0.30f, 0.28f),
        new(0.42f, 0.36f, 0.30f),
    ];

    public static Vector3 ColorFor(int materialVariant)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(materialVariant);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(materialVariant, Colours.Length);
        return Colours[materialVariant];
    }
}
