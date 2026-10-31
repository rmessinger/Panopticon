using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Projection;

namespace Panopticon.Geospatial.Grid;

/// <summary>
/// Chunk-aligned local grid for one generated map (B4b deep dive sections 4 and 5.1a): the
/// retained <see cref="GeospatialMapTransform"/>, the chunk counts, the sample lattice anchored
/// at the southwest origin of the projected extent, and the projected playable-area polygon.
/// </summary>
public sealed class LocalMapGrid
{
    private const int NorthernUtmEpsgBase = 32600;

    private const int SouthernUtmEpsgBase = 32700;

    // Round-off budgets, in ULPs, applied only when a chunk ratio is within them of a whole
    // number: a handful of ULPs of the ratio itself plus the same budget for the two
    // projected coordinates whose difference is the width. This absorbs only floating-point
    // round-off; physical coverage (for example projection scale inflation) always rounds
    // outward to the next chunk.
    private const int RatioUlpBudget = 8;

    private const int CoordinateUlpBudget = 4;

    // Keeps global sample indices (chunks * 129 + apron) inside int range.
    private const int MaxChunksPerAxis = int.MaxValue / ChunkLayout.PointsPerChunkAxis;

    private readonly int _maxSampleX;

    private readonly int _maxSampleY;

    private readonly double _sampleSpacingMeters;

    private LocalMapGrid(GeospatialMapTransform transform, GeoPolygon playableArea, int chunksX, int chunksY)
    {
        Transform = transform;
        PlayableArea = playableArea;
        ChunksX = chunksX;
        ChunksY = chunksY;
        _maxSampleX = checked(chunksX * ChunkLayout.IntervalsPerChunk);
        _maxSampleY = checked(chunksY * ChunkLayout.IntervalsPerChunk);
        _sampleSpacingMeters = ChunkLayout.SampleSpacingGameUnits * transform.MetersPerGameUnit;
    }

    /// <summary>Retained map-level coordinate and elevation metadata.</summary>
    public GeospatialMapTransform Transform { get; }

    /// <summary>
    /// Playable-area polygon in projected target-CRS meters, holes retained; samples outside it
    /// are impassable and unbuildable.
    /// </summary>
    public GeoPolygon PlayableArea { get; }

    public int ChunksX { get; }

    public int ChunksY { get; }

    public CoordinateSystemId ProjectedCoordinateSystem => Transform.ProjectedCoordinateSystem;

    /// <summary>
    /// Creates the grid for <paramref name="extentFeature"/>: resolves the target CRS,
    /// projects the extent, rejects excessive point scale error, and rounds the containing
    /// rectangle outward to whole chunks. Returns null after adding an error diagnostic.
    /// </summary>
    public static LocalMapGrid? Create(
        GeospatialFeature extentFeature,
        GeospatialImportManifest manifest,
        ICoordinateTransformer transformer,
        GeoDiagnosticBag diagnostics,
        GeospatialImportLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(extentFeature);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(transformer);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (extentFeature.Geometry is not GeoPolygon polygon)
        {
            AddError(
                diagnostics,
                GeoDiagnosticCodes.CoordinatesInvalidExtent,
                $"The extent feature '{extentFeature.FeatureId}' must be a single polygon; found {DescribeGeometry(extentFeature.Geometry)}.");
            return null;
        }

        return Create(polygon, extentFeature.CoordinateSystem, manifest, transformer, diagnostics, limits);
    }

