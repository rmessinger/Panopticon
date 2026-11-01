#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Payload.Serialization;
using Panopticon.Geospatial.Sampling;

namespace Panopticon.Geospatial.Tests.Payload;

/// <summary>
/// Builds one complete metadata and report fixture covering every geometry kind, both
/// CRS forms, shared attributes, provenance with embedded datums, imagery, and full
/// report sections. Values are chosen so source altitude and game Z never coincide.
/// </summary>
internal static class PayloadTestSupport
{
    public const int ChunksX = 2;

    public const int ChunksY = 1;

    public static string ManifestSha256Hex { get; } = new('3', 64);

    public static string PayloadSha256Hex { get; } = new('7', 64);

    /// <summary>Distinct lowercase SHA-256 text per ordinal.</summary>
    public static string Sha256(int ordinal)
    {
        return ordinal.ToString("x").PadLeft(64, '0');
    }

    public static GeospatialMapTransform Transform()
    {
        return new GeospatialMapTransform(
            [CoordinateSystemId.Wgs84, CoordinateSystemId.TransverseMercator(9d, 0.9996d, 500000d, 0d)],
            CoordinateSystemId.Utm(31, northernHemisphere: true),
            new Vector2d(500000d, 6000000d),
            metersPerGameUnit: 1d,
            verticalDatum: "EGM96",
            verticalOriginMeters: 10d,
            verticalExaggeration: 1.5d);
    }

    public static GeoPolygon PlayableArea()
    {
        return new GeoPolygon(
            Ring((0d, 0d), (1280d, 0d), (1280d, 1280d), (0d, 1280d), (0d, 0d)),
            [Ring((100d, 100d), (200d, 100d), (200d, 200d), (100d, 200d), (100d, 100d))]);
    }

    public static PayloadGridDefinition Grid(int? imageryPixelsPerSample = 2)
    {
        return new PayloadGridDefinition(ChunksX, ChunksY, Transform(), PlayableArea(), imageryPixelsPerSample, new SlopeThresholds(45d, 15d));
    }

    public static PayloadGridDefinition GridWithProjected(CoordinateSystemId projected)
    {
        return new PayloadGridDefinition(ChunksX, ChunksY, WithProjected(Transform(), projected), PlayableArea(), 2, new SlopeThresholds(45d, 15d));
    }

    public static PayloadGridDefinition GridWithSources(IReadOnlyList<CoordinateSystemId> sources)
    {
        var template = Transform();
        var transform = new GeospatialMapTransform(
            sources,
            template.ProjectedCoordinateSystem,
            template.OriginEastingNorthing,
            template.MetersPerGameUnit,
            template.VerticalDatum,
            template.VerticalOriginMeters,
            template.VerticalExaggeration);

        return new PayloadGridDefinition(ChunksX, ChunksY, transform, PlayableArea(), 2, new SlopeThresholds(45d, 15d));
    }

    public static PayloadGridDefinition GridWithPlayableArea(GeoPolygon playableArea)
    {
        return new PayloadGridDefinition(ChunksX, ChunksY, Transform(), playableArea, 2, new SlopeThresholds(45d, 15d));
    }

    public static PayloadGridDefinition GridWithTransform(string verticalDatum)
    {
        var template = Transform();
        var transform = new GeospatialMapTransform(
            template.SourceCoordinateSystems,
            template.ProjectedCoordinateSystem,
            template.OriginEastingNorthing,
            template.MetersPerGameUnit,
            verticalDatum,
            template.VerticalOriginMeters,
            template.VerticalExaggeration);

        return new PayloadGridDefinition(ChunksX, ChunksY, transform, PlayableArea(), 2, new SlopeThresholds(45d, 15d));
    }

    public static GeospatialMapTransform WithProjected(GeospatialMapTransform template, CoordinateSystemId projected)
    {
        return new GeospatialMapTransform(
            template.SourceCoordinateSystems,
            projected,
            template.OriginEastingNorthing,
            template.MetersPerGameUnit,
            template.VerticalDatum,
            template.VerticalOriginMeters,
            template.VerticalExaggeration);
    }

