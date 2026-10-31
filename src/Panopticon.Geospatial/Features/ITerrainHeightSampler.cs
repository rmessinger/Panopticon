namespace Panopticon.Geospatial.Features;

/// <summary>
/// Samples generated terrain height in game units at local game coordinates. A false
/// result means no height is available; implementors never fabricate a value.
/// </summary>
public interface ITerrainHeightSampler
{
    /// <summary>Bilinear terrain height at (gameX, gameY) in game units; false when unavailable.</summary>
    bool TrySample(double gameX, double gameY, out double gameZ);
}