    /// <summary>Convenience overload of <see cref="Create"/> for an extent given directly as a polygon.</summary>
    public static LocalMapGrid? Create(
        GeoPolygon extentPolygon,
        CoordinateSystemId extentCoordinateSystem,
        GeospatialImportManifest manifest,
        ICoordinateTransformer transformer,
        GeoDiagnosticBag diagnostics,
        GeospatialImportLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(extentPolygon);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(transformer);
        ArgumentNullException.ThrowIfNull(diagnostics);
        var sources = manifest.Sources;
        var targetSpec = manifest.TargetCoordinateSystem;
        var verticalPolicy = manifest.VerticalPolicy;
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(targetSpec);
        ArgumentNullException.ThrowIfNull(verticalPolicy);
        limits ??= new GeospatialImportLimits();

        if (!ValidateManifestParameters(manifest, diagnostics))
        {
            return null;
        }

        if (!IsExplicitCoordinateSystem(extentCoordinateSystem))
        {
            AddError(
                diagnostics,
                GeoDiagnosticCodes.CoordinatesInvalidExtent,
                $"The extent coordinate system must be an explicit EPSG or Transverse Mercator definition; found {extentCoordinateSystem}.");
            return null;
        }

        if (!ValidateExtentRings(extentPolygon, diagnostics))
        {
            return null;
        }

        if (!TryGetGeographicBounds(extentPolygon.OuterRing, extentCoordinateSystem, transformer, diagnostics, out var bounds))
        {
            return null;
        }

        var target = ResolveTargetCoordinateSystem(targetSpec, bounds.CenterLongitudeDegrees);

        if (!PassesScaleCheck(target, bounds, manifest.MaxScaleError, diagnostics))
        {
            return null;
        }

        if (!TryProjectPolygon(extentPolygon, extentCoordinateSystem, target, transformer, diagnostics, out var playableArea))
        {
            return null;
        }

        var (minEasting, maxEasting, minNorthing, maxNorthing) = ProjectedBounds(playableArea.OuterRing);
        if (!(maxEasting > minEasting) || !(maxNorthing > minNorthing))
        {
            AddError(
                diagnostics,
                GeoDiagnosticCodes.CoordinatesInvalidExtent,
                "The projected extent is degenerate: it has zero width or zero height.");
            return null;
        }

        var chunkSpanMeters = ChunkLayout.ChunkSpanGameUnits * manifest.MetersPerGameUnit;
        if (!TryGetChunkCount(
                (maxEasting - minEasting) / chunkSpanMeters,
                chunkSpanMeters,
                Math.Max(Math.Abs(minEasting), Math.Abs(maxEasting)),
                diagnostics,
                out var chunksX))
        {
            return null;
        }

        if (!TryGetChunkCount(
                (maxNorthing - minNorthing) / chunkSpanMeters,
                chunkSpanMeters,
                Math.Max(Math.Abs(minNorthing), Math.Abs(maxNorthing)),
                diagnostics,
                out var chunksY))
        {
            return null;
        }

        var sampleSpacingMeters = ChunkLayout.SampleSpacingGameUnits * manifest.MetersPerGameUnit;
        var maxSampleX = (chunksX * ChunkLayout.IntervalsPerChunk) + ChunkLayout.ApronSamplesPerSide;
        var maxSampleY = (chunksY * ChunkLayout.IntervalsPerChunk) + ChunkLayout.ApronSamplesPerSide;
        if (!double.IsFinite(minEasting - sampleSpacingMeters)
            || !double.IsFinite(minEasting + (maxSampleX * sampleSpacingMeters))
            || !double.IsFinite(minNorthing - sampleSpacingMeters)
            || !double.IsFinite(minNorthing + (maxSampleY * sampleSpacingMeters)))
        {
            AddError(
                diagnostics,
                GeoDiagnosticCodes.CoordinatesExtentOverflow,
                "The chunk-aligned grid corners are not finite: the extent or metersPerGameUnit is too large for projected coordinates.");
            return null;
        }

        // Checked before any generation allocation happens downstream.
        if ((long)chunksX * chunksY > limits.MaxChunkCount)
        {
            AddError(
                diagnostics,
                GeoDiagnosticCodes.GenerateFailure,
                $"The map extent requires {chunksX} x {chunksY} = {(long)chunksX * chunksY} chunks, exceeding the maxChunkCount limit {limits.MaxChunkCount}.",
                stage: GeoStage.Generate);
            return null;
        }

        var mapTransform = new GeospatialMapTransform(
            [extentCoordinateSystem],
            target,
            new Vector2d(minEasting, minNorthing),
            manifest.MetersPerGameUnit,
            ResolveVerticalDatum(manifest),
            verticalPolicy.OriginMeters,
            verticalPolicy.Exaggeration);

        return new LocalMapGrid(mapTransform, playableArea, chunksX, chunksY);
    }

