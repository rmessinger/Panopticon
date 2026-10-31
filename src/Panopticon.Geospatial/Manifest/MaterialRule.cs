namespace Panopticon.Geospatial.Manifest;

public sealed record MaterialRule(
    string Material,
    int? ClassificationValue = null,
    string? SourceId = null,
    string? Attribute = null,
    string? Value = null);
