namespace Panopticon.Geospatial.Manifest;

public sealed record ToleranceSpec(
    double? HorizontalMeters = null,
    double? VerticalMeters = null,
    double? RelativeVerticalMeters = null);
