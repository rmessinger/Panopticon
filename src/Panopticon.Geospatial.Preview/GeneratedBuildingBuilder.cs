using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate.Polygon;
using Panopticon.Content.Importing.Maps;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Payload.Models;
using GeneratedFeature = Panopticon.Geospatial.Features.GeneratedFeature;

namespace Panopticon.Geospatial.Preview;

// Builds the generated-building layer from the ORIGINAL payload emissions (the same list
// BuildVectorLayers consumes, before provenance dedupe): one building per (SourceId,
// FeatureId), one part per source part stable id, heights with counted median fallbacks,
// conservative-skirt terrain seating, and flat or prototype-gabled roofs triangulated with
// the NTS PolygonTriangulator. Every grouping and emission order is sorted explicitly so
// identical inputs produce identical BuildingLayerSet instances and therefore
// byte-identical packages.
public static class GeneratedBuildingBuilder
{
    private const string CleabsAttribute = "cleabs";
    private const string HeightAttribute = "hauteur";
    private const string UsageAttribute = "usage_1";

    private const string HeightSourceAttribute = "SourceAttribute";
    private const string HeightSourceUsageMedian = "UsageMedian";
    private const string HeightSourceGlobalMedian = "GlobalMedian";

    private const string FlatRoofRule = "Flat";
    private const string GabledRoofRule = "Gabled";

    private const string SeatMethod = "ConservativeSkirtCellRange";

    private const double SinkGameUnits = 1d;
    private const double GableSlopeRadians = 35d * Math.PI / 180d;
    private const double MinimumGableAreaGameUnits = 1d;
    private const int MinimumGableUniqueVertices = 4;
    private const double SampleSpacing = ChunkLayout.SampleSpacingGameUnits;
    private const int IntervalsPerChunk = ChunkLayout.IntervalsPerChunk;
    private const int ChunkSpanGameUnits = ChunkLayout.ChunkSpanGameUnits;
    private const int CorePointsPerChunkAxis = ChunkLayout.PointsPerChunkAxis;
    private const int SegmentVertexLimit = 65535;
    private const int TrianglesPerSegmentLimit = SegmentVertexLimit / 3;
    private const int RoofVariantOffset = 4;

