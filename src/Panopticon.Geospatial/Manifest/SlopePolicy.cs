namespace Panopticon.Geospatial.Manifest;

public sealed record SlopePolicy(
    double ImpassableSlopeDegrees = 45,
    double UnbuildableSlopeDegrees = 15);
