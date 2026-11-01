using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;

namespace Panopticon.Geospatial.Generation;

/// <summary>
/// Generates passability and buildability mask chunks (B4b deep dive sections 5.1 and
/// 5.3): slope thresholds, then water, passability, and buildability regions in global
/// manifest rule order, then the playable-area polygon forcing both bits last.
/// </summary>
public static class TerrainMaskGenerator
{
    private const byte ImpassableBit = (byte)TerrainMaskFlags.Impassable;

    private const byte UnbuildableBit = (byte)TerrainMaskFlags.Unbuildable;

    private const byte BothBits = ImpassableBit | UnbuildableBit;

    private const double RadiansToDegrees = 180d / Math.PI;

    private static readonly GeometryFactory NtsFactory = GeometryFactory.Default;

    /// <summary>
    /// Generates the mask chunk for the chunk identified by <paramref name="terrain"/>.
    /// Rejections add typed diagnostics and return null; cancellation throws.
    /// </summary>
    public static TerrainMaskChunk? Generate(
        LocalMapGrid grid,
        TerrainChunk terrain,
        IReadOnlyList<GeneratedFeature>? features = null,
        SlopeThresholds? thresholds = null,
        TerrainMaskReport? report = null,
        GeoDiagnosticBag? diagnostics = null,
        GeospatialImportLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(terrain);

        var cx = terrain.ChunkX;
        var cy = terrain.ChunkY;
        ArgumentOutOfRangeException.ThrowIfNegative(cx);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cx, grid.ChunksX);
        ArgumentOutOfRangeException.ThrowIfNegative(cy);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cy, grid.ChunksY);

        diagnostics ??= new GeoDiagnosticBag();
        limits ??= new GeospatialImportLimits();
        cancellationToken.ThrowIfCancellationRequested();

        var slopeThresholds = thresholds ?? new SlopeThresholds();
        if (!IsValidThreshold(slopeThresholds.ImpassableDegrees) || !IsValidThreshold(slopeThresholds.UnbuildableDegrees))
        {
            return Fail(
                TerrainMaskDiagnosticCodes.TerrainMaskThresholdInvalid,
                $"Chunk ({cx}, {cy}): slope thresholds must be finite angles in [0, 90] degrees; found impassable "
                + $"{Format(slopeThresholds.ImpassableDegrees)} and unbuildable {Format(slopeThresholds.UnbuildableDegrees)}.");
        }

        int baseSampleX;
        int baseSampleY;
        int lastSampleX;
        int lastSampleY;

        try
        {
            // Checked before any int narrowing downstream uses the global sample range.
            baseSampleX = checked(cx * ChunkLayout.IntervalsPerChunk);
            baseSampleY = checked(cy * ChunkLayout.IntervalsPerChunk);
            lastSampleX = checked(baseSampleX + ChunkLayout.IntervalsPerChunk);
            lastSampleY = checked(baseSampleY + ChunkLayout.IntervalsPerChunk);
        }
        catch (OverflowException ex)
        {
            return Fail(
                GeoDiagnosticCodes.GenerateFailure,
                $"Chunk ({cx}, {cy}): the chunk's global sample indices overflow int32; the grid is too large.",
                ex);
        }

        var points = ChunkLayout.PointsPerChunkAxis;
        var flags = new byte[points * points];
        var context = new MaskContext(cx, cy, diagnostics, limits, cancellationToken);

        ApplySlopeStage(terrain, flags, slopeThresholds, out var slopeImpassable, out var slopeUnbuildable, cancellationToken);

        if (!TryCollectRegions(features, grid.PlayableArea, context, out var regions))
        {
            return null;
        }

        if (!TryApplyRegions(regions, flags, baseSampleX, baseSampleY, lastSampleX, lastSampleY, context, out var waterApplications, out var passabilityApplications, out var buildabilityApplications))
        {
            return null;
        }

        if (!TryApplyPlayableArea(grid, flags, baseSampleX, baseSampleY, lastSampleX, lastSampleY, context, out var forcedCells))
        {
            return null;
        }

        long impassableCells = 0;
        long unbuildableCells = 0;
        for (var i = 0; i < flags.Length; i++)
        {
            if ((flags[i] & ImpassableBit) != 0)
            {
                impassableCells++;
            }

            if ((flags[i] & UnbuildableBit) != 0)
            {
                unbuildableCells++;
            }
        }

        var chunk = new TerrainMaskChunk(cx, cy, flags);
        report?.Record(
            chunk,
            new TerrainMaskChunkStats(
                slopeImpassable,
                slopeUnbuildable,
                waterApplications,
                passabilityApplications,
                buildabilityApplications,
                forcedCells,
                impassableCells,
                unbuildableCells),
            slopeThresholds,
            grid.Transform.VerticalExaggeration);

        return chunk;

        TerrainMaskChunk? Fail(string code, string message, Exception? detail = null)
        {
            diagnostics.Add(new GeoDiagnostic(code, GeoStage.Generate, GeoSeverity.Error, message, detail: detail));

            return null;
        }
    }

    private static bool IsValidThreshold(double degrees) => degrees >= 0d && degrees <= 90d;

    /// <summary>
    /// Central differences on the stored apron. Heights carry the vertical conversion
    /// and exaggeration already, so the run is two lattice spacings in game units and no
    /// exaggeration is reapplied.
    /// </summary>
    private static void ApplySlopeStage(
        TerrainChunk terrain,
        byte[] flags,
        SlopeThresholds thresholds,
        out long impassableCells,
        out long unbuildableCells,
        CancellationToken cancellationToken)
    {
        var points = ChunkLayout.PointsPerChunkAxis;
        var run = 2d * ChunkLayout.SampleSpacingGameUnits;
        impassableCells = 0;
        unbuildableCells = 0;

        for (var sy = 0; sy < points; sy++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rowOffset = sy * points;
            var coreRow = sy + 1;

            for (var sx = 0; sx < points; sx++)
            {
                var coreColumn = sx + 1;

                // Samples widen to double before the subtraction, so the difference is
                // computed in double precision regardless of the float32 payload.
                double west = terrain.GetHeight(sx, coreRow);
                double east = terrain.GetHeight(sx + 2, coreRow);
                double south = terrain.GetHeight(coreColumn, sy);
                double north = terrain.GetHeight(coreColumn, sy + 2);
                var dzdx = (east - west) / run;
                var dzdy = (north - south) / run;
                var slopeDegrees = Math.Atan(Math.Sqrt((dzdx * dzdx) + (dzdy * dzdy))) * RadiansToDegrees;

                byte cell = 0;
                if (slopeDegrees > thresholds.ImpassableDegrees)
                {
                    cell |= ImpassableBit;
                    impassableCells++;
                }

                if (slopeDegrees > thresholds.UnbuildableDegrees)
                {
                    cell |= UnbuildableBit;
                    unbuildableCells++;
                }

                flags[rowOffset + sx] = cell;
            }
        }
    }

    /// <summary>
    /// Collects the region rules sorted by global rule index (input order breaking
    /// ties), after enforcing the feature and coordinate budgets: the coordinate budget
    /// covers every ring the chunk will copy, the playable-area polygon included.
    /// </summary>
    private static bool TryCollectRegions(
        IReadOnlyList<GeneratedFeature>? features,
        GeoPolygon playableArea,
        MaskContext context,
        out List<RegionEntry> regions)
    {
        regions = [];
        var playablePositions = CountRingPositions(playableArea, context);
        if (playablePositions < 0)
        {
            return false;
        }

        if (playablePositions > context.Limits.MaxCoordinateCount)
        {
            context.FailLimit(
                $"Chunk ({context.ChunkX}, {context.ChunkY}): {playablePositions} playable-area coordinates exceed the "
                + $"maxCoordinateCount limit {context.Limits.MaxCoordinateCount.ToString(CultureInfo.InvariantCulture)}.");

            return false;
        }

        if (features is not { Count: > 0 })
        {
            return true;
        }

        if (features.Count > context.Limits.MaxFeatureCount)
        {
            context.FailLimit(
                $"Chunk ({context.ChunkX}, {context.ChunkY}): {features.Count} generated features exceed the "
                + $"maxFeatureCount limit {context.Limits.MaxFeatureCount.ToString(CultureInfo.InvariantCulture)}.");

            return false;
        }

        long coordinates = playablePositions;
        for (var i = 0; i < features.Count; i++)
        {
            var feature = features[i];
            ArgumentNullException.ThrowIfNull(feature);

            if (!context.TryCharge(1))
            {
                return false;
            }

            if (feature.Kind is not (GeneratedFeatureKind.Water or GeneratedFeatureKind.Passability or GeneratedFeatureKind.Buildability))
            {
                continue;
            }

            // Unsupported geometry is rejected before any traversal: a nested multi
            // geometry must not be walked.
            if (feature.Geometry is not GeoPolygon polygon)
            {
                context.FailRegion(
                    $"Chunk ({context.ChunkX}, {context.ChunkY}): {DescribeKind(feature.Kind)} region part "
                    + $"'{feature.PartId}' must have polygon geometry; found {feature.Geometry.GetType().Name}.");

                return false;
            }

            if (feature.RuleIndex < 0)
            {
                context.FailRegion(
                    $"Chunk ({context.ChunkX}, {context.ChunkY}): {DescribeKind(feature.Kind)} region part "
                    + $"'{feature.PartId}' must carry the rule index of its manifest rule; found {feature.RuleIndex}.");

                return false;
            }

            var passable = false;
            var buildable = false;
            if (feature.Kind == GeneratedFeatureKind.Passability)
            {
                if (feature.Passable is not { } flag)
                {
                    context.FailRegion(
                        $"Chunk ({context.ChunkX}, {context.ChunkY}): passability region part '{feature.PartId}' requires a passable flag.");

                    return false;
                }

                passable = flag;
            }
            else if (feature.Kind == GeneratedFeatureKind.Buildability)
            {
                if (feature.Buildable is not { } flag)
                {
                    context.FailRegion(
                        $"Chunk ({context.ChunkX}, {context.ChunkY}): buildability region part '{feature.PartId}' requires a buildable flag.");

                    return false;
                }

                buildable = flag;
            }

            var positions = CountRingPositions(polygon, context);
            if (positions < 0)
            {
                return false;
            }

            coordinates += positions;
            if (coordinates > context.Limits.MaxCoordinateCount)
            {
                context.FailLimit(
                    $"Chunk ({context.ChunkX}, {context.ChunkY}): {coordinates} region and playable-area coordinates exceed "
                    + $"the maxCoordinateCount limit {context.Limits.MaxCoordinateCount.ToString(CultureInfo.InvariantCulture)}.");

                return false;
            }

            regions.Add(new RegionEntry(feature.RuleIndex, i, feature.Kind, feature.PartId, polygon, passable, buildable));
        }

        regions.Sort(
            static (left, right) => left.RuleIndex != right.RuleIndex
                ? left.RuleIndex.CompareTo(right.RuleIndex)
                : left.Order.CompareTo(right.Order));

        return true;
    }

    private static string DescribeKind(GeneratedFeatureKind kind) => kind switch
    {
        GeneratedFeatureKind.Water => "water",
        GeneratedFeatureKind.Passability => "passability",
        GeneratedFeatureKind.Buildability => "buildability",
        _ => kind.ToString(),
    };

    /// <summary>Returns -1 when the traversal stopped on an exhausted budget.</summary>
    private static long CountRingPositions(GeoPolygon polygon, MaskContext context)
    {
        long count = polygon.OuterRing.Positions.Count;
        if (!context.TryCharge(count))
        {
            return -1;
        }

        for (var i = 0; i < polygon.InnerRings.Count; i++)
        {
            var ringCount = polygon.InnerRings[i].Positions.Count;
            count += ringCount;

            if (!context.TryCharge(ringCount))
            {
                return -1;
            }
        }

        return count;
    }

    private static bool TryApplyRegions(
        List<RegionEntry> regions,
        byte[] flags,
        int baseSampleX,
        int baseSampleY,
        int lastSampleX,
        int lastSampleY,
        MaskContext context,
        out long waterApplications,
        out long passabilityApplications,
        out long buildabilityApplications)
    {
        waterApplications = 0;
        passabilityApplications = 0;
        buildabilityApplications = 0;
        var spacing = (double)ChunkLayout.SampleSpacingGameUnits;
        var points = ChunkLayout.PointsPerChunkAxis;
        var probe = new Coordinate();

        for (var r = 0; r < regions.Count && !context.Stopped; r++)
        {
            var region = regions[r];
            context.CancellationToken.ThrowIfCancellationRequested();
            var description = $"{DescribeKind(region.Kind)} region part '{region.PartId}'";

            if (!TryPreparePolygon(
                    region.Polygon,
                    context,
                    TerrainMaskDiagnosticCodes.TerrainMaskRegionInvalid,
                    description,
                    out var rings,
                    out var originX,
                    out var originY,
                    out var worldMinX,
                    out var worldMinY,
                    out var worldMaxX,
                    out var worldMaxY))
            {
                return false;
            }

            if (!TryGetLatticeRange(worldMinX, worldMinY, worldMaxX, worldMaxY, baseSampleX, baseSampleY, lastSampleX, lastSampleY, out var gxMin, out var gyMin, out var gxMax, out var gyMax))
            {
                continue;
            }

            for (var gy = gyMin; gy <= gyMax && !context.Stopped; gy++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var rowOffset = (gy - baseSampleY) * points;

                for (var gx = gxMin; gx <= gxMax && !context.Stopped; gx++)
                {
                    // The global index is computed before any double position, so the
                    // shared edge cells of adjacent chunks evaluate bit-identically.
                    probe.X = (gx * spacing) - originX;
                    probe.Y = (gy * spacing) - originY;
                    var contained = Contains(rings, probe, context, TerrainMaskDiagnosticCodes.TerrainMaskRegionInvalid, description);

                    if (context.Stopped)
                    {
                        return false;
                    }

                    if (!contained)
                    {
                        continue;
                    }

                    var index = rowOffset + (gx - baseSampleX);
                    if (region.Kind == GeneratedFeatureKind.Water)
                    {
                        flags[index] |= BothBits;
                        waterApplications++;
                    }
                    else if (region.Kind == GeneratedFeatureKind.Passability)
                    {
                        flags[index] = region.Passable
                            ? (byte)(flags[index] & ~ImpassableBit)
                            : (byte)(flags[index] | ImpassableBit);
                        passabilityApplications++;
                    }
                    else
                    {
                        flags[index] = region.Buildable
                            ? (byte)(flags[index] & ~UnbuildableBit)
                            : (byte)(flags[index] | UnbuildableBit);
                        buildabilityApplications++;
                    }
                }
            }

            if (context.Stopped)
            {
                return false;
            }
        }

        return !context.Stopped;
    }

    /// <summary>
    /// Forces both bits on every core sample outside the playable-area polygon. The
    /// outer boundary is contained; hole interiors and hole boundaries are not.
    /// </summary>
    private static bool TryApplyPlayableArea(
        LocalMapGrid grid,
        byte[] flags,
        int baseSampleX,
        int baseSampleY,
        int lastSampleX,
        int lastSampleY,
        MaskContext context,
        out long forcedCells)
    {
        forcedCells = 0;
        const string failCode = TerrainMaskDiagnosticCodes.TerrainMaskPlayableInvalid;
        const string description = "the playable-area polygon";

        if (!TryConvertPlayableArea(grid, context, out var playable))
        {
            return false;
        }

        if (!TryPreparePolygon(
                playable,
                context,
                failCode,
                description,
                out var rings,
                out var originX,
                out var originY,
                out var worldMinX,
                out var worldMinY,
                out var worldMaxX,
                out var worldMaxY))
        {
            return false;
        }

        var spacing = (double)ChunkLayout.SampleSpacingGameUnits;
        var points = ChunkLayout.PointsPerChunkAxis;
        var probe = new Coordinate();

        for (var gy = baseSampleY; gy <= lastSampleY && !context.Stopped; gy++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var rowOffset = (gy - baseSampleY) * points;

            for (var gx = baseSampleX; gx <= lastSampleX && !context.Stopped; gx++)
            {
                // The bbox test runs in world game units; classification runs in the
                // ring frame translated by the polygon's first vertex.
                var worldX = gx * spacing;
                var worldY = gy * spacing;
                var insideBox = worldX >= worldMinX && worldX <= worldMaxX && worldY >= worldMinY && worldY <= worldMaxY;
                probe.X = worldX - originX;
                probe.Y = worldY - originY;
                var contained = insideBox && Contains(rings, probe, context, failCode, description);

                if (context.Stopped)
                {
                    return false;
                }

                if (contained)
                {
                    continue;
                }

                flags[rowOffset + (gx - baseSampleX)] |= BothBits;
                forcedCells++;
            }
        }

        return !context.Stopped;
    }

    private static bool TryConvertPlayableArea(LocalMapGrid grid, MaskContext context, [NotNullWhen(true)] out GeoPolygon? converted)
    {
        var source = grid.PlayableArea;
        var outer = ConvertRing(source.OuterRing, grid, context, "outer ring");

        if (outer is null)
        {
            converted = null;
            return false;
        }

        var holes = new List<GeoRing>(source.InnerRings.Count);
        for (var i = 0; i < source.InnerRings.Count; i++)
        {
            var hole = ConvertRing(source.InnerRings[i], grid, context, $"hole {i}");

            if (hole is null)
            {
                converted = null;
                return false;
            }

            holes.Add(hole);
        }

        converted = new GeoPolygon(outer, holes);

        return true;
    }

    private static GeoRing? ConvertRing(GeoRing ring, LocalMapGrid grid, MaskContext context, string role)
    {
        var count = ring.Positions.Count;

        // Charged before the position copy: the budget must cover the allocation.
        if (!context.TryCharge(count))
        {
            return null;
        }

        var positions = new GeoPosition[count];

        for (var i = 0; i < count; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var game = grid.ProjectedToGame(ring.Positions[i].Coordinate);
            if (!double.IsFinite(game.X) || !double.IsFinite(game.Y))
            {
                context.FailPlayable(
                    $"Chunk ({context.ChunkX}, {context.ChunkY}): the playable-area {role} converts to a "
                    + "non-finite game coordinate.");

                return null;
            }

            positions[i] = new GeoPosition(game, ring.Positions[i].AltitudeMeters);
        }

        return new GeoRing(positions);
    }

    /// <summary>
    /// Prepares translated closed rings, validates their topology with NTS without
    /// repair, and returns the polygon's world game-unit bbox. Rings that bound no area
    /// and non-finite coordinates are rejected typed, as is invalid topology
    /// (self-intersection, holes outside the outer ring).
    /// </summary>
    private static bool TryPreparePolygon(
        GeoPolygon polygon,
        MaskContext context,
        string failCode,
        string description,
        out Coordinate[][] rings,
        out double originX,
        out double originY,
        out double worldMinX,
        out double worldMinY,
        out double worldMaxX,
        out double worldMaxY)
    {
        rings = [];
        var outerPositions = polygon.OuterRing.Positions;
        originX = outerPositions[0].Coordinate.X;
        originY = outerPositions[0].Coordinate.Y;
        worldMinX = worldMinY = worldMaxX = worldMaxY = 0d;

        var outer = PrepareRing(polygon.OuterRing, originX, originY, context);
        if (outer.Status == RingPrepStatus.Stopped)
        {
            return false;
        }

        if (outer.Status != RingPrepStatus.Prepared || outer.Ring is null)
        {
            context.Fail(failCode, RingMessage(context, "outer ring", description, outer.Status));

            return false;
        }

        var prepared = new List<Coordinate[]> { outer.Ring };

        for (var i = 0; i < polygon.InnerRings.Count && !context.Stopped; i++)
        {
            var hole = PrepareRing(polygon.InnerRings[i], originX, originY, context);
            if (hole.Status == RingPrepStatus.Stopped)
            {
                return false;
            }

            if (hole.Status != RingPrepStatus.Prepared || hole.Ring is null)
            {
                context.Fail(failCode, RingMessage(context, $"hole {i}", description, hole.Status));

                return false;
            }

            prepared.Add(hole.Ring);
        }

        if (context.Stopped || !TryValidateTopology(prepared, context, failCode, description))
        {
            return false;
        }

        rings = [.. prepared];
        var shell = rings[0];
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        for (var i = 0; i < shell.Length; i++)
        {
            minX = Math.Min(minX, shell[i].X);
            maxX = Math.Max(maxX, shell[i].X);
            minY = Math.Min(minY, shell[i].Y);
            maxY = Math.Max(maxY, shell[i].Y);
        }

        worldMinX = originX + minX;
        worldMinY = originY + minY;
        worldMaxX = originX + maxX;
        worldMaxY = originY + maxY;

        if (!IsFiniteQuad(worldMinX, worldMinY, worldMaxX, worldMaxY))
        {
            context.Fail(failCode, $"Chunk ({context.ChunkX}, {context.ChunkY}): the bbox of {description} is not finite.");

            return false;
        }

        return true;
    }

    private static string RingMessage(MaskContext context, string role, string description, RingPrepStatus status)
    {
        var problem = status == RingPrepStatus.NonFinite
            ? "has a coordinate that is not finite"
            : "bounds no area";

        return $"Chunk ({context.ChunkX}, {context.ChunkY}): {role} of {description} {problem}.";
    }

    /// <summary>
    /// Translates the ring by the outer origin, drops consecutive and closing
    /// duplicates, rejects non-finite coordinates and zero signed area, and returns the
    /// closed ring NTS and classification consume.
    /// </summary>
    private static PreparedRing PrepareRing(GeoRing ring, double originX, double originY, MaskContext context)
    {
        List<Coordinate> opened = [];
        var positions = ring.Positions;

        for (var i = 0; i < positions.Count; i++)
        {
            if (!context.TryCharge(1))
            {
                return new PreparedRing(RingPrepStatus.Stopped, null);
            }

            var x = positions[i].Coordinate.X - originX;
            var y = positions[i].Coordinate.Y - originY;
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                return new PreparedRing(RingPrepStatus.NonFinite, null);
            }

            if (opened.Count > 0 && opened[^1].X == x && opened[^1].Y == y)
            {
                continue;
            }

            opened.Add(new Coordinate(x, y));
        }

        if (opened.Count > 1 && opened[0].X == opened[^1].X && opened[0].Y == opened[^1].Y)
        {
            opened.RemoveAt(opened.Count - 1);
        }

        if (opened.Count < 3)
        {
            return new PreparedRing(RingPrepStatus.Degenerate, null);
        }

        // Exact zero area covers collinear rings and bowties; a non-finite sum flags
        // overflow in the derived arithmetic, not just in the inputs.
        var twiceArea = SignedTwiceArea(opened);
        if (!double.IsFinite(twiceArea))
        {
            return new PreparedRing(RingPrepStatus.NonFinite, null);
        }

        if (twiceArea == 0d)
        {
            return new PreparedRing(RingPrepStatus.Degenerate, null);
        }

        opened.Add(opened[0]);

        return new PreparedRing(RingPrepStatus.Prepared, [.. opened]);
    }

    // Translating by the first vertex avoids cancellation at large coordinates.
    private static double SignedTwiceArea(List<Coordinate> ring)
    {
        var originX = ring[0].X;
        var originY = ring[0].Y;
        double twiceArea = 0d;

        for (var i = 1; i + 1 < ring.Count; i++)
        {
            var ax = ring[i].X - originX;
            var ay = ring[i].Y - originY;
            var bx = ring[i + 1].X - originX;
            var by = ring[i + 1].Y - originY;
            twiceArea += (ax * by) - (bx * ay);
        }

        return twiceArea;
    }

    /// <summary>
    /// Validates the rings as one NTS polygon. The whole-ring vertex work is charged
    /// before any NTS allocation; invalid topology is reported typed, never repaired.
    /// </summary>
    private static bool TryValidateTopology(List<Coordinate[]> rings, MaskContext context, string failCode, string description)
    {
        long vertices = 0;
        for (var i = 0; i < rings.Count; i++)
        {
            vertices += rings[i].Length;
        }

        if (!context.TryCharge(vertices))
        {
            return false;
        }

        try
        {
            var shell = NtsFactory.CreateLinearRing(rings[0]);
            var holes = new LinearRing[rings.Count - 1];
            for (var i = 1; i < rings.Count; i++)
            {
                holes[i - 1] = NtsFactory.CreateLinearRing(rings[i]);
            }

            var validator = new IsValidOp(NtsFactory.CreatePolygon(shell, holes));
            _ = validator.IsValid;
            if (validator.ValidationError is { } error)
            {
                context.Fail(
                    failCode,
                    $"Chunk ({context.ChunkX}, {context.ChunkY}): {description} has invalid topology ({error.Message}); no repair is applied.");

                return false;
            }
        }
        catch (Exception ex) when (ex is TopologyException or ArgumentException)
        {
            context.Fail(
                failCode,
                $"Chunk ({context.ChunkX}, {context.ChunkY}): topology validation of {description} failed: {ex.Message}.");

            return false;
        }

        return true;
    }

    /// <summary>
    /// Boundary-classified point-in-polygon: the outer ring's interior and boundary
    /// contain, then every hole's interior or boundary excludes, unambiguously.
    /// </summary>
    private static bool Contains(Coordinate[][] rings, Coordinate probe, MaskContext context, string failCode, string description)
    {
        if (Locate(rings[0], probe, context, failCode, description) == Location.Exterior)
        {
            return false;
        }

        for (var i = 1; i < rings.Length && !context.Stopped; i++)
        {
            if (Locate(rings[i], probe, context, failCode, description) != Location.Exterior)
            {
                return false;
            }
        }

        return !context.Stopped;
    }

    /// <summary>The whole-ring vertex work is charged before the classification call.</summary>
    private static Location Locate(Coordinate[] ring, Coordinate probe, MaskContext context, string failCode, string description)
    {
        if (!context.TryCharge(ring.Length))
        {
            return Location.Exterior;
        }

        try
        {
            return PointLocation.LocateInRing(probe, ring);
        }
        catch (Exception ex) when (ex is TopologyException or ArgumentException)
        {
            context.Fail(
                failCode,
                $"Chunk ({context.ChunkX}, {context.ChunkY}): locating a sample against {description} failed: {ex.Message}.");

            return Location.Exterior;
        }
    }

    /// <summary>
    /// Clips the polygon bbox to the chunk's world range and derives lattice indices:
    /// the casts then stay inside [base, last]. Returns false when no core sample of
    /// this chunk lies in the bbox.
    /// </summary>
    private static bool TryGetLatticeRange(
        double worldMinX,
        double worldMinY,
        double worldMaxX,
        double worldMaxY,
        int baseSampleX,
        int baseSampleY,
        int lastSampleX,
        int lastSampleY,
        out int gxMin,
        out int gyMin,
        out int gxMax,
        out int gyMax)
    {
        gxMin = gyMin = gxMax = gyMax = 0;
        var spacing = (double)ChunkLayout.SampleSpacingGameUnits;
        var chunkMinX = baseSampleX * spacing;
        var chunkMinY = baseSampleY * spacing;
        var chunkMaxX = lastSampleX * spacing;
        var chunkMaxY = lastSampleY * spacing;
        var clippedMinX = Math.Max(worldMinX, chunkMinX);
        var clippedMinY = Math.Max(worldMinY, chunkMinY);
        var clippedMaxX = Math.Min(worldMaxX, chunkMaxX);
        var clippedMaxY = Math.Min(worldMaxY, chunkMaxY);

        var minGx = Math.Ceiling(clippedMinX / spacing);
        var maxGx = Math.Floor(clippedMaxX / spacing);
        var minGy = Math.Ceiling(clippedMinY / spacing);
        var maxGy = Math.Floor(clippedMaxY / spacing);
        if (minGx > maxGx || minGy > maxGy || !IsFiniteQuad(minGx, minGy, maxGx, maxGy))
        {
            return false;
        }

        gxMin = (int)Math.Clamp(minGx, baseSampleX, lastSampleX);
        gxMax = (int)Math.Clamp(maxGx, baseSampleX, lastSampleX);
        gyMin = (int)Math.Clamp(minGy, baseSampleY, lastSampleY);
        gyMax = (int)Math.Clamp(maxGy, baseSampleY, lastSampleY);

        return true;
    }

    private static bool IsFiniteQuad(double minX, double minY, double maxX, double maxY)
    {
        return double.IsFinite(minX) && double.IsFinite(minY) && double.IsFinite(maxX) && double.IsFinite(maxY);
    }

    private static string Format(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private readonly record struct RegionEntry(int RuleIndex, int Order, GeneratedFeatureKind Kind, string PartId, GeoPolygon Polygon, bool Passable, bool Buildable);

    private readonly record struct PreparedRing(RingPrepStatus Status, Coordinate[]? Ring);

    private enum RingPrepStatus
    {
        Prepared,

        Stopped,

        NonFinite,

        Degenerate,
    }

    /// <summary>
    /// One chunk's mask vector budget: every feature check, ring position, NTS ring
    /// validation, and point classification charge against MaxVectorWorkPerChunk; the
    /// first exhaustion or invalid polygon stops all stages with one typed error.
    /// </summary>
    private sealed class MaskContext(
        int chunkX,
        int chunkY,
        GeoDiagnosticBag diagnostics,
        GeospatialImportLimits limits,
        CancellationToken cancellationToken)
    {
        private long _budgetRemaining = limits.MaxVectorWorkPerChunk;

        private long _unitsSinceCancellationCheck;

        public int ChunkX { get; } = chunkX;

        public int ChunkY { get; } = chunkY;

        public GeospatialImportLimits Limits { get; } = limits;

        public CancellationToken CancellationToken { get; } = cancellationToken;

        public bool Stopped { get; private set; }

        public bool TryCharge(long units)
        {
            if (Stopped)
            {
                return false;
            }

            _budgetRemaining -= units;
            if (_budgetRemaining < 0)
            {
                FailLimit(
                    $"Chunk ({ChunkX}, {ChunkY}): terrain mask vector work exceeded the maxVectorWorkPerChunk budget "
                    + $"of {Limits.MaxVectorWorkPerChunk.ToString(CultureInfo.InvariantCulture)} units.");

                return false;
            }

            // Ring and edge loops are long; stay responsive to cancellation inside them.
            _unitsSinceCancellationCheck += units;
            if (_unitsSinceCancellationCheck >= 256)
            {
                _unitsSinceCancellationCheck = 0;
                CancellationToken.ThrowIfCancellationRequested();
            }

            return true;
        }

        public void Fail(string code, string message)
        {
            Stop(code, message);
        }

        public void FailRegion(string message)
        {
            Stop(TerrainMaskDiagnosticCodes.TerrainMaskRegionInvalid, message);
        }

        public void FailPlayable(string message)
        {
            Stop(TerrainMaskDiagnosticCodes.TerrainMaskPlayableInvalid, message);
        }

        public void FailLimit(string message)
        {
            Stop(TerrainMaskDiagnosticCodes.TerrainMaskLimitExceeded, message);
        }

        private void Stop(string code, string message)
        {
            if (Stopped)
            {
                return;
            }

            Stopped = true;
            diagnostics.Add(new GeoDiagnostic(code, GeoStage.Generate, GeoSeverity.Error, message));
        }
    }
}
