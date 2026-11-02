namespace Panopticon.Geospatial.Generation;

/// <summary>
/// Slope thresholds for mask generation, in degrees of terrain slope. A slope strictly
/// greater than a threshold sets the matching flag; defaults are 45 degrees impassable
/// and 15 degrees unbuildable. The generator rejects values that are not finite or not
/// in [0, 90] degrees.
/// </summary>
public sealed record SlopeThresholds(double ImpassableDegrees = 45d, double UnbuildableDegrees = 15d);
