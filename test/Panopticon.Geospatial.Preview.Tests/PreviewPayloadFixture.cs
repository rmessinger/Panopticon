using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Sampling;
using Xunit;

namespace Panopticon.Geospatial.Preview.Tests;

/// <summary>
/// Stages complete synthetic payloads through the accepted writer facade (WI-B4b-18
/// pattern): deterministic global terrain, material, and mask lattices over a 2x2
/// (default) chunk grid, configurable features, no imagery. All values are analytic so
/// tests can derive expected heights, tiles, masks, and offsets in closed form.
/// </summary>
internal static class PreviewPayloadFixture
{
    public static readonly string[] PaletteNames = ["grass", "dirt", "rock"];

    public static readonly string[] CatalogTextureNames = ["GrassType1", "DirtMediumType1", "RockType1", "SandType1"];

    public const string DefaultTextureMapJson =
        """
        {
          "grass": "GrassType1",
          "dirt": "DirtMediumType1",
          "rock": "RockType1",
          "spare": "SandType1"
        }
        """;

    public static string ManifestSha256Hex { get; } = new('7', 64);

    /// <summary>Deterministic global terrain height over the lattice, in game units.</summary>
    public static float GlobalHeight(long latticeX, long latticeY)
    {
        return (float)(1000d + (1.25d * latticeX) + (0.5d * latticeY));
    }

    /// <summary>Deterministic global material index over the core lattice.</summary>
    public static byte GlobalMaterial(long latticeX, long latticeY)
    {
        return (byte)((latticeX + (3L * latticeY)) % PaletteNames.Length);
    }

    /// <summary>Deterministic global mask flags over the core lattice.</summary>
    public static byte GlobalMask(long latticeX, long latticeY)
    {
        if (latticeX % 3 == 0)
        {
            return (byte)TerrainMaskFlags.Impassable;
        }

        return latticeY % 4 == 0 ? (byte)TerrainMaskFlags.Unbuildable : (byte)TerrainMaskFlags.None;
    }

    public static float[] TerrainFromLattice(int chunkX, int chunkY, Func<long, long, float>? heightOverride = null)
    {
        var heights = new float[ChunkLayout.StoredPointsPerChunkAxis * ChunkLayout.StoredPointsPerChunkAxis];

        for (var storedY = 0; storedY < ChunkLayout.StoredPointsPerChunkAxis; storedY++)
        {
            for (var storedX = 0; storedX < ChunkLayout.StoredPointsPerChunkAxis; storedX++)
            {
                var latticeX = (chunkX * ChunkLayout.IntervalsPerChunk) + storedX - 1;
                var latticeY = (chunkY * ChunkLayout.IntervalsPerChunk) + storedY - 1;
                heights[(storedY * ChunkLayout.StoredPointsPerChunkAxis) + storedX] =
                    heightOverride?.Invoke(latticeX, latticeY) ?? GlobalHeight(latticeX, latticeY);
            }
        }

        return heights;
    }

    public static byte[] MaterialFromLattice(int chunkX, int chunkY)
    {
        var indices = new byte[ChunkLayout.PointsPerChunkAxis * ChunkLayout.PointsPerChunkAxis];

        for (var y = 0; y < ChunkLayout.PointsPerChunkAxis; y++)
        {
            for (var x = 0; x < ChunkLayout.PointsPerChunkAxis; x++)
            {
                indices[(y * ChunkLayout.PointsPerChunkAxis) + x] =
                    GlobalMaterial((chunkX * ChunkLayout.IntervalsPerChunk) + x, (chunkY * ChunkLayout.IntervalsPerChunk) + y);
            }
        }

        return indices;
    }

    public static byte[] MaskFromLattice(int chunkX, int chunkY)
    {
        var flags = new byte[ChunkLayout.PointsPerChunkAxis * ChunkLayout.PointsPerChunkAxis];

        for (var y = 0; y < ChunkLayout.PointsPerChunkAxis; y++)
        {
            for (var x = 0; x < ChunkLayout.PointsPerChunkAxis; x++)
            {
                flags[(y * ChunkLayout.PointsPerChunkAxis) + x] =
                    GlobalMask((chunkX * ChunkLayout.IntervalsPerChunk) + x, (chunkY * ChunkLayout.IntervalsPerChunk) + y);
            }
        }

        return flags;
    }