    internal static (BuildingLayerSet? Layer, IReadOnlyList<string> Diagnostics, IReadOnlyList<string> Conflicts) Build(
        IReadOnlyList<GeneratedFeature> features,
        PayloadGridDefinition grid,
        IReadOnlyList<Chunk> chunks)
    {
        var conflicts = new List<string>();
        var parts = CollectParts(features, conflicts);

        if (conflicts.Count > 0)
        {
            return (null, [], conflicts);
        }

        var buildings = GroupPartsIntoBuildings(parts, conflicts);

        if (conflicts.Count > 0)
        {
            return (null, [], conflicts);
        }

        if (buildings.Count == 0)
        {
            return (null, [], conflicts);
        }

        ResolveHeights(buildings, conflicts, out var sourceHeights, out var usageFallbacks, out var globalFallbacks);

        if (conflicts.Count > 0)
        {
            return (null, [], conflicts);
        }

        var transform = grid.Transform;
        var latticeWidth = (grid.ChunksX * IntervalsPerChunk) + 1;
        var latticeHeight = (grid.ChunksY * IntervalsPerChunk) + 1;
        var chunkLookup = chunks.ToDictionary(chunk => (chunk.Key.Cx, chunk.Key.Cy));

        var layerBuildings = new List<GeneratedBuilding>(buildings.Count);
        var chunkTriangles = new Dictionary<(int ChunkX, int ChunkY), Dictionary<int, List<MeshTriangle>>>();
        var gabledCount = 0;
        var flatCount = 0;

        foreach (var building in buildings)
        {
            building.HeightGameUnits = building.HeightMeters / transform.MetersPerGameUnit * transform.VerticalExaggeration;
            building.MaterialVariant = MaterialVariantOf(building.SourceId, building.FeatureId);

            var localParts = building.Parts
                .OrderBy(part => part.StableId, StringComparer.Ordinal)
                .Select(part => new LocalPart(part.StableId, part.Geometry, LocalPolygons(part.Geometry)))
                .ToArray();

            SeatBuilding(building, localParts, latticeWidth, latticeHeight, grid.ChunksX, grid.ChunksY, chunkLookup, conflicts);

            if (conflicts.Count > 0)
            {
                return (null, [], conflicts);
            }

            var failureReasons = localParts.Select(RoofFailureReason).ToArray();
            var failingPart = Array.FindIndex(failureReasons, reason => reason is not null);
            var gabled = failingPart < 0;
            building.RoofRule = gabled ? GabledRoofRule : FlatRoofRule;
            building.RoofFallbackReason = gabled ? null : failureReasons[failingPart];

            if (gabled)
            {
                gabledCount++;
            }
            else
            {
                flatCount++;
            }

            var baseZ = building.SupportMinZ - SinkGameUnits;
            var eaveZ = building.SupportMaxZ + building.HeightGameUnits;
            var buildingParts = new List<BuildingPart>(localParts.Length);

            foreach (var localPart in localParts)
            {
                if (GeneratedMapPackageConverter.ConvertVectorGeometry(localPart.Geometry, conflicts) is not { } footprint)
                {
                    conflicts.Add(Format(
                        GeneratedMapPackageDiagnosticCodes.BuildingConflict,
                        $"Building '{building.SourceId}/{building.FeatureId}' part '{localPart.StableId}' carries an unsupported footprint geometry."));
                    return (null, [], conflicts);
                }

                var (centroidX, centroidY) = FirstPolygonCentroid(localPart.Polygons);
                var chunkX = ClampIndex(Math.Floor(centroidX / ChunkSpanGameUnits), grid.ChunksX - 1);
                var chunkY = ClampIndex(Math.Floor(centroidY / ChunkSpanGameUnits), grid.ChunksY - 1);
                buildingParts.Add(new BuildingPart(localPart.StableId, footprint, chunkX, chunkY));

                try
                {
                    AppendPartTriangles(chunkTriangles, chunkX, chunkY, building, localPart, gabled, baseZ, eaveZ);
                }
                catch (Exception ex)
                {
                    conflicts.Add(Format(
                        GeneratedMapPackageDiagnosticCodes.BuildingConflict,
                        $"Building '{building.SourceId}/{building.FeatureId}' part '{localPart.StableId}' geometry generation failed: {ex.Message}"));
                    return (null, [], conflicts);
                }
            }

            layerBuildings.Add(new GeneratedBuilding(
                building.SourceId,
                building.FeatureId,
                building.Attributes,
                building.HeightMeters,
                building.HeightSource,
                building.HeightGameUnits,
                building.RoofRule,
                building.RoofFallbackReason,
                building.MaterialVariant,
                SeatMethod,
                building.SupportMinZ,
                building.SupportMaxZ,
                SinkGameUnits,
                baseZ,
                eaveZ,
                buildingParts));
        }

        var meshChunks = new List<BuildingMeshChunk>(chunkTriangles.Count);
        var triangleCount = 0;

        foreach (var chunkKey in chunkTriangles.Keys.OrderBy(key => key.ChunkX).ThenBy(key => key.ChunkY))
        {
            var buckets = chunkTriangles[chunkKey];
            var segments = new List<BuildingMeshSegment>(buckets.Count);

            foreach (var variant in buckets.Keys.OrderBy(variant => variant))
            {
                var triangles = buckets[variant];
                triangleCount += triangles.Count;

                for (var offset = 0; offset < triangles.Count; offset += TrianglesPerSegmentLimit)
                {
                    segments.Add(BuildSegment(variant, triangles, offset, Math.Min(TrianglesPerSegmentLimit, triangles.Count - offset)));
                }
            }

            meshChunks.Add(new BuildingMeshChunk(chunkKey.ChunkX, chunkKey.ChunkY, segments));
        }

        var summary = string.Create(
            CultureInfo.InvariantCulture,
            $"{GeneratedMapPackageDiagnosticCodes.BuildingGenerated}: Generated {layerBuildings.Count} buildings from {parts.Count} source parts ({sourceHeights} source heights, {usageFallbacks} usage-median fallbacks, {globalFallbacks} global-median fallbacks; roofs: {gabledCount} gabled, {flatCount} flat; triangles: {triangleCount}; mesh chunks: {meshChunks.Count}).");

        return (new BuildingLayerSet(layerBuildings, meshChunks), [summary], conflicts);
    }

