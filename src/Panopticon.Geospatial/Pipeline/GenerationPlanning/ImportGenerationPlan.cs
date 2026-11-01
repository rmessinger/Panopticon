using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Pipeline.SourceLoading;
using Panopticon.Geospatial.Projection;
using Panopticon.Geospatial.Sampling;

namespace Panopticon.Geospatial.Pipeline.GenerationPlanning;

/// <summary>
/// Immutable source, grid, policy and provenance inputs for chunk generation.
/// </summary>
internal sealed record ImportGenerationPlan
{
    private ImportGenerationPlan(
        PreparedImportSources sources,
        ICoordinateTransformer transformer,
        LocalMapGrid grid,
        MaterialPalette palette,
        SamplingOptions samplingOptions,
        SlopeThresholds slopeThresholds,
        PayloadGridDefinition gridDefinition,
        ReadOnlyCollection<PayloadSourceProvenance> provenance)
    {
        Sources = sources;
        Transformer = transformer;
        Grid = grid;
        Palette = palette;
        SamplingOptions = samplingOptions;
        SlopeThresholds = slopeThresholds;
        GridDefinition = gridDefinition;
        Provenance = provenance;
    }

    /// <summary>The exact prepared snapshot this plan was built from; never rebuilt or copied.</summary>
    public PreparedImportSources Sources { get; }

    /// <summary>The exact manifest instance the snapshot carries.</summary>
    public GeospatialImportManifest Manifest => Sources.LoadedManifest;

    /// <summary>SHA-256 of the manifest bytes as 64 lowercase hex characters.</summary>
    public string ManifestSha256 => Sources.ManifestSha256;

    /// <summary>The single transformer used for grid construction and all later generation.</summary>
    public ICoordinateTransformer Transformer { get; }

    public LocalMapGrid Grid { get; }

    /// <summary>Global palette shared by every material consumer; entry 0 is the default.</summary>
    public MaterialPalette Palette { get; }

    public SamplingOptions SamplingOptions { get; }

    public SlopeThresholds SlopeThresholds { get; }

    public PayloadGridDefinition GridDefinition { get; }

    /// <summary>Provenance of every import source in manifest order.</summary>
    public IReadOnlyList<PayloadSourceProvenance> Provenance { get; }

    /// <summary>
    /// Builds from the selected polygon, returning diagnostics for invalid generation policies.
    /// </summary>
    public static ImportGenerationPlan? Create(
        PreparedImportSources sources,
        GeoDiagnosticBag diagnostics,
        GeospatialImportLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(diagnostics);
        cancellationToken.ThrowIfCancellationRequested();
        limits ??= new GeospatialImportLimits();

        var manifest = sources.LoadedManifest;
        var transformer = new WgsTransverseMercatorTransformer();

        var grid = LocalMapGrid.Create(
            sources.SelectedExtent.Polygon,
            sources.SelectedExtent.Feature.CoordinateSystem,
            manifest,
            transformer,
            diagnostics,
            limits);

        if (grid is null)
        {
            return null;
        }

        MaterialPalette palette;

        try
        {
            palette = MaterialPalette.FromRules(manifest.DefaultMaterial, manifest.MaterialRules, manifest.FeatureMappings);
        }
        catch (ArgumentException ex)
        {
            ImportGenerationPlanDiagnostics.AddError(
                diagnostics,
                ImportGenerationPlanDiagnostics.PaletteInvalidCode,
                $"The material palette cannot be built from the manifest rules: {ex.Message}",
                ex);
            return null;
        }

        SamplingOptions samplingOptions;

        try
        {
            samplingOptions = BuildSamplingOptions(manifest.RasterPolicy);
        }
        catch (ArgumentException ex)
        {
            ImportGenerationPlanDiagnostics.AddError(
                diagnostics,
                ImportGenerationPlanDiagnostics.SamplingOptionsInvalidCode,
                $"The sampling policy cannot be derived from rasterPolicy: {ex.Message}",
                ex);
            return null;
        }

        var slopeThresholds = new SlopeThresholds(
            manifest.SlopePolicy.ImpassableSlopeDegrees,
            manifest.SlopePolicy.UnbuildableSlopeDegrees);

        if (!IsValidSlopeThreshold(slopeThresholds.ImpassableDegrees)
            || !IsValidSlopeThreshold(slopeThresholds.UnbuildableDegrees))
        {
            ImportGenerationPlanDiagnostics.AddError(
                diagnostics,
                ImportGenerationPlanDiagnostics.SlopeThresholdsInvalidCode,
                "Slope thresholds must be finite angles in [0, 90] degrees; found "
                + $"impassable {slopeThresholds.ImpassableDegrees:R} and unbuildable {slopeThresholds.UnbuildableDegrees:R}.");
            return null;
        }

        var provenance = BuildProvenance(sources, diagnostics, cancellationToken);

        if (provenance is null)
        {
            return null;
        }

        // The payload transform carries every actual source CRS plus the reconciled DEM
        // datum; the grid keeps its own extent-only transform untouched.
        var payloadTransform = new GeospatialMapTransform(
            CollectSourceCoordinateSystems(sources, cancellationToken),
            grid.ProjectedCoordinateSystem,
            grid.Transform.OriginEastingNorthing,
            grid.Transform.MetersPerGameUnit,
            sources.EffectiveDemDatum,
            grid.Transform.VerticalOriginMeters,
            grid.Transform.VerticalExaggeration);

        int? imageryPixelsPerSample = sources.Imagery is null ? null : manifest.RasterPolicy.ImageryPixelsPerSample;

        PayloadGridDefinition gridDefinition;

        try
        {
            gridDefinition = new PayloadGridDefinition(
                grid.ChunksX,
                grid.ChunksY,
                payloadTransform,
                grid.PlayableArea,
                imageryPixelsPerSample,
                slopeThresholds);
        }
        catch (ArgumentException ex)
        {
            ImportGenerationPlanDiagnostics.AddError(
                diagnostics,
                ImportGenerationPlanDiagnostics.PayloadGridInvalidCode,
                $"The payload grid definition is invalid: {ex.Message}",
                ex);
            return null;
        }

        return new ImportGenerationPlan(
            sources,
            transformer,
            grid,
            palette,
            samplingOptions,
            slopeThresholds,
            gridDefinition,
            provenance);
    }