    /// <summary>Mapped waypoint at payload game XY with an explicit absolute game Z.</summary>
    public static GeneratedFeature Waypoint(string featureId, double gameX, double gameY, double gameZ, string? name = null)
    {
        var attributes = name is null ? null : new Dictionary<string, string> { ["name"] = name };
        return new GeneratedFeature(
            GeneratedFeatureKind.Waypoint,
            new GeoPointGeometry(new GeoPosition(new Vector2d(500000d + gameX, 6000000d + gameY))),
            "vectors",
            featureId,
            featureId + "#0",
            -1,
            attributes,
            new GamePosition(gameX, gameY, gameZ));
    }

    public static GeneratedFeature Landmark(string featureId, double gameX, double gameY)
    {
        return new GeneratedFeature(
            GeneratedFeatureKind.Landmark,
            new GeoPointGeometry(new GeoPosition(new Vector2d(500000d + gameX, 6000000d + gameY))),
            "vectors",
            featureId,
            featureId + "#0",
            -1,
            position: new GamePosition(gameX, gameY, GlobalHeight((long)(gameX / 10), (long)(gameY / 10))));
    }

    public static GeneratedFeature AnnotationLine()
    {
        return new GeneratedFeature(
            GeneratedFeatureKind.Annotation,
            new GeoLineString(
            [
                new GeoPosition(new Vector2d(500000d, 6000000d)),
                new GeoPosition(new Vector2d(500010d, 600010d)),
            ]),
            "vectors",
            "note-0",
            "vectors/note-0#0",
            -1);
    }

    /// <summary>Default feature set: one ordinary waypoint inside any whole-map or single-chunk crop.</summary>
    public static List<GeneratedFeature> DefaultFeatures()
    {
        return
        [
            Waypoint("wp-alpha", 640d, 1280d, 1100d, "alpha"),
            Landmark("landmark-0", 300d, 300d),
            AnnotationLine(),
        ];
    }

    /// <summary>Stages and commits one complete payload; the returned metadata is the verified truth.</summary>
    public static GeneratedMapPayloadMetadata CreatePayload(
        string outputDirectory,
        int chunksX = 2,
        int chunksY = 2,
        List<GeneratedFeature>? features = null,
        Func<long, long, float>? heightOverride = null)
    {
        var diagnostics = new GeoDiagnosticBag();
        var writer = GeneratedMapPayloadWriter.Create(outputDirectory, [], diagnostics);
        Assert.NotNull(writer);
        Assert.False(diagnostics.HasErrors);

        var palette = new MaterialPalette(PaletteNames);
        var descriptors = new List<PayloadChunkDescriptor>();
        for (var chunkY = 0; chunkY < chunksY; chunkY++)
        {
            for (var chunkX = 0; chunkX < chunksX; chunkX++)
            {
                var terrain = writer.WriteTerrain(new TerrainChunk(chunkX, chunkY, TerrainFromLattice(chunkX, chunkY, heightOverride)));
                var materials = writer.WriteMaterial(new MaterialChunk(chunkX, chunkY, palette, MaterialFromLattice(chunkX, chunkY)));
                var masks = writer.WriteMask(new TerrainMaskChunk(chunkX, chunkY, MaskFromLattice(chunkX, chunkY)));

                Assert.NotNull(terrain);
                Assert.NotNull(materials);
                Assert.NotNull(masks);

                descriptors.Add(new PayloadChunkDescriptor(chunkX, chunkY, terrain!, materials!, masks!, null));
            }
        }

        var metadata = new GeneratedMapPayloadMetadata(
            new PayloadGridDefinition(chunksX, chunksY, Transform(), PlayableArea(chunksX, chunksY), null, new SlopeThresholds(45d, 15d)),
            palette,
            features ?? DefaultFeatures(),
            ManifestSha256Hex,
            Provenance(),
            descriptors);

        var payloadSha256Hex = writer.WriteMetadata(metadata);
        Assert.NotNull(payloadSha256Hex);
        Assert.True(writer.WriteReport(CleanReport(metadata, payloadSha256Hex!)));
        Assert.True(writer.TryCommit());

        return metadata;
    }

    public static GeneratedMapPayloadReader OpenPayload(string outputDirectory)
    {
        var diagnostics = new GeoDiagnosticBag();
        var reader = GeneratedMapPayloadReader.Open(outputDirectory, diagnostics);
        Assert.NotNull(reader);
        Assert.False(diagnostics.HasErrors);

        return reader!;
    }