    /// <summary>
    /// Projected position of global lattice sample (x, y). Indices count samples from the
    /// southwest origin across all chunks and cover the one-sample apron on each side:
    /// -<see cref="ChunkLayout.ApronSamplesPerSide"/> through
    /// chunks * <see cref="ChunkLayout.IntervalsPerChunk"/> + apron inclusive.
    /// </summary>
    public Vector2d SampleToProjected(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(x, -ChunkLayout.ApronSamplesPerSide);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(x, _maxSampleX + ChunkLayout.ApronSamplesPerSide);
        ArgumentOutOfRangeException.ThrowIfLessThan(y, -ChunkLayout.ApronSamplesPerSide);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(y, _maxSampleY + ChunkLayout.ApronSamplesPerSide);

        var origin = Transform.OriginEastingNorthing;

        return new Vector2d(origin.X + (x * _sampleSpacingMeters), origin.Y + (y * _sampleSpacingMeters));
    }

    /// <summary>
    /// Game-units position of a projected point: easting maps to game +X and northing to
    /// game +Y, relative to the southwest origin.
    /// </summary>
    public Vector2d ProjectedToGame(Vector2d projected)
    {
        var origin = Transform.OriginEastingNorthing;

        return new Vector2d(
            (projected.X - origin.X) / Transform.MetersPerGameUnit,
            (projected.Y - origin.Y) / Transform.MetersPerGameUnit);
    }

    private static bool ValidateManifestParameters(GeospatialImportManifest manifest, GeoDiagnosticBag diagnostics)
    {
        var valid = true;

        if (!IsPositiveFinite(manifest.MetersPerGameUnit))
        {
            ManifestValueInvalid($"metersPerGameUnit must be a positive finite number; found {Format(manifest.MetersPerGameUnit)}.");
        }
        else if (!IsPositiveFinite(ChunkLayout.ChunkSpanGameUnits * manifest.MetersPerGameUnit))
        {
            ManifestValueInvalid($"metersPerGameUnit {Format(manifest.MetersPerGameUnit)} overflows the chunk span to a non-finite value.");
        }

        if (!IsPositiveFinite(manifest.MaxScaleError))
        {
            ManifestValueInvalid($"maxScaleError must be a positive finite number; found {Format(manifest.MaxScaleError)}.");
        }

        if (!double.IsFinite(manifest.VerticalPolicy.OriginMeters))
        {
            ManifestValueInvalid($"verticalPolicy.originMeters must be a finite number; found {Format(manifest.VerticalPolicy.OriginMeters)}.");
        }

        if (!IsPositiveFinite(manifest.VerticalPolicy.Exaggeration))
        {
            ManifestValueInvalid($"verticalPolicy.exaggeration must be a positive finite number; found {Format(manifest.VerticalPolicy.Exaggeration)}.");
        }

        ValidateTargetCoordinateSystem(manifest.TargetCoordinateSystem);

        return valid;

        void ManifestValueInvalid(string message)
        {
            AddManifestValueInvalid(diagnostics, message);
            valid = false;
        }

        void ValidateTargetCoordinateSystem(TargetCoordinateSystemSpec spec)
        {
            if (spec.Kind is not (ManifestCrsKind.Utm or ManifestCrsKind.TransverseMercator))
            {
                ManifestValueInvalid($"targetCoordinateSystem.type must be 'utm' or 'transverse-mercator'; found '{spec.Kind}'.");
                return;
            }

            if (spec.Kind == ManifestCrsKind.Utm)
            {
                if (spec.UtmZone is not { } zone || zone is < 1 or > 60)
                {
                    ManifestValueInvalid($"targetCoordinateSystem.zone must be between 1 and 60 for type 'utm'; found {FormatNullable(spec.UtmZone)}.");
                }

                if (spec.Hemisphere is not { } hemisphere)
                {
                    ManifestValueInvalid("targetCoordinateSystem.hemisphere is required for type 'utm'.");
                }
                else if (hemisphere is not (ManifestHemisphere.North or ManifestHemisphere.South))
                {
                    ManifestValueInvalid($"targetCoordinateSystem.hemisphere must be 'north' or 'south'; found '{hemisphere}'.");
                }

                if (spec.CentralMeridianDegrees is not null)
                {
                    ManifestValueInvalid("targetCoordinateSystem.centralMeridian applies to type 'transverse-mercator' only.");
                }
            }
            else
            {
                if (spec.UtmZone is not null)
                {
                    ManifestValueInvalid("targetCoordinateSystem.zone applies to type 'utm' only.");
                }

                if (spec.Hemisphere is not null)
                {
                    ManifestValueInvalid("targetCoordinateSystem.hemisphere applies to type 'utm' only.");
                }

                if (spec.CentralMeridianDegrees is { } meridian
                    && (!double.IsFinite(meridian) || meridian <= -180d || meridian > 180d))
                {
                    ManifestValueInvalid($"targetCoordinateSystem.centralMeridian must be a finite value in (-180, 180] degrees; found {Format(meridian)}.");
                }
            }
        }
    }

