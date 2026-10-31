namespace Panopticon.Geospatial.Manifest;

public sealed record VerticalPolicy(
    double OriginMeters = 0,
    double Exaggeration = 1);