    public static PreviewTextureMap DefaultTextureMap()
    {
        var textureMap = PreviewTextureMap.FromJson(DefaultTextureMapJson, new FakeCatalog(), new GeoDiagnosticBag());
        Assert.NotNull(textureMap);

        return textureMap!;
    }

    public static GeospatialMapTransform Transform()
    {
        return new GeospatialMapTransform(
            [CoordinateSystemId.Wgs84],
            CoordinateSystemId.Utm(31, northernHemisphere: true),
            new Vector2d(500000d, 6000000d),
            metersPerGameUnit: 1d,
            verticalDatum: "EGM96",
            verticalOriginMeters: 0d,
            verticalExaggeration: 1d);
    }

    public static GeoPolygon PlayableArea(int chunksX, int chunksY)
    {
        var width = chunksX * (double)ChunkLayout.ChunkSpanGameUnits;
        var height = chunksY * (double)ChunkLayout.ChunkSpanGameUnits;

        return new GeoPolygon(Ring((500000d, 6000000d), (500000d + width, 6000000d), (500000d + width, 6000000d + height), (500000d, 6000000d + height), (500000d, 6000000d)));
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
                new string('1', 64),
                1024L,
                CoordinateSystemId.Wgs84,
                "unknown"),
            new PayloadSourceProvenance(
                "dem",
                ManifestSourceKind.Geotiff,
                ManifestSourceRole.Elevation,
                "sources/dem.tif",
                new string('2', 64),
                4096L,
                CoordinateSystemId.Utm(31, northernHemisphere: true),
                "EGM96",
                embeddedVerticalDatum: "EGM96"),
        ];
    }

    public static GeneratedMapPayloadReport CleanReport(GeneratedMapPayloadMetadata metadata, string payloadSha256Hex)
    {
        var featureList = metadata.Features;
        var counts = new Dictionary<GeneratedFeatureKind, long>();
        foreach (var kind in Enum.GetValues<GeneratedFeatureKind>())
        {
            counts[kind] = 0;
        }

        foreach (var feature in featureList)
        {
            counts[feature.Kind]++;
        }

        return new GeneratedMapPayloadReport(
            [new PayloadDiagnostic("GEO-VAL-001", GeoStage.Validate, GeoSeverity.Info, "Control points matched.")],
            new PayloadTerrainGenerationSummary(
                SamplingSummary(metadata.Grid.ChunksX * metadata.Grid.ChunksY * 64L),
                metadata.Grid.ChunksX * (long)metadata.Grid.ChunksY,
                GlobalHeight(-1, -1),
                GlobalHeight(metadata.Grid.ChunksX * ChunkLayout.IntervalsPerChunk, metadata.Grid.ChunksY * ChunkLayout.IntervalsPerChunk)),
            new PayloadMaskGenerationSummary(45d, 15d, 1d, metadata.Grid.ChunksX * (long)metadata.Grid.ChunksY, 10, 20, 5, 15, 1, 2, 3, 4),
            new PayloadFeatureGenerationSummary(
                featureList.Count,
                [.. Enum.GetValues<GeneratedFeatureKind>().Select(kind => new PayloadFeatureKindCount(kind, counts[kind]))]),
            [new PayloadValidationCheck("payload", "chunk-inventory", PayloadValidationStatus.Passed, message: "Inventory complete.")],
            payloadSha256Hex);
    }

    public static PayloadSamplingSummary SamplingSummary(long sampleCount)
    {
        var counts = new Dictionary<SampleKernel, long>();
        foreach (var kernel in Enum.GetValues<SampleKernel>())
        {
            counts[kernel] = kernel == SampleKernel.Nearest ? sampleCount : 0;
        }

        return new PayloadSamplingSummary(sampleCount, 0, 0, 0, [.. Enum.GetValues<SampleKernel>().Select(kernel => new PayloadKernelSampleCount(kernel, counts[kernel]))]);
    }

    private static GeoRing Ring(params (double X, double Y)[] positions)
    {
        var ring = positions.Select(position => new GeoPosition(new Vector2d(position.X, position.Y))).ToList();
        ring.Add(ring[0]);

        return new GeoRing(ring);
    }

    public sealed class FakeCatalog : ITerrainTextureCatalog
    {
        public IReadOnlyCollection<string> TerrainTextureNames => CatalogTextureNames;
    }

    /// <summary>Private scratch directory removed on dispose.</summary>
    internal sealed class ScratchRoot : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "panopticon-preview-tests-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
