using System.Collections.Generic;

namespace Panopticon.Geospatial.Manifest;

/// <summary>
/// One named operator control point (WI-B4b-20): a WGS84 position checked against the
/// generated payload after staging. Independent horizontal truth is either the paired
/// expected game XY or a matched generated feature id, never both; imagery comparison
/// is a per-channel byte threshold applied independently to R, G, and B.
/// </summary>
public sealed record ManifestControlPoint(
    string Name,
    double Latitude,
    double Longitude,
    double? ElevationMeters = null,
    double? ExpectedGameX = null,
    double? ExpectedGameY = null,
    string? FeatureId = null,
    string? FeatureSourceId = null,
    IReadOnlyList<int>? ImageryRgb = null,
    int? ImageryToleranceBytes = null);