    private static CoordinateSystemId ResolveTargetCoordinateSystem(
        TargetCoordinateSystemSpec spec,
        double defaultCentralMeridianDegrees)
    {
        if (spec.Kind == ManifestCrsKind.Utm)
        {
            return CoordinateSystemId.Utm(spec.UtmZone!.Value, spec.Hemisphere == ManifestHemisphere.North);
        }

        // Custom Transverse Mercator: scale 1, zero false origin; the central meridian
        // defaults to the extent's WGS84 bounding-box centre rounded half away from zero
        // to 0.01 degrees (B4b deep dive section 4).
        var resolved = spec.CentralMeridianDegrees
            ?? Math.Round(defaultCentralMeridianDegrees, 2, MidpointRounding.AwayFromZero);

        return CoordinateSystemId.TransverseMercator(resolved, 1d, 0d, 0d);
    }

    private static bool PassesScaleCheck(
        CoordinateSystemId target,
        in GeographicBounds bounds,
        double maxScaleError,
        GeoDiagnosticBag diagnostics)
    {
        var projection = CreateTargetProjection(target);
        var centerLongitude = bounds.CenterLongitudeDegrees;
        var centerLatitude = (bounds.SouthDegrees + bounds.NorthDegrees) / 2d;
        var points = new (double Longitude, double Latitude)[]
        {
            (bounds.WestDegrees, bounds.SouthDegrees),
            (bounds.EastDegrees, bounds.SouthDegrees),
            (bounds.WestDegrees, bounds.NorthDegrees),
            (bounds.EastDegrees, bounds.NorthDegrees),
            (centerLongitude, bounds.SouthDegrees),
            (centerLongitude, bounds.NorthDegrees),
            (bounds.WestDegrees, centerLatitude),
            (bounds.EastDegrees, centerLatitude),
        };

        var worstError = 0d;
        var worstLongitude = 0d;
        var worstLatitude = 0d;
        foreach (var (longitude, latitude) in points)
        {
            try
            {
                var error = Math.Abs((projection.ScaleFactor(latitude, longitude) / projection.K0) - 1d);
                if (error > worstError)
                {
                    worstError = error;
                    worstLongitude = longitude;
                    worstLatitude = latitude;
                }
            }
            catch (ArgumentException ex)
            {
                AddError(
                    diagnostics,
                    GeoDiagnosticCodes.CoordinatesFailure,
                    $"Scale check failed at {Format(longitude)} E, {Format(latitude)} N: the point is outside the projectable domain.",
                    ex);
                return false;
            }
        }

        if (worstError > maxScaleError)
        {
            AddError(
                diagnostics,
                GeoDiagnosticCodes.CoordinatesScaleError,
                $"Point scale error {Format(worstError)} at {Format(worstLongitude)} E, {Format(worstLatitude)} N exceeds maxScaleError {Format(maxScaleError)}; move the extent or declare a central meridian closer to it.");
            return false;
        }

        return true;
    }