    private static BuildingMeshSegment BuildSegment(int variant, List<MeshTriangle> triangles, int offset, int count)
    {
        var vertices = new List<BuildingVertex>(count * 3);
        var indices = new List<uint>(count * 3);
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        double maxZ = double.NegativeInfinity;

        for (var triangle = offset; triangle < offset + count; triangle++)
        {
            AppendVertex(vertices, indices, triangles[triangle].A, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
            AppendVertex(vertices, indices, triangles[triangle].B, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
            AppendVertex(vertices, indices, triangles[triangle].C, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
        }

        return new BuildingMeshSegment(variant, vertices, indices, minX, minY, minZ, maxX, maxY, maxZ);
    }

    private static void AppendVertex(
        List<BuildingVertex> vertices,
        List<uint> indices,
        BuildingVertex vertex,
        ref double minX,
        ref double minY,
        ref double minZ,
        ref double maxX,
        ref double maxY,
        ref double maxZ)
    {
        minX = Math.Min(minX, vertex.X);
        minY = Math.Min(minY, vertex.Y);
        minZ = Math.Min(minZ, vertex.Z);
        maxX = Math.Max(maxX, vertex.X);
        maxY = Math.Max(maxY, vertex.Y);
        maxZ = Math.Max(maxZ, vertex.Z);
        indices.Add((uint)vertices.Count);
        vertices.Add(vertex);
    }

    private static List<BuildingPartSource> CollectParts(IReadOnlyList<GeneratedFeature> features, List<string> conflicts)
    {
        var parts = new Dictionary<string, BuildingPartSource>(StringComparer.Ordinal);

        foreach (var feature in features)
        {
            if (!IsBuildingCandidate(feature))
            {
                continue;
            }

            var stableId = GeneratedMapPackageConverter.StableIdOf(feature);
            var geometryText = GeneratedMapPackageConverter.VectorGeometryText(feature.Geometry);

            if (parts.TryGetValue(stableId, out var first))
            {
                if (!string.Equals(first.GeometryText, geometryText, StringComparison.Ordinal)
                    || !GeneratedMapPackageConverter.SameAttributes(first.Attributes, feature.Attributes))
                {
                    conflicts.Add(Format(
                        GeneratedMapPackageDiagnosticCodes.BuildingConflict,
                        $"Building part '{stableId}' is emitted again with different source geometry or attributes; the conflicting building emissions cannot be merged."));
                }

                continue;
            }

            parts.Add(stableId, new BuildingPartSource(stableId, feature.SourceId, feature.FeatureId, feature.Attributes, feature.Geometry, geometryText));
        }

        return parts.Values.ToList();
    }

    private static bool IsBuildingCandidate(GeneratedFeature feature) =>
        feature.Geometry is GeoPolygon or GeoMultiGeometry
        && feature.Attributes.TryGetValue(CleabsAttribute, out var cleabs)
        && !string.IsNullOrEmpty(cleabs);

    private static List<BuildingSource> GroupPartsIntoBuildings(List<BuildingPartSource> parts, List<string> conflicts)
    {
        var groups = new Dictionary<(string SourceId, string FeatureId), BuildingSource>();

        foreach (var part in parts)
        {
            var key = (part.SourceId, part.FeatureId);

            if (!groups.TryGetValue(key, out var group))
            {
                group = new BuildingSource(part.SourceId, part.FeatureId, part.Attributes);
                groups.Add(key, group);
            }
            else if (!GeneratedMapPackageConverter.SameAttributes(group.Attributes, part.Attributes))
            {
                conflicts.Add(Format(
                    GeneratedMapPackageDiagnosticCodes.BuildingConflict,
                    $"Building '{part.SourceId}/{part.FeatureId}' carries parts with differing attributes; the conflicting building parts cannot be merged."));
            }

            group.Parts.Add(part);
        }

        return groups.Values
            .OrderBy(group => group.SourceId, StringComparer.Ordinal)
            .ThenBy(group => group.FeatureId, StringComparer.Ordinal)
            .ToList();
    }

    private static void ResolveHeights(
        List<BuildingSource> buildings,
        List<string> conflicts,
        out int sourceHeights,
        out int usageFallbacks,
        out int globalFallbacks)
    {
        var usageHeights = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        var globalHeights = new List<double>();
        var parsed = new double?[buildings.Count];

        for (var index = 0; index < buildings.Count; index++)
        {
            var attributes = buildings[index].Attributes;
            parsed[index] = ParseHeight(attributes);

            if (parsed[index] is { } height)
            {
                var usage = attributes.TryGetValue(UsageAttribute, out var usageValue) ? usageValue : string.Empty;

                if (!usageHeights.TryGetValue(usage, out var bucket))
                {
                    bucket = [];
                    usageHeights.Add(usage, bucket);
                }

                bucket.Add(height);
                globalHeights.Add(height);
            }
        }

        sourceHeights = 0;
        usageFallbacks = 0;
        globalFallbacks = 0;

        for (var index = 0; index < buildings.Count; index++)
        {
            var building = buildings[index];

            if (parsed[index] is { } height)
            {
                building.HeightMeters = height;
                building.HeightSource = HeightSourceAttribute;
                sourceHeights++;
                continue;
            }

            var usageKey = building.Attributes.TryGetValue(UsageAttribute, out var usageValue) ? usageValue : string.Empty;

            if (usageHeights.TryGetValue(usageKey, out var usageBucket) && usageBucket.Count > 0)
            {
                building.HeightMeters = Median(usageBucket);
                building.HeightSource = HeightSourceUsageMedian;
                usageFallbacks++;
                continue;
            }

            if (globalHeights.Count > 0)
            {
                building.HeightMeters = Median(globalHeights);
                building.HeightSource = HeightSourceGlobalMedian;
                globalFallbacks++;
                continue;
            }

            conflicts.Add(Format(
                GeneratedMapPackageDiagnosticCodes.BuildingHeightUnresolved,
                $"Building '{building.SourceId}/{building.FeatureId}' has no valid '{HeightAttribute}' attribute and no usage or global median is available; its height cannot be resolved."));
        }
    }

    private static double? ParseHeight(IReadOnlyDictionary<string, string> attributes)
    {
        if (!attributes.TryGetValue(HeightAttribute, out var text)
            || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var height)
            || !double.IsFinite(height)
            || height <= 0d)
        {
            return null;
        }

        return height;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.ToArray();
        Array.Sort(sorted);
        var middle = sorted.Length / 2;

        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2d;
    }

    private static void SeatBuilding(
        BuildingSource building,
        LocalPart[] parts,
        int latticeWidth,
        int latticeHeight,
        int chunksX,
        int chunksY,
        Dictionary<(int Cx, int Cy), Chunk> chunkLookup,
        List<string> conflicts)
    {
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;

        foreach (var part in parts)
        {
            foreach (var polygon in part.Polygons)
            {
                foreach (var point in polygon.Outer.Points)
                {
                    minX = Math.Min(minX, point.X);
                    minY = Math.Min(minY, point.Y);
                    maxX = Math.Max(maxX, point.X);
                    maxY = Math.Max(maxY, point.Y);
                }
            }
        }

        if (double.IsPositiveInfinity(minX))
        {
            building.SupportMinZ = 0d;
            building.SupportMaxZ = 0d;
            return;
        }

        var gxMin = ClampIndex(Math.Ceiling((minX - SampleSpacing) / SampleSpacing), latticeWidth - 1);
        var gxMax = ClampIndex(Math.Floor((maxX + SampleSpacing) / SampleSpacing), latticeWidth - 1);
        var gyMin = ClampIndex(Math.Ceiling((minY - SampleSpacing) / SampleSpacing), latticeHeight - 1);
        var gyMax = ClampIndex(Math.Floor((maxY + SampleSpacing) / SampleSpacing), latticeHeight - 1);
        var supportMin = double.PositiveInfinity;
        var supportMax = double.NegativeInfinity;

        for (var gy = gyMin; gy <= gyMax; gy++)
        {
            for (var gx = gxMin; gx <= gxMax; gx++)
            {
                var chunkX = Math.Min(gx / IntervalsPerChunk, chunksX - 1);
                var chunkY = Math.Min(gy / IntervalsPerChunk, chunksY - 1);
                var coreWidth = Math.Min(CorePointsPerChunkAxis, latticeWidth - (chunkX * IntervalsPerChunk));
                var coreHeight = Math.Min(CorePointsPerChunkAxis, latticeHeight - (chunkY * IntervalsPerChunk));
                var storedWidth = coreWidth + 2;
                var expectedSamples = storedWidth * (coreHeight + 2);
                var chunk = chunkLookup[(chunkX, chunkY)];
                var samples = chunk.Heights.Samples;

                if (samples.Count != expectedSamples)
                {
                    conflicts.Add(Format(
                        GeneratedMapPackageDiagnosticCodes.BuildingConflict,
                        $"Chunk ({chunkX}, {chunkY}) stores {samples.Count} terrain height samples; conservative-skirt seating expects {expectedSamples}."));
                    return;
                }

                var sample = samples[(((gy - (chunkY * IntervalsPerChunk)) + 1) * storedWidth) + ((gx - (chunkX * IntervalsPerChunk)) + 1)];

                if (!float.IsFinite(sample))
                {
                    conflicts.Add(Format(
                        GeneratedMapPackageDiagnosticCodes.BuildingConflict,
                        $"Chunk ({chunkX}, {chunkY}) terrain height sample at lattice point ({gx}, {gy}) is not finite."));
                    return;
                }

                var value = (double)sample;
                supportMin = Math.Min(supportMin, value);
                supportMax = Math.Max(supportMax, value);
            }
        }

        building.SupportMinZ = supportMin;
        building.SupportMaxZ = supportMax;
    }

    // Feature geometry is already in game units; no origin or scale applies.
    private static IReadOnlyList<LocalPolygon> LocalPolygons(GeoGeometry geometry)
    {
        var polygons = new List<LocalPolygon>();
        CollectPolygons(geometry, polygons);
        return polygons;
    }

    private static void CollectPolygons(GeoGeometry geometry, List<LocalPolygon> polygons)
    {
        switch (geometry)
        {
            case GeoPolygon polygon:
                polygons.Add(ToLocalPolygon(polygon));
                break;
            case GeoMultiGeometry multi:
                foreach (var child in multi.Geometries)
                {
                    CollectPolygons(child, polygons);
                }

                break;
        }
    }

    private static LocalPolygon ToLocalPolygon(GeoPolygon polygon) =>
        new(
            ToLocalRing(polygon.OuterRing),
            polygon.InnerRings.Select(ToLocalRing).ToArray());

    private static LocalRing ToLocalRing(GeoRing ring)
    {
        var points = new List<LocalPoint>(ring.Positions.Count);

        foreach (var position in ring.Positions)
        {
            var point = new LocalPoint(position.Coordinate.X, position.Coordinate.Y);

            if (points.Count > 0 && Same(points[^1], point))
            {
                continue;
            }

            points.Add(point);
        }

        if (points.Count > 1 && Same(points[0], points[^1]))
        {
            points.RemoveAt(points.Count - 1);
        }

        return new LocalRing(points.ToArray());
    }

    private static string? RoofFailureReason(LocalPart part)
    {
        if (part.Geometry is not GeoPolygon)
        {
            return "multi-polygon";
        }

        var polygon = part.Polygons.Single();

        if (polygon.Inners.Count > 0)
        {
            return "holes";
        }

        var unique = UniqueRingVertices(polygon.Outer.Points);

        if (unique.Length < MinimumGableUniqueVertices)
        {
            return "too-few-vertices";
        }

        var hull = StripCollinear(unique);

        if (hull.Length < 3 || !AllCrossProductsSameSign(hull))
        {
            return "concave";
        }

        if (Math.Abs(SignedArea(hull)) < MinimumGableAreaGameUnits)
        {
            return "tiny-area";
        }

        return null;
    }

    private static void AppendPartTriangles(
        Dictionary<(int ChunkX, int ChunkY), Dictionary<int, List<MeshTriangle>>> chunkTriangles,
        int chunkX,
        int chunkY,
        BuildingSource building,
        LocalPart part,
        bool gabled,
        double baseZ,
        double eaveZ)
    {
        if (!chunkTriangles.TryGetValue((chunkX, chunkY), out var buckets))
        {
            buckets = [];
            chunkTriangles.Add((chunkX, chunkY), buckets);
        }

        var wallVariant = building.MaterialVariant;
        var roofVariant = wallVariant + RoofVariantOffset;

        foreach (var polygon in part.Polygons)
        {
            AppendWallRings(Bucket(buckets, wallVariant), polygon.Outer, outerRing: true, baseZ, eaveZ);

            foreach (var inner in polygon.Inners)
            {
                AppendWallRings(Bucket(buckets, wallVariant), inner, outerRing: false, baseZ, eaveZ);
            }

            if (gabled)
            {
                AppendGableRoof(Bucket(buckets, roofVariant), Bucket(buckets, wallVariant), polygon, eaveZ);
            }
            else
            {
                AppendFlatRoof(Bucket(buckets, roofVariant), polygon, eaveZ);
            }
        }
    }

    private static List<MeshTriangle> Bucket(Dictionary<int, List<MeshTriangle>> buckets, int variant)
    {
        if (!buckets.TryGetValue(variant, out var triangles))
        {
            triangles = [];
            buckets.Add(variant, triangles);
        }

        return triangles;
    }

    private static void AppendWallRings(List<MeshTriangle> triangles, LocalRing ring, bool outerRing, double baseZ, double eaveZ)
    {
        var points = OrientRing(ring.Points, counterClockwise: outerRing);
        var count = points.Length;

        if (count < 2)
        {
            return;
        }

        for (var index = 0; index < count; index++)
        {
            var start = points[index];
            var end = points[(index + 1) % count];

            if (Same(start, end))
            {
                continue;
            }

            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var scale = 1d / Math.Sqrt((dx * dx) + (dy * dy));
            var normalX = dy * scale;
            var normalY = -dx * scale;
            var baseVertex = new BuildingVertex(start.X, start.Y, baseZ, normalX, normalY, 0d);
            var startTop = new BuildingVertex(start.X, start.Y, eaveZ, normalX, normalY, 0d);
            var endTop = new BuildingVertex(end.X, end.Y, eaveZ, normalX, normalY, 0d);
            var endBase = new BuildingVertex(end.X, end.Y, baseZ, normalX, normalY, 0d);
            triangles.Add(new MeshTriangle(baseVertex, endTop, startTop));
            triangles.Add(new MeshTriangle(baseVertex, endBase, endTop));
        }
    }

    private static void AppendFlatRoof(List<MeshTriangle> triangles, LocalPolygon polygon, double eaveZ)
    {
        var factory = GeometryFactory.Default;
        var shell = factory.CreateLinearRing(ClosedCoordinates(polygon.Outer.Points, counterClockwise: true));
        var holes = polygon.Inners
            .Select(ring => factory.CreateLinearRing(ClosedCoordinates(ring.Points, counterClockwise: false)))
            .ToArray();
        var triangulation = PolygonTriangulator.Triangulate(factory.CreatePolygon(shell, holes));

        if (triangulation is not GeometryCollection trianglesGeometry)
        {
            return;
        }

        foreach (var triangle in trianglesGeometry.Geometries.OfType<Polygon>())
        {
            var coordinates = triangle.Coordinates;

            if (coordinates.Length < 4)
            {
                continue;
            }

            AppendFlatRoofTriangle(
                triangles,
                coordinates[0].X,
                coordinates[0].Y,
                coordinates[1].X,
                coordinates[1].Y,
                coordinates[2].X,
                coordinates[2].Y,
                eaveZ);
        }
    }

    private static void AppendFlatRoofTriangle(
        List<MeshTriangle> triangles,
        double ax,
        double ay,
        double bx,
        double by,
        double cx,
        double cy,
        double z)
    {
        var crossZ = ((bx - ax) * (cy - ay)) - ((by - ay) * (cx - ax));

        if (crossZ == 0d)
        {
            return;
        }

        if (crossZ < 0d)
        {
            (bx, cx) = (cx, bx);
            (by, cy) = (cy, by);
        }

        triangles.Add(new MeshTriangle(
            new BuildingVertex(ax, ay, z, 0d, 0d, 1d),
            new BuildingVertex(bx, by, z, 0d, 0d, 1d),
            new BuildingVertex(cx, cy, z, 0d, 0d, 1d)));
    }

    private static Coordinate[] ClosedCoordinates(LocalPoint[] points, bool counterClockwise)
    {
        var ordered = OrientRing(points, counterClockwise);
        var coordinates = new Coordinate[ordered.Length + 1];

        for (var index = 0; index < ordered.Length; index++)
        {
            coordinates[index] = new Coordinate(ordered[index].X, ordered[index].Y);
        }

        coordinates[ordered.Length] = new Coordinate(ordered[0].X, ordered[0].Y);
        return coordinates;
    }

    private static void AppendGableRoof(
        List<MeshTriangle> roofTriangles,
        List<MeshTriangle> wallTriangles,
        LocalPolygon polygon,
        double eaveZ)
    {
        var hull = StripCollinear(UniqueRingVertices(polygon.Outer.Points));

        if (hull.Length < 3)
        {
            return;
        }

        var centroid = AreaCentroid(hull);
        var axis = LongerMinimumRectangleAxis(hull);
        var perpendicular = new LocalPoint(-axis.Y, axis.X);
        var projections = new double[hull.Length];
        var maxPerpendicular = 0d;

        for (var index = 0; index < hull.Length; index++)
        {
            var dx = hull[index].X - centroid.X;
            var dy = hull[index].Y - centroid.Y;
            projections[index] = (dx * axis.X) + (dy * axis.Y);
            maxPerpendicular = Math.Max(maxPerpendicular, Math.Abs((dx * perpendicular.X) + (dy * perpendicular.Y)));
        }

        var ridgeZ = eaveZ + (Math.Tan(GableSlopeRadians) * maxPerpendicular);
        var sMin = projections.Min();
        var sMax = projections.Max();
        var tolerance = 1e-9d * Math.Max(1d, sMax - sMin);

        for (var index = 0; index < hull.Length; index++)
        {
            var next = (index + 1) % hull.Length;
            var p = hull[index];
            var q = hull[next];
            var sp = projections[index];
            var sq = projections[next];

            // An edge perpendicular to the ridge collapses onto the vertical gable plane,
            // which the gable-end triangle owns; skip it here to avoid a coincident face.
            if (sp == sq)
            {
                continue;
            }

            var ridgeP = new LocalPoint(centroid.X + (axis.X * sp), centroid.Y + (axis.Y * sp));
            var ridgeQ = new LocalPoint(centroid.X + (axis.X * sq), centroid.Y + (axis.Y * sq));
            AppendUpwardTriangle(roofTriangles, At(p, eaveZ), At(q, eaveZ), At(ridgeQ, ridgeZ));
            AppendUpwardTriangle(roofTriangles, At(p, eaveZ), At(ridgeQ, ridgeZ), At(ridgeP, ridgeZ));
        }

        AppendGableEndTriangle(wallTriangles, hull, projections, sMin, tolerance, eaveZ, ridgeZ, centroid, axis, new LocalPoint(-axis.X, -axis.Y));
        AppendGableEndTriangle(wallTriangles, hull, projections, sMax, tolerance, eaveZ, ridgeZ, centroid, axis, axis);
    }

    private static void AppendGableEndTriangle(
        List<MeshTriangle> triangles,
        LocalPoint[] hull,
        double[] projections,
        double target,
        double tolerance,
        double eaveZ,
        double ridgeZ,
        LocalPoint centroid,
        LocalPoint axis,
        LocalPoint away)
    {
        var first = -1;
        var last = -1;

        for (var index = 0; index < hull.Length; index++)
        {
            if (Math.Abs(projections[index] - target) > tolerance)
            {
                continue;
            }

            if (first < 0)
            {
                first = index;
            }

            last = index;
        }

        if (first < 0 || last == first)
        {
            return;
        }

        var start = hull[first];
        var end = hull[last];
        var ridgeX = centroid.X + (axis.X * target);
        var ridgeY = centroid.Y + (axis.Y * target);
        var a = new BuildingVertex(start.X, start.Y, eaveZ, 0d, 0d, 0d);
        var b = new BuildingVertex(ridgeX, ridgeY, ridgeZ, 0d, 0d, 0d);
        var c = new BuildingVertex(end.X, end.Y, eaveZ, 0d, 0d, 0d);
        var cross = Cross3(a, b, c);
        var lengthSquared = (cross.X * cross.X) + (cross.Y * cross.Y) + (cross.Z * cross.Z);

        if (lengthSquared <= 0d)
        {
            return;
        }

        if ((cross.X * away.X) + (cross.Y * away.Y) < 0d)
        {
            (b, c) = (c, b);
            cross = (-cross.X, -cross.Y, -cross.Z);
        }

        var scale = 1d / Math.Sqrt(lengthSquared);
        triangles.Add(new MeshTriangle(
            WithNormal(a, cross.X * scale, cross.Y * scale, cross.Z * scale),
            WithNormal(b, cross.X * scale, cross.Y * scale, cross.Z * scale),
            WithNormal(c, cross.X * scale, cross.Y * scale, cross.Z * scale)));
    }

    private static void AppendUpwardTriangle(List<MeshTriangle> triangles, BuildingVertex a, BuildingVertex b, BuildingVertex c)
    {
        var cross = Cross3(a, b, c);
        var lengthSquared = (cross.X * cross.X) + (cross.Y * cross.Y) + (cross.Z * cross.Z);

        if (lengthSquared <= 0d)
        {
            return;
        }

        if (cross.Z < 0d)
        {
            (b, c) = (c, b);
            cross = (-cross.X, -cross.Y, -cross.Z);
        }

        var scale = 1d / Math.Sqrt(lengthSquared);
        triangles.Add(new MeshTriangle(
            WithNormal(a, cross.X * scale, cross.Y * scale, cross.Z * scale),
            WithNormal(b, cross.X * scale, cross.Y * scale, cross.Z * scale),
            WithNormal(c, cross.X * scale, cross.Y * scale, cross.Z * scale)));
    }

    private static BuildingVertex At(LocalPoint point, double z) => new(point.X, point.Y, z, 0d, 0d, 0d);

    private static BuildingVertex WithNormal(BuildingVertex vertex, double normalX, double normalY, double normalZ) =>
        new(vertex.X, vertex.Y, vertex.Z, normalX, normalY, normalZ);

    private static (double X, double Y, double Z) Cross3(BuildingVertex a, BuildingVertex b, BuildingVertex c)
    {
        var ux = b.X - a.X;
        var uy = b.Y - a.Y;
        var uz = b.Z - a.Z;
        var vx = c.X - a.X;
        var vy = c.Y - a.Y;
        var vz = c.Z - a.Z;

        return ((uy * vz) - (uz * vy), (uz * vx) - (ux * vz), (ux * vy) - (uy * vx));
    }

    private static LocalPoint[] UniqueRingVertices(LocalPoint[] points)
    {
        var kept = new List<LocalPoint>(points.Length);

        foreach (var point in points)
        {
            if (kept.Count > 0 && Same(kept[^1], point))
            {
                continue;
            }

            kept.Add(point);
        }

        if (kept.Count > 1 && Same(kept[0], kept[^1]))
        {
            kept.RemoveAt(kept.Count - 1);
        }

        return [.. kept];
    }

    private static LocalPoint[] StripCollinear(LocalPoint[] ring)
    {
        var current = ring;

        while (current.Length >= 3)
        {
            var kept = new List<LocalPoint>(current.Length);
            var changed = false;

            for (var index = 0; index < current.Length; index++)
            {
                var before = current[(index - 1 + current.Length) % current.Length];
                var after = current[(index + 1) % current.Length];

                if (Cross2(before, current[index], after) == 0d)
                {
                    changed = true;
                    continue;
                }

                kept.Add(current[index]);
            }

            if (kept.Count < 3)
            {
                return [];
            }

            var next = kept.ToArray();

            if (!changed)
            {
                return next;
            }

            current = next;
        }

        return current;
    }

    private static bool AllCrossProductsSameSign(LocalPoint[] ring)
    {
        var count = ring.Length;
        var sign = 0;

        for (var index = 0; index < count; index++)
        {
            var cross = Cross2(ring[(index - 1 + count) % count], ring[index], ring[(index + 1) % count]);

            if (cross > 0d)
            {
                if (sign < 0)
                {
                    return false;
                }

                sign = 1;
            }
            else if (cross < 0d)
            {
                if (sign > 0)
                {
                    return false;
                }

                sign = -1;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static LocalPoint LongerMinimumRectangleAxis(LocalPoint[] hull)
    {
        var bestArea = double.PositiveInfinity;
        var bestU = new LocalPoint(1d, 0d);
        var bestWidthU = 0d;
        var bestWidthV = 0d;

        for (var index = 0; index < hull.Length; index++)
        {
            var edgeEnd = hull[(index + 1) % hull.Length];
            var dx = edgeEnd.X - hull[index].X;
            var dy = edgeEnd.Y - hull[index].Y;
            var length = Math.Sqrt((dx * dx) + (dy * dy));

            if (length <= 0d)
            {
                continue;
            }

            var u = new LocalPoint(dx / length, dy / length);
            var v = new LocalPoint(-u.Y, u.X);
            double minU = double.PositiveInfinity;
            double maxU = double.NegativeInfinity;
            double minV = double.PositiveInfinity;
            double maxV = double.NegativeInfinity;

            foreach (var point in hull)
            {
                var pu = (point.X * u.X) + (point.Y * u.Y);
                var pv = (point.X * v.X) + (point.Y * v.Y);
                minU = Math.Min(minU, pu);
                maxU = Math.Max(maxU, pu);
                minV = Math.Min(minV, pv);
                maxV = Math.Max(maxV, pv);
            }

            var area = (maxU - minU) * (maxV - minV);

            if (area < bestArea)
            {
                bestArea = area;
                bestU = u;
                bestWidthU = maxU - minU;
                bestWidthV = maxV - minV;
            }
        }

        var axis = bestWidthU >= bestWidthV ? bestU : new LocalPoint(-bestU.Y, bestU.X);

        if (axis.X < 0d || (axis.X == 0d && axis.Y < 0d))
        {
            axis = new LocalPoint(-axis.X, -axis.Y);
        }

        return axis;
    }

    private static LocalPoint AreaCentroid(LocalPoint[] points)
    {
        var areaTwice = 0d;
        var centroidX = 0d;
        var centroidY = 0d;
        var count = points.Length;

        for (var index = 0; index < count; index++)
        {
            var p = points[index];
            var q = points[(index + 1) % count];
            var cross = (p.X * q.Y) - (q.X * p.Y);
            areaTwice += cross;
            centroidX += (p.X + q.X) * cross;
            centroidY += (p.Y + q.Y) * cross;
        }

        if (areaTwice == 0d)
        {
            var sumX = 0d;
            var sumY = 0d;

            foreach (var point in points)
            {
                sumX += point.X;
                sumY += point.Y;
            }

            return new LocalPoint(sumX / points.Length, sumY / points.Length);
        }

        return new LocalPoint(centroidX / (3d * areaTwice), centroidY / (3d * areaTwice));
    }

    private static (double X, double Y) FirstPolygonCentroid(IReadOnlyList<LocalPolygon> polygons)
    {
        if (polygons.Count == 0)
        {
            return (0d, 0d);
        }

        var points = polygons[0].Outer.Points;

        if (points.Length == 0)
        {
            return (0d, 0d);
        }

        var areaTwice = 0d;
        var centroidX = 0d;
        var centroidY = 0d;

        for (var index = 0; index < points.Length; index++)
        {
            var p = points[index];
            var q = points[(index + 1) % points.Length];
            var cross = (p.X * q.Y) - (q.X * p.Y);
            areaTwice += cross;
            centroidX += (p.X + q.X) * cross;
            centroidY += (p.Y + q.Y) * cross;
        }

        if (areaTwice == 0d)
        {
            var sumX = 0d;
            var sumY = 0d;

            foreach (var point in points)
            {
                sumX += point.X;
                sumY += point.Y;
            }

            return (sumX / points.Length, sumY / points.Length);
        }

        return (centroidX / (3d * areaTwice), centroidY / (3d * areaTwice));
    }

    private static LocalPoint[] OrientRing(LocalPoint[] points, bool counterClockwise)
    {
        var area = SignedArea(points);
        var flip = counterClockwise ? area < 0d : area > 0d;
        return flip ? points.Reverse().ToArray() : points;
    }

    private static double SignedArea(LocalPoint[] points)
    {
        var sum = 0d;
        var count = points.Length;

        for (var index = 0; index < count; index++)
        {
            var p = points[index];
            var q = points[(index + 1) % count];
            sum += (p.X * q.Y) - (q.X * p.Y);
        }

        return sum / 2d;
    }

    private static double Cross2(LocalPoint before, LocalPoint current, LocalPoint after)
    {
        var dx1 = current.X - before.X;
        var dy1 = current.Y - before.Y;
        var dx2 = after.X - current.X;
        var dy2 = after.Y - current.Y;

        return (dx1 * dy2) - (dy1 * dx2);
    }

    private static bool Same(LocalPoint left, LocalPoint right) => left.X == right.X && left.Y == right.Y;

    private static int ClampIndex(double value, int max) => (int)Math.Clamp(value, 0d, (double)max);

    private static int MaterialVariantOf(string sourceId, string featureId)
    {
        const uint OffsetBasis = 2166136261u;
        const uint Prime = 16777619u;
        var hash = OffsetBasis;

        foreach (var octet in Encoding.UTF8.GetBytes($"{sourceId}/{featureId}"))
        {
            unchecked
            {
                hash = (hash ^ octet) * Prime;
            }
        }

        return (int)(hash % 4);
    }

    private static string Format(string code, string message) => $"{code}: {message}";

    private readonly record struct LocalPoint(double X, double Y);

    private readonly record struct LocalRing(LocalPoint[] Points);

    private sealed record LocalPolygon(LocalRing Outer, IReadOnlyList<LocalRing> Inners);

    private sealed record LocalPart(string StableId, GeoGeometry Geometry, IReadOnlyList<LocalPolygon> Polygons);

    private sealed record MeshTriangle(BuildingVertex A, BuildingVertex B, BuildingVertex C);

    private sealed record BuildingPartSource(
        string StableId,
        string SourceId,
        string FeatureId,
        IReadOnlyDictionary<string, string> Attributes,
        GeoGeometry Geometry,
        string GeometryText);

    private sealed class BuildingSource(string sourceId, string featureId, IReadOnlyDictionary<string, string> attributes)
    {
        public string SourceId { get; } = sourceId;

        public string FeatureId { get; } = featureId;

        public IReadOnlyDictionary<string, string> Attributes { get; } = attributes;

        public List<BuildingPartSource> Parts { get; } = [];

        public double HeightMeters { get; set; }

        public string HeightSource { get; set; } = string.Empty;

        public double HeightGameUnits { get; set; }

        public double SupportMinZ { get; set; }

        public double SupportMaxZ { get; set; }

        public string RoofRule { get; set; } = FlatRoofRule;

        public string? RoofFallbackReason { get; set; }

        public int MaterialVariant { get; set; }
    }
}