    /// <summary>Attribute dictionary inserted in non-ordinal key order.</summary>
    public static IReadOnlyDictionary<string, string> SharedAttributes()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["zone"] = "residential",
            ["name"] = "alpha",
            ["elevation-tag"] = "hill",
        };
    }

    public static List<GeneratedFeature> Features(IReadOnlyDictionary<string, string>? shared = null)
    {
        shared ??= SharedAttributes();

        return
        [
            Feature(GeneratedFeatureKind.Waypoint, new GeoPointGeometry(Position(100d, 200d, 120d)), shared, position: new GamePosition(10d, 20d, 30d)),
            Feature(GeneratedFeatureKind.Landmark, new GeoPointGeometry(Position(150d, 250d)), null, position: new GamePosition(15d, 25d, 6.5d)),
            Feature(GeneratedFeatureKind.ObjectCandidate, new GeoPointGeometry(Position(160d, 260d)), null, position: new GamePosition(16d, 26d, 7d), placement: new GeneratedObjectPlacement("TreeOak", 45d)),
            Feature(GeneratedFeatureKind.Road, Line((0d, 10d, 5d), (50d, 60d, null), (120d, 130d, 80d)), null),
            Feature(GeneratedFeatureKind.River, Line((10d, 0d, null), (60d, 50d, 33d)), null),
            Feature(GeneratedFeatureKind.Water, PlayableArea(), null),
            Feature(GeneratedFeatureKind.Passability, new GeoPolygon(Ring((300d, 300d), (400d, 300d), (400d, 400d), (300d, 400d), (300d, 300d)), null), null, passable: true),
            Feature(GeneratedFeatureKind.Buildability, new GeoPolygon(Ring((500d, 500d), (600d, 500d), (600d, 600d), (500d, 600d), (500d, 500d)), null), null, buildable: false),
            Feature(GeneratedFeatureKind.Annotation, new GeoMultiGeometry([new GeoPointGeometry(Position(1d, 2d, 3d)), Line((0d, 0d, null), (1d, 1d, null))]), null),
            Feature(GeneratedFeatureKind.Waypoint, new GeoPointGeometry(Position(180d, 280d, 190d)), shared, position: new GamePosition(18d, 28d, 40d), featureIndex: 1),
        ];
    }

    public static List<PayloadSourceProvenance> Provenance()
    {
        return
        [
            new PayloadSourceProvenance(
                "vectors",
                ManifestSourceKind.Kmz,
                ManifestSourceRole.Extent,
                "sources/area.kmz",
                Sha256(1),
                1024L,
                CoordinateSystemId.Wgs84,
                "unknown"),
            new PayloadSourceProvenance(
                "dem",
                ManifestSourceKind.Geotiff,
                ManifestSourceRole.Elevation,
                "sources/dem.tif",
                Sha256(2),
                4096L,
                CoordinateSystemId.Utm(31, northernHemisphere: true),
                "EGM96",
                embeddedVerticalDatum: "EGM96",
                label: "SRTM 1 arc-second",
                attribution: "Public domain",
                horizontalAccuracyMeters: 5.5d,
                verticalAccuracyMeters: 7.25d),
            new PayloadSourceProvenance(
                "classification",
                ManifestSourceKind.Geotiff,
                ManifestSourceRole.Classification,
                "sources/landcover.tif",
                Sha256(3),
                2048L,
                CoordinateSystemId.Wgs84,
                "unknown"),
        ];
    }

    public static List<PayloadChunkDescriptor> ChunkDescriptors(bool imagery = true)
    {
        var result = new List<PayloadChunkDescriptor>();

        for (var chunkY = 0; chunkY < ChunksY; chunkY++)
        {
            for (var chunkX = 0; chunkX < ChunksX; chunkX++)
            {
                var ordinal = (chunkY * ChunksX) + chunkX;
                result.Add(new PayloadChunkDescriptor(
                    chunkX,
                    chunkY,
                    new PayloadFileReference(PayloadChunkPaths.Terrain(chunkX, chunkY), PayloadChunkDescriptor.TerrainChunkBytes, Sha256(10 + ordinal)),
                    new PayloadFileReference(PayloadChunkPaths.Materials(chunkX, chunkY), PayloadChunkDescriptor.MaterialChunkBytes, Sha256(30 + ordinal)),
                    new PayloadFileReference(PayloadChunkPaths.Masks(chunkX, chunkY), PayloadChunkDescriptor.MaskChunkBytes, Sha256(50 + ordinal)),
                    imagery ? new PayloadFileReference(PayloadChunkPaths.Imagery(chunkX, chunkY), 9001 + ordinal, Sha256(70 + ordinal)) : null));
            }
        }

        return result;
    }

    public static GeneratedMapPayloadMetadata Metadata(bool imagery = true)
    {
        return new GeneratedMapPayloadMetadata(
            Grid(imagery ? 2 : null),
            new MaterialPalette(["grass", "dirt", "rock"]),
            Features(),
            ManifestSha256Hex,
            Provenance(),
            ChunkDescriptors(imagery));
    }

    public static GeneratedMapPayloadReport Report(bool imagery = true)
    {
        return new GeneratedMapPayloadReport(
            [
                new PayloadDiagnostic("GEO-GEN-001", GeoStage.Generate, GeoSeverity.Warning, "Filled 3 samples from the edge policy.", "sources/dem.tif"),
                new PayloadDiagnostic("GEO-VAL-001", GeoStage.Validate, GeoSeverity.Info, "Control points matched."),
            ],
            new PayloadTerrainGenerationSummary(SamplingSummary(10_000, 3, 12, 5, minGsd: 8d, maxGsd: 12.5d, minRatio: 1d, maxRatio: 1.5d), 2, 1.5f, 87.25f),
            MasksSummary(),
            FeaturesSummary(Features()),
            [
                new PayloadValidationCheck("control-point", "waypoint-z", PayloadValidationStatus.Passed, 30d, 30d, 0.5d, "game-units", "Matched"),
                new PayloadValidationCheck("payload", "chunk-inventory", PayloadValidationStatus.Failed, message: "Chunk file missing"),
                new PayloadValidationCheck("control-point", "imagery-r", PayloadValidationStatus.NotEvaluated),
            ],
            PayloadSha256Hex,
            imagery ? new PayloadImageryGenerationSummary(SamplingSummary(20, 0, 0, 0, minGsd: 0.5d, maxGsd: 0.5d, minRatio: 1d, maxRatio: 1d), 2) : null);
    }

    public static PayloadMaskGenerationSummary MasksSummary()
    {
        return new PayloadMaskGenerationSummary(
            45d,
            15d,
            1.5d,
            2,
            impassableCellCount: 100,
            unbuildableCellCount: 200,
            slopeImpassableCellCount: 50,
            slopeUnbuildableCellCount: 150,
            waterRuleApplicationCount: 10,
            passabilityRuleApplicationCount: 5,
            buildabilityRuleApplicationCount: 3,
            playableForcedCellCount: 40);
    }

    public static PayloadSamplingSummary SamplingSummary(
        long sampleCount,
        long failedCount,
        long noDataFills,
        long outsideFills,
        double minGsd,
        double maxGsd,
        double minRatio,
        double maxRatio)
    {
        // Sums to sampleCount exactly; small summaries stay non-negative per kernel.
        var areaWeighted = Math.Min(2L, sampleCount);
        var bilinear = failedCount;
        var nearest = Math.Min(100L, sampleCount - areaWeighted - bilinear);
        var auto = sampleCount - areaWeighted - bilinear - nearest;

        return new PayloadSamplingSummary(
            sampleCount,
            failedCount,
            noDataFills,
            outsideFills,
            KernelCounts(auto, nearest, bilinear, areaWeighted),
            new PayloadObservedRanges(
                new PayloadObservedRange(minGsd, maxGsd),
                new PayloadObservedRange(minGsd, maxGsd),
                new PayloadObservedRange(minGsd * minRatio, maxGsd * maxRatio),
                new PayloadObservedRange(minGsd * minRatio, maxGsd * maxRatio),
                new PayloadObservedRange(minRatio, maxRatio),
                new PayloadObservedRange(minRatio, maxRatio)));
    }

    public static List<PayloadKernelSampleCount> KernelCounts(long auto, long nearest, long bilinear, long areaWeighted)
    {
        var byKernel = new Dictionary<SampleKernel, long>
        {
            [SampleKernel.Auto] = auto,
            [SampleKernel.Nearest] = nearest,
            [SampleKernel.Bilinear] = bilinear,
            [SampleKernel.AreaWeighted] = areaWeighted,
        };

        var result = new List<PayloadKernelSampleCount>();

        foreach (SampleKernel kernel in Enum.GetValues<SampleKernel>())
        {
            result.Add(new PayloadKernelSampleCount(kernel, byKernel[kernel]));
        }

        return result;
    }

    /// <summary>Counts the fixture features per kind, in enum order.</summary>
    public static PayloadFeatureGenerationSummary FeaturesSummary(IReadOnlyList<GeneratedFeature> features)
    {
        var byKind = new Dictionary<GeneratedFeatureKind, long>();

        foreach (GeneratedFeatureKind kind in Enum.GetValues<GeneratedFeatureKind>())
        {
            byKind[kind] = 0;
        }

        foreach (var feature in features)
        {
            byKind[feature.Kind]++;
        }

        var counts = new List<PayloadFeatureKindCount>();

        foreach (GeneratedFeatureKind kind in Enum.GetValues<GeneratedFeatureKind>())
        {
            counts.Add(new PayloadFeatureKindCount(kind, byKind[kind]));
        }

        return new PayloadFeatureGenerationSummary(features.Count, counts);
    }

    /// <summary>
    /// Minimal metadata fixture with one annotation feature carrying a single attribute
    /// value, for string-budget and single-token read tests.
    /// </summary>
    public static GeneratedMapPayloadMetadata MetadataWithAttributeValue(string value)
    {
        return MetadataWithAttributes(new Dictionary<string, string>(StringComparer.Ordinal) { ["note"] = value });
    }

    /// <summary>Attribute fixture with an explicit key and value pair.</summary>
    public static GeneratedMapPayloadMetadata MetadataWithAttributePair(string key, string value)
    {
        return MetadataWithAttributes(new Dictionary<string, string>(StringComparer.Ordinal) { [key] = value });
    }

    /// <summary>Annotation feature with a line of <paramref name="positionCount"/> finite positions.</summary>
    public static GeneratedMapPayloadMetadata MetadataWithLine(int positionCount)
    {
        var positions = new GeoPosition[positionCount];
        for (var index = 0; index < positionCount; index++)
        {
            positions[index] = new GeoPosition(new Vector2d(index * 0.25d, index * 0.5d));
        }

        return MetadataWithAttributes(null, new GeoLineString(positions));
    }

    private static GeneratedMapPayloadMetadata MetadataWithAttributes(
        Dictionary<string, string>? attributes,
        GeoGeometry? geometry = null)
    {
        var feature = GeneratedFeature.CreateShared(
            GeneratedFeatureKind.Annotation,
            geometry ?? Line((0d, 0d, null), (1d, 1d, null)),
            "vectors",
            "note-0",
            "vectors/note-0#0",
            -1,
            attributes,
            null,
            null,
            null,
            null);

        return new GeneratedMapPayloadMetadata(
            Grid(),
            new MaterialPalette(["grass"]),
            [feature],
            ManifestSha256Hex,
            Provenance(),
            ChunkDescriptors());
    }

    public static byte[] WriteMetadataBytes(GeneratedMapPayloadMetadata metadata)
    {
        using var stream = new MemoryStream();
        PayloadJson.WriteMetadata(stream, metadata);

        return stream.ToArray();
    }

    /// <summary>
    /// Minimal structurally valid payload.json with one injectable feature item and no
    /// imagery, for value-level feature-geometry rejection tests.
    /// </summary>
    public static string MetadataJsonWithFeature(string featureJson)
    {
        return """
            {
              "kind": "panopticon-geospatial-payload",
              "schemaVersion": 1,
              "grid": {
                "chunksX": 1, "chunksY": 1,
                "layout": { "sampleSpacingGameUnits": 10, "intervalsPerChunk": 128, "pointsPerChunkAxis": 129, "apronSamplesPerSide": 1, "storedPointsPerChunkAxis": 131 },
                "transform": {
                  "sourceCoordinateSystems": [ { "epsg": 4326 } ],
                  "projectedCoordinateSystem": { "epsg": 32631 },
                  "originEastingNorthing": { "x": 500000, "y": 6000000 },
                  "metersPerGameUnit": 1, "verticalDatum": "EGM96", "verticalOriginMeters": 0, "verticalExaggeration": 1
                },
                "playableArea": {
                  "outerRing": { "positions": [ { "x": 500000, "y": 6000000 }, { "x": 500100, "y": 6000000 }, { "x": 500100, "y": 6000100 }, { "x": 500000, "y": 6000100 }, { "x": 500000, "y": 6000000 } ] },
                  "innerRings": []
                },
                "slopeThresholds": { "impassableDegrees": 45, "unbuildableDegrees": 15 }
              },
              "palette": [ "grass" ],
              "features": { "attributeTables": [], "items": [ FEATURE ] },
              "manifestSha256": "3333333333333333333333333333333333333333333333333333333333333333",
              "provenance": [ {
                "sourceId": "vectors", "kind": "kmz", "role": "extent", "relativePath": "sources/area.kmz",
                "sha256": "0000000000000000000000000000000000000000000000000000000000000001",
                "lengthBytes": 1024, "actualCoordinateSystem": { "epsg": 4326 }, "declaredVerticalDatum": "unknown"
              } ],
              "chunks": [ {
                "cx": 0, "cy": 0,
                "terrain": { "path": "terrain/0_0.bin", "lengthBytes": 68644, "sha256": "0000000000000000000000000000000000000000000000000000000000000010" },
                "materials": { "path": "materials/0_0.bin", "lengthBytes": 16641, "sha256": "0000000000000000000000000000000000000000000000000000000000000030" },
                "masks": { "path": "masks/0_0.bin", "lengthBytes": 16641, "sha256": "0000000000000000000000000000000000000000000000000000000000000050" }
              } ]
            }
            """.Replace("FEATURE", featureJson);
    }

    public static byte[] WriteReportBytes(GeneratedMapPayloadReport report)
    {
        using var stream = new MemoryStream();
        PayloadJson.WriteReport(stream, report);

        return stream.ToArray();
    }

    public static GeneratedMapPayloadMetadata? ReadMetadata(
        byte[] bytes,
        GeospatialImportLimits? limits = null,
        GeoDiagnosticBag? diagnostics = null,
        CancellationToken cancellationToken = default)
    {
        diagnostics ??= new GeoDiagnosticBag();

        using var stream = new MemoryStream(bytes, writable: false);

        return PayloadJson.TryReadMetadata(stream, diagnostics, limits ?? new GeospatialImportLimits(), cancellationToken);
    }

    public static GeneratedMapPayloadReport? ReadReport(
        byte[] bytes,
        GeospatialImportLimits? limits = null,
        GeoDiagnosticBag? diagnostics = null,
        CancellationToken cancellationToken = default)
    {
        diagnostics ??= new GeoDiagnosticBag();

        using var stream = new MemoryStream(bytes, writable: false);

        return PayloadJson.TryReadReport(stream, diagnostics, limits ?? new GeospatialImportLimits(), cancellationToken);
    }

    internal static GeoPosition Position(double x, double y, double? altitudeMeters = null)
    {
        return new GeoPosition(new Vector2d(x, y), altitudeMeters);
    }

    internal static GeoRing Ring(params (double X, double Y)[] coordinates)
    {
        var positions = new GeoPosition[coordinates.Length];

        for (var index = 0; index < coordinates.Length; index++)
        {
            positions[index] = new GeoPosition(new Vector2d(coordinates[index].X, coordinates[index].Y));
        }

        return new GeoRing(positions);
    }

    internal static GeoLineString Line(params (double X, double Y, double? Altitude)[] coordinates)
    {
        return new GeoLineString(coordinates.Select(coordinate => new GeoPosition(new Vector2d(coordinate.X, coordinate.Y), coordinate.Altitude)).ToArray());
    }

    private static GeneratedFeature Feature(
        GeneratedFeatureKind kind,
        GeoGeometry geometry,
        IReadOnlyDictionary<string, string>? attributes,
        GamePosition? position = null,
        GeneratedObjectPlacement? placement = null,
        bool? passable = null,
        bool? buildable = null,
        int featureIndex = 0)
    {
        return GeneratedFeature.CreateShared(
            kind,
            geometry,
            "vectors",
            $"feature-{featureIndex}",
            $"vectors/feature-{featureIndex}#{featureIndex}",
            featureIndex,
            attributes,
            position,
            placement,
            passable,
            buildable);
    }
}