    private static TransverseMercatorProjection CreateTargetProjection(CoordinateSystemId id)
    {
        if (id.EpsgCode is { } epsgCode)
        {
            var northern = epsgCode < SouthernUtmEpsgBase;

            return TransverseMercatorProjection.Utm(epsgCode - (northern ? NorthernUtmEpsgBase : SouthernUtmEpsgBase), northern);
        }

        return new TransverseMercatorProjection(id.CentralMeridianDegrees, id.Scale, id.FalseEasting, id.FalseNorthing);
    }

    private static bool ValidateExtentRings(GeoPolygon extentPolygon, GeoDiagnosticBag diagnostics)
    {
        if (!ValidateRing(extentPolygon.OuterRing, "The extent ring", diagnostics))
        {
            return false;
        }

        for (var i = 0; i < extentPolygon.InnerRings.Count; i++)
        {
            if (!ValidateRing(extentPolygon.InnerRings[i], $"Extent hole {i}", diagnostics))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateRing(GeoRing ring, string role, GeoDiagnosticBag diagnostics)
    {
        var positions = ring.Positions;
        foreach (var position in positions)
        {
            if (!double.IsFinite(position.Coordinate.X) || !double.IsFinite(position.Coordinate.Y))
            {
                AddError(diagnostics, GeoDiagnosticCodes.CoordinatesInvalidExtent, "The extent contains a coordinate that is not finite.");
                return false;
            }
        }

        var first = positions[0].Coordinate;
        var last = positions[^1].Coordinate;
        if (first.X != last.X || first.Y != last.Y)
        {
            AddError(diagnostics, GeoDiagnosticCodes.CoordinatesInvalidExtent, $"{role} must be closed: its first and last positions must coincide.");
            return false;
        }

        var distinct = new HashSet<(double X, double Y)>();
        foreach (var position in positions)
        {
            distinct.Add((position.Coordinate.X, position.Coordinate.Y));
        }

        if (distinct.Count < 3)
        {
            AddError(diagnostics, GeoDiagnosticCodes.CoordinatesInvalidExtent, $"{role} must have at least three distinct positions.");
            return false;
        }

        if (SignedTwiceArea(ring) == 0d)
        {
            AddError(diagnostics, GeoDiagnosticCodes.CoordinatesInvalidExtent, $"{role} has zero area.");
            return false;
        }

        return true;
    }

    // Translating avoids cancellation at large projected origins.
    private static double SignedTwiceArea(GeoRing ring)
    {
        var positions = ring.Positions;
        var origin = positions[0].Coordinate;
        var twiceArea = 0d;
        for (var i = 1; i < positions.Count - 1; i++)
        {
            var ax = positions[i].Coordinate.X - origin.X;
            var ay = positions[i].Coordinate.Y - origin.Y;
            var bx = positions[i + 1].Coordinate.X - origin.X;
            var by = positions[i + 1].Coordinate.Y - origin.Y;
            twiceArea += (ax * by) - (bx * ay);
        }

        return twiceArea;
    }

    private static bool TryGetGeographicBounds(
        GeoRing outerRing,
        CoordinateSystemId extentCoordinateSystem,
        ICoordinateTransformer transformer,
        GeoDiagnosticBag diagnostics,
        out GeographicBounds bounds)
    {
        bounds = default;
        var first = true;
        double west = 0d;
        double east = 0d;
        double south = 0d;
        double north = 0d;
        foreach (var position in outerRing.Positions)
        {
            if (!TryProjectPosition(position, extentCoordinateSystem, CoordinateSystemId.Wgs84, transformer, diagnostics, out var geographic))
            {
                return false;
            }

            if (first)
            {
                west = east = geographic.X;
                south = north = geographic.Y;
                first = false;
            }
            else
            {
                west = Math.Min(west, geographic.X);
                east = Math.Max(east, geographic.X);
                south = Math.Min(south, geographic.Y);
                north = Math.Max(north, geographic.Y);
            }
        }

        bounds = new GeographicBounds(west, south, east, north);

        return true;
    }

    private static bool TryProjectPolygon(
        GeoPolygon polygon,
        CoordinateSystemId source,
        CoordinateSystemId target,
        ICoordinateTransformer transformer,
        GeoDiagnosticBag diagnostics,
        [NotNullWhen(true)] out GeoPolygon? projected)
    {
        if (!TryProjectRing(polygon.OuterRing, source, target, transformer, diagnostics, out var outerRing))
        {
            projected = null;
            return false;
        }

        var innerRings = new List<GeoRing>(polygon.InnerRings.Count);
        foreach (var ring in polygon.InnerRings)
        {
            if (!TryProjectRing(ring, source, target, transformer, diagnostics, out var innerRing))
            {
                projected = null;
                return false;
            }

            innerRings.Add(innerRing);
        }

        projected = new GeoPolygon(outerRing, innerRings);

        return true;
    }

    private static bool TryProjectRing(
        GeoRing ring,
        CoordinateSystemId source,
        CoordinateSystemId target,
        ICoordinateTransformer transformer,
        GeoDiagnosticBag diagnostics,
        [NotNullWhen(true)] out GeoRing? projected)
    {
        var positions = new GeoPosition[ring.Positions.Count];
        for (var i = 0; i < ring.Positions.Count; i++)
        {
            if (!TryProjectPosition(ring.Positions[i], source, target, transformer, diagnostics, out var coordinate))
            {
                projected = null;
                return false;
            }

            positions[i] = new GeoPosition(coordinate, ring.Positions[i].AltitudeMeters);
        }

        projected = new GeoRing(positions);

        return true;
    }

    private static bool TryProjectPosition(
        GeoPosition position,
        CoordinateSystemId source,
        CoordinateSystemId target,
        ICoordinateTransformer transformer,
        GeoDiagnosticBag diagnostics,
        out Vector2d projected)
    {
        if (!double.IsFinite(position.Coordinate.X) || !double.IsFinite(position.Coordinate.Y))
        {
            AddError(diagnostics, GeoDiagnosticCodes.CoordinatesInvalidExtent, "The extent contains a coordinate that is not finite.");
            projected = default;
            return false;
        }

        if (source == target)
        {
            projected = position.Coordinate;
            return true;
        }

        if (transformer is WgsTransverseMercatorTransformer concrete)
        {
            if (!concrete.TryTransform(position.Coordinate, source, target, out projected, out var diagnostic))
            {
                diagnostics.Add(diagnostic);
                return false;
            }
        }
        else
        {
            try
            {
                projected = transformer.Transform(position.Coordinate, source, target);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                AddError(
                    diagnostics,
                    GeoDiagnosticCodes.CoordinatesFailure,
                    $"Coordinate transform from {source} to {target} failed: {ex.Message}",
                    ex);
                projected = default;
                return false;
            }
        }

        if (!double.IsFinite(projected.X) || !double.IsFinite(projected.Y))
        {
            AddError(
                diagnostics,
                GeoDiagnosticCodes.CoordinatesInvalidExtent,
                $"Coordinate transform from {source} to {target} produced a non-finite result.");
            return false;
        }

        return true;
    }

    private static (double MinEasting, double MaxEasting, double MinNorthing, double MaxNorthing) ProjectedBounds(GeoRing outerRing)
    {
        var first = true;
        double minEasting = 0d;
        double maxEasting = 0d;
        double minNorthing = 0d;
        double maxNorthing = 0d;
        foreach (var position in outerRing.Positions)
        {
            if (first)
            {
                minEasting = maxEasting = position.Coordinate.X;
                minNorthing = maxNorthing = position.Coordinate.Y;
                first = false;
            }
            else
            {
                minEasting = Math.Min(minEasting, position.Coordinate.X);
                maxEasting = Math.Max(maxEasting, position.Coordinate.X);
                minNorthing = Math.Min(minNorthing, position.Coordinate.Y);
                maxNorthing = Math.Max(maxNorthing, position.Coordinate.Y);
            }
        }

        return (minEasting, maxEasting, minNorthing, maxNorthing);
    }

    private static bool TryGetChunkCount(double ratio, double spanMeters, double coordinateMagnitude, GeoDiagnosticBag diagnostics, out int chunks)
    {
        // Reject overflow before converting to integer chunk counts.
        if (!double.IsFinite(ratio) || ratio > MaxChunksPerAxis)
        {
            AddError(
                diagnostics,
                GeoDiagnosticCodes.CoordinatesExtentOverflow,
                $"The map extent spans {Format(ratio)} chunk spans on one axis, beyond the supported maximum of {MaxChunksPerAxis} chunks per axis.");
            chunks = 0;
            return false;
        }

        var roundOffGuard = (RatioUlpBudget * UlpOf(ratio)) + (CoordinateUlpBudget * UlpOf(coordinateMagnitude) / spanMeters);
        var rounded = Math.Round(ratio);
        if (rounded >= 1 && Math.Abs(ratio - rounded) <= roundOffGuard)
        {
            chunks = (int)rounded;
        }
        else
        {
            chunks = Math.Max(1, (int)Math.Ceiling(ratio));
        }

        return true;
    }

    private static double UlpOf(double value)
    {
        var magnitude = Math.Abs(value);

        return Math.BitIncrement(magnitude) - magnitude;
    }

    private static bool IsExplicitCoordinateSystem(CoordinateSystemId id)
    {
        if (id.EpsgCode is not null)
        {
            return true;
        }

        return double.IsFinite(id.Scale)
            && id.Scale > 0d
            && double.IsFinite(id.CentralMeridianDegrees)
            && double.IsFinite(id.FalseEasting)
            && double.IsFinite(id.FalseNorthing);
    }

    private static string ResolveVerticalDatum(GeospatialImportManifest manifest)
    {
        // The manifest validator enforces exactly one elevation source; anything else falls
        // back to unknown. Raster source CRS are appended by the import pipeline, not here.
        ManifestSource? elevation = null;
        var count = 0;
        foreach (var source in manifest.Sources)
        {
            if (source.Role == ManifestSourceRole.Elevation)
            {
                elevation = source;
                count++;
            }
        }

        var datum = count == 1 ? elevation!.VerticalDatum : null;

        return string.IsNullOrWhiteSpace(datum) ? "unknown" : datum.Trim();
    }

    private static string DescribeGeometry(GeoGeometry geometry) => geometry switch
    {
        GeoPointGeometry => "a point",
        GeoLineString => "a line string",
        GeoPolygon => "a polygon",
        GeoMultiGeometry => "a multi geometry",
        _ => geometry.GetType().Name,
    };

    private static void AddError(
        GeoDiagnosticBag diagnostics,
        string code,
        string message,
        Exception? detail = null,
        GeoStage stage = GeoStage.Coordinates)
    {
        diagnostics.Add(new GeoDiagnostic(code, stage, GeoSeverity.Error, message, detail: detail));
    }

    private static void AddManifestValueInvalid(GeoDiagnosticBag diagnostics, string message)
    {
        AddError(diagnostics, GeoDiagnosticCodes.ManifestValueInvalid, message, stage: GeoStage.Manifest);
    }

    private static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0d;

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string FormatNullable(double? value) => value is null ? "null" : Format(value.Value);

    private readonly record struct GeographicBounds(
        double WestDegrees,
        double SouthDegrees,
        double EastDegrees,
        double NorthDegrees)
    {
        public double CenterLongitudeDegrees => (WestDegrees + EastDegrees) / 2d;
    }
}
