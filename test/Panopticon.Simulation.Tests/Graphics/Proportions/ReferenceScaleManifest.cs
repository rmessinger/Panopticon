using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Panopticon.Tests.Graphics.Proportions;

/// <summary>
/// Committed reference data for the G10 proportions audit (WI-G10-01). The manifest is copied to
/// the test output directory; tests never write it back. Mismatches against installation assets
/// are explicit failures: preserve the artifacts and report, never regenerate silently.
/// </summary>
internal sealed record ReferenceScaleManifest(ReferenceScaleBaseline Baseline, List<ReferenceScaleObject> Objects)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static ReferenceScaleManifest Load()
    {
        var path = Path.Combine(Environment.CurrentDirectory, "Data", "Graphics", "Proportions", "reference-scale-manifest.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ReferenceScaleManifest>(json, Options)
            ?? throw new InvalidOperationException($"Reference scale manifest at '{path}' is empty or invalid.");
    }
}

internal sealed record ReferenceScaleBaseline(
    string AnchorObject,
    string AnchorModel,
    decimal HullLengthUnits,
    List<string> HullExclusions,
    decimal ReferenceMeters,
    string ReferenceSource,
    decimal UnitsPerMeter);

internal sealed record ReferenceScaleObject(
    string ObjectName,
    string ClassName,
    string ModelName,
    decimal IniScale,
    string MeasurementAxis,
    string MeasurementLabel,
    List<string> Exclusions,
    decimal? HullLengthUnits,
    List<string>? HullExclusions,
    ReferenceScaleExtents Extents,
    ReferenceScaleGeometry Geometry,
    decimal? ReferenceMeters,
    string? ReferenceSource,
    decimal? ReferenceToleranceMeters,
    string W3dSha256);

internal sealed record ReferenceScaleExtents(decimal X, decimal Y, decimal Z);

internal sealed record ReferenceScaleGeometry(string Shape, decimal MajorRadius, decimal MinorRadius, decimal Height);