    private static SamplingOptions BuildSamplingOptions(RasterPolicy policy)
    {
        return new SamplingOptions(
            ToNoDataPolicy(policy.NoData),
            policy.NoDataSearchRadiusMeters,
            policy.NoDataConstantMeters,
            ToEdgePolicy(policy.Edge),
            policy.EdgeConstantMeters,
            policy.EdgeSearchRadiusMeters);
    }

    private static NoDataPolicy ToNoDataPolicy(ManifestNoDataPolicy policy) => policy switch
    {
        ManifestNoDataPolicy.Fail => NoDataPolicy.Fail,
        ManifestNoDataPolicy.NearestValid => NoDataPolicy.NearestValid,
        ManifestNoDataPolicy.Constant => NoDataPolicy.Constant,
        _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Undefined no-data policy."),
    };

    private static EdgePolicy ToEdgePolicy(ManifestEdgePolicy policy) => policy switch
    {
        ManifestEdgePolicy.Fail => EdgePolicy.Fail,
        ManifestEdgePolicy.Nearest => EdgePolicy.Nearest,
        ManifestEdgePolicy.Constant => EdgePolicy.Constant,
        _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Undefined edge policy."),
    };

    private static bool IsValidSlopeThreshold(double degrees) => double.IsFinite(degrees) && degrees >= 0d && degrees <= 90d;

    private static ReadOnlyCollection<PayloadSourceProvenance>? BuildProvenance(
        PreparedImportSources sources,
        GeoDiagnosticBag diagnostics,
        CancellationToken cancellationToken)
    {
        var provenance = new PayloadSourceProvenance[sources.Sources.Count];

        for (var i = 0; i < sources.Sources.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = sources.Sources[i];

            try
            {
                provenance[i] = new PayloadSourceProvenance(
                    source.SourceId,
                    source.Kind,
                    source.Role,
                    ImportGenerationPlanPaths.CanonicalizeProvenancePath(source.RelativePath),
                    source.Sha256Hex,
                    source.LengthBytes,
                    source.ActualCoordinateSystem,
                    source.DeclaredVerticalDatum,
                    source.EmbeddedVerticalDatum,
                    source.Label,
                    source.Attribution,
                    source.HorizontalAccuracyMeters,
                    source.VerticalAccuracyMeters);
            }
            catch (ArgumentException ex)
            {
                ImportGenerationPlanDiagnostics.AddError(
                    diagnostics,
                    ImportGenerationPlanDiagnostics.ProvenanceInvalidCode,
                    $"The provenance of source '{source.SourceId}' is invalid: {ex.Message}",
                    ex);
                return null;
            }
        }

        return new ReadOnlyCollection<PayloadSourceProvenance>(provenance);
    }

    private static List<CoordinateSystemId> CollectSourceCoordinateSystems(
        PreparedImportSources sources,
        CancellationToken cancellationToken)
    {
        var systems = new List<CoordinateSystemId>();
        var seen = new HashSet<CoordinateSystemId>();

        foreach (var source in sources.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (seen.Add(source.ActualCoordinateSystem))
            {
                systems.Add(source.ActualCoordinateSystem);
            }
        }

        return systems;
    }
}
