namespace Panopticon.Tools.WorldBuilder.Core;

/// <summary>
/// Brush mode for height editing tools.
/// </summary>
public enum HeightBrushMode
{
    Mound,
    Dig,
    Smooth,
}

/// <summary>
/// Shared settings for all brush-based editor tools.
/// </summary>
public sealed class BrushSettings
{
    /// <summary>Brush radius in terrain cells (1 cell = 10 world units).</summary>
    public int RadiusCells { get; set; } = 5;

    /// <summary>Brush strength in [0,1] range.</summary>
    public float Strength { get; set; } = 0.3f;

    /// <summary>Height delta per stroke applied at full strength (in elevation units).</summary>
    public int HeightDelta { get; set; } = 200;
}
