#nullable enable

using System;
using System.Collections.Generic;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Pipeline.SourceLoading;

namespace Panopticon.Geospatial.Tests.Pipeline;

/// <summary>
/// Direct unit fixtures for <see cref="ImportGenerationPlan"/>: prepared snapshots built
/// from synthetic loaded sources without repeating file import. The extent CRS equals the
/// manifest's resolved Transverse Mercator target, so projecting the extent is the
/// identity and expected grid numbers are exact.
/// </summary>
internal static class PlanTestSupport
{
    public static readonly CoordinateSystemId ExtentCrs = CoordinateSystemId.TransverseMercator(3d, 1d, 0d, 0d);

    public static GeospatialImportManifest ImportManifest(
        IReadOnlyList<ManifestSource>? manifestSources = null,
        IReadOnlyList<MaterialRule>? materialRules = null,
        IReadOnlyList<FeatureMappingRule>? featureMappings = null,
        RasterPolicy? rasterPolicy = null,
        SlopePolicy? slopePolicy = null,
        VerticalPolicy? verticalPolicy = null,
        double metersPerGameUnit = 1d)
    {
        return new GeospatialImportManifest
        {
            Sources = manifestSources ?? DefaultManifestSources(),
            TargetCoordinateSystem = new TargetCoordinateSystemSpec(
                ManifestCrsKind.TransverseMercator,
                CentralMeridianDegrees: 3d),
            MetersPerGameUnit = metersPerGameUnit,
            DefaultMaterial = "dirt",
            MaterialRules = materialRules ?? [],
            FeatureMappings = featureMappings ?? [],
            RasterPolicy = rasterPolicy ?? new RasterPolicy(),
            SlopePolicy = slopePolicy ?? new SlopePolicy(),
            VerticalPolicy = verticalPolicy ?? new VerticalPolicy(),
        };
    }

    /// <summary>Extent and elevation manifest sources, the minimum the grid inspects.</summary>
    public static List<ManifestSource> DefaultManifestSources(string elevationDatum = "unknown")
    {
        return
        [
            new ManifestSource("extent", ManifestSourceKind.Kml, "inputs/area.kml", ManifestSourceRole.Extent),
            new ManifestSource("dem", ManifestSourceKind.Geotiff, "inputs/dem.tif", ManifestSourceRole.Elevation, VerticalDatum: elevationDatum),
        ];
    }

    public static LoadedSource Source(
        string sourceId,
        ManifestSourceRole role,
        ManifestSourceKind kind = ManifestSourceKind.Geotiff,
        CoordinateSystemId? actualCoordinateSystem = null,
        string declaredVerticalDatum = "unknown",
        string? embeddedVerticalDatum = null,
        string? effectiveVerticalDatum = null,
        string? label = null,
        string? attribution = null,
        double? horizontalAccuracyMeters = null,
        double? verticalAccuracyMeters = null,
        string? relativePath = null)
    {
        return new LoadedSource(
            sourceId,
            kind,
            role,
            relativePath ?? $"inputs/{sourceId}.dat",
            $"/work/inputs/{sourceId}.dat",
            Sha256(sourceId),
            1000L + sourceId.Length,
            actualCoordinateSystem ?? ExtentCrs,
            declaredVerticalDatum,
            embeddedVerticalDatum,
            effectiveVerticalDatum ?? ResolveEffectiveDatum(declaredVerticalDatum, embeddedVerticalDatum),
            label,
            attribution,
            horizontalAccuracyMeters,
            verticalAccuracyMeters);
    }

    /// <summary>The default loaded pair matching <see cref="DefaultManifestSources"/>.</summary>
    public static List<LoadedSource> DefaultLoadedSources(
        CoordinateSystemId? demCoordinateSystem = null,
        string? demEmbeddedDatum = null)
    {
        return
        [
            Source("extent", ManifestSourceRole.Extent, ManifestSourceKind.Kml),
            Source("dem", ManifestSourceRole.Elevation, actualCoordinateSystem: demCoordinateSystem, embeddedVerticalDatum: demEmbeddedDatum),
        ];
    }

    public static PreparedImportSources Prepared(
        GeospatialImportManifest manifest,
        IReadOnlyList<LoadedSource> sources,
        SelectedExtentFeature selectedExtent,
        IReadOnlyList<GeospatialFeature>? vectorFeatures = null)
    {
        LoadedSource? elevation = null;
        LoadedSource? imagery = null;
        LoadedSource? classification = null;

        foreach (var source in sources)
        {
            if (source.Role == ManifestSourceRole.Elevation)
            {
                elevation = source;
            }
            else if (source.Role == ManifestSourceRole.Imagery)
            {
                imagery = source;
            }
            else if (source.Role == ManifestSourceRole.Classification)
            {
                classification = source;
            }
        }

        return new PreparedImportSources(
            manifest,
            Sha256("manifest"),
            null,
            sources,
            vectorFeatures ?? [],
            selectedExtent,
            RasterOf(elevation ?? throw new InvalidOperationException("The fixture needs an elevation source.")),
            imagery is null ? null : RasterOf(imagery),
            classification is null ? null : RasterOf(classification));
    }

    /// <summary>Axis-aligned closed rectangle in the extent CRS.</summary>
    public static GeoPolygon TmRectangle(double minEasting, double minNorthing, double widthMeters, double heightMeters)
    {
        return new GeoPolygon(
            new GeoRing(
            [
                new GeoPosition(new Vector2d(minEasting, minNorthing)),
                new GeoPosition(new Vector2d(minEasting + widthMeters, minNorthing)),
                new GeoPosition(new Vector2d(minEasting + widthMeters, minNorthing + heightMeters)),
                new GeoPosition(new Vector2d(minEasting, minNorthing + heightMeters)),
                new GeoPosition(new Vector2d(minEasting, minNorthing)),
            ]));
    }

    /// <summary>Extent selection whose owning feature is the polygon itself.</summary>
    public static SelectedExtentFeature PolygonExtent(GeoPolygon polygon, string featureId = "playable")
    {
        return new SelectedExtentFeature(
            new GeospatialFeature("extent", featureId, polygon, ExtentCrs),
            polygon);
    }

    /// <summary>Extent selection whose owning feature is a multi geometry around the polygon.</summary>
    public static SelectedExtentFeature MultiExtent(GeoPolygon selected, GeoGeometry other, string featureId = "zone")
    {
        return new SelectedExtentFeature(
            new GeospatialFeature("extent", featureId, new GeoMultiGeometry([selected, other]), ExtentCrs),
            selected);
    }

    public static LoadedRaster RasterOf(LoadedSource source)
    {
        return new LoadedRaster(
            source,
            new GeospatialRaster<double>(
                1,
                1,
                1,
                new AffineTransform(0d, 1d, 0d, 0d, 0d, -1d),
                source.ActualCoordinateSystem,
                new[] { 0d }));
    }

    public static string Sha256(string seed) => seed.Length.ToString("x2").PadLeft(64, '0');

    private static string ResolveEffectiveDatum(string declared, string? embedded)
    {
        if (IsKnownDatum(declared))
        {
            return declared.Trim();
        }

        if (embedded is not null && IsKnownDatum(embedded))
        {
            return embedded.Trim();
        }

        return "unknown";
    }

    private static bool IsKnownDatum(string datum)
    {
        var key = datum.Trim();

        return string.Equals(key, "egm96", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "egm2008", StringComparison.OrdinalIgnoreCase);
    }
}
