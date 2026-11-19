using System.Collections.Generic;

namespace Panopticon.Geospatial.Manifest;

public sealed record GeospatialImportManifest
{
    public required IReadOnlyList<ManifestSource> Sources { get; init; }

    public required TargetCoordinateSystemSpec TargetCoordinateSystem { get; init; }

    public required double MetersPerGameUnit { get; init; }

    public required string DefaultMaterial { get; init; }

    public string? ExtentFeatureId { get; init; }

    public VerticalPolicy VerticalPolicy { get; init; } = new();

    public RasterPolicy RasterPolicy { get; init; } = new();

    public ToleranceSpec Tolerances { get; init; } = new();

    public IReadOnlyList<MaterialRule> MaterialRules { get; init; } = [];

    /// <summary>
    /// Opt-in line-corridor rules painted between classification samples and polygon
    /// rules; the whole line pass is dormant while this list is empty.
    /// </summary>
    public IReadOnlyList<LineMaterialRule> LineMaterialRules { get; init; } = [];

    public IReadOnlyList<FeatureMappingRule> FeatureMappings { get; init; } = [];

    public SlopePolicy SlopePolicy { get; init; } = new();

    /// <summary>Operator control points in declared order; empty when none are declared.</summary>
    public IReadOnlyList<ManifestControlPoint> ControlPoints { get; init; } = [];

    public double MaxScaleError { get; init; } = 0.001;
}
