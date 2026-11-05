#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Payload.Serialization;
using Panopticon.Geospatial.Payload.Storage;
using Panopticon.Geospatial.Sampling;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Panopticon.Geospatial.Tests.Payload;

/// <summary>
/// Builds complete on-disk payload fixtures for reader tests: a nontrivial global
/// terrain, material, and mask lattice over a 2x2 (default) chunk grid plus distinct
/// per-chunk imagery, written through the accepted payload JSON codec and chunk codec
/// into a private scratch directory. Chunk bytes can be mutated before they are written
/// so descriptors stay hash-consistent and only decode or seam checks can fail.
/// </summary>
internal static class ReaderTestSupport
{
    public const int PaletteEntries = 3;

    public static readonly string[] PaletteNames = ["grass", "dirt", "rock"];

    public static string ManifestSha256Hex { get; } = new('5', 64);

    /// <summary>Deterministic global terrain height over the lattice, in game units.</summary>
    public static float GlobalHeight(long latticeX, long latticeY)
    {
        return (float)(1000d + (1.25d * latticeX) + (0.5d * latticeY));
    }

    /// <summary>Deterministic global material index over the core lattice.</summary>
    public static byte GlobalMaterial(long latticeX, long latticeY)
    {
        return (byte)((latticeX + (3L * latticeY)) % PaletteEntries);
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

    /// <summary>Distinct base color of one chunk's imagery; four visibly different corner colors.</summary>
    public static (byte R, byte G, byte B) ChunkColor(int chunkX, int chunkY)
    {
        return (chunkX, chunkY) switch
        {
            (0, 0) => (255, 0, 0),
            (1, 0) => (0, 255, 0),
            (0, 1) => (0, 0, 255),
            (1, 1) => (255, 255, 0),
            _ => ((byte)((chunkX * 37) + (chunkY * 11)), (byte)((chunkX * 17) + (chunkY * 53)), (byte)((chunkX * 5) + (chunkY * 29))),
        };
    }

    public static float[] TerrainFromLattice(int chunkX, int chunkY)
    {
        var heights = new float[ChunkLayout.StoredPointsPerChunkAxis * ChunkLayout.StoredPointsPerChunkAxis];

        for (var storedY = 0; storedY < ChunkLayout.StoredPointsPerChunkAxis; storedY++)
        {
            for (var storedX = 0; storedX < ChunkLayout.StoredPointsPerChunkAxis; storedX++)
            {
                heights[(storedY * ChunkLayout.StoredPointsPerChunkAxis) + storedX] =
                    GlobalHeight((chunkX * ChunkLayout.IntervalsPerChunk) + storedX - 1, (chunkY * ChunkLayout.IntervalsPerChunk) + storedY - 1);
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

    /// <summary>
    /// Reference bilinear height over the global lattice, mirroring the counts-based
    /// sampler's inclusive east and north edge handling.
    /// </summary>
    public static double ExpectedHeight(double gameX, double gameY, int chunksX, int chunksY)
    {
        var maxSampleX = chunksX * ChunkLayout.IntervalsPerChunk;
        var maxSampleY = chunksY * ChunkLayout.IntervalsPerChunk;
        var latticeX = gameX / ChunkLayout.SampleSpacingGameUnits;
        var latticeY = gameY / ChunkLayout.SampleSpacingGameUnits;
        var sampleX = (int)Math.Floor(latticeX);
        var sampleY = (int)Math.Floor(latticeY);
        double weightX;
        double weightY;

        if (sampleX >= maxSampleX)
        {
            sampleX = maxSampleX - 1;
            weightX = 1d;
        }
        else
        {
            weightX = latticeX - sampleX;
        }

        if (sampleY >= maxSampleY)
        {
            sampleY = maxSampleY - 1;
            weightY = 1d;
        }
        else
        {
            weightY = latticeY - sampleY;
        }

        var southWest = (double)GlobalHeight(sampleX, sampleY);
        var southEast = (double)GlobalHeight(sampleX + 1, sampleY);
        var northWest = (double)GlobalHeight(sampleX, sampleY + 1);
        var northEast = (double)GlobalHeight(sampleX + 1, sampleY + 1);

        return (southWest * (1d - weightX) * (1d - weightY))
            + (southEast * weightX * (1d - weightY))
            + (northWest * (1d - weightX) * weightY)
            + (northEast * weightX * weightY);
    }

    public static string Sha256Hex(byte[] content)
    {
        return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    /// <summary>Encodes a north-row-first RGBA buffer with the fixed generator encoder settings the codec accepts.</summary>
    public static byte[] EncodeImageryPng(int side, byte[] rgba)
    {
        using var image = new Image<Rgba32>(side, side);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < side; y++)
            {
                var row = accessor.GetRowSpan(y);

                for (var x = 0; x < side; x++)
                {
                    var offset = ((y * side) + x) * 4;
                    row[x] = new Rgba32(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);
                }
            }
        });

        using var stream = new MemoryStream();
        var encoder = new PngEncoder
        {
            ColorType = PngColorType.RgbWithAlpha,
            BitDepth = PngBitDepth.Bit8,
            CompressionLevel = PngCompressionLevel.DefaultCompression,
            FilterMethod = PngFilterMethod.Adaptive,
            InterlaceMethod = PngInterlaceMode.None,
            ChunkFilter = PngChunkFilter.None,
        };

        encoder.Encode(image, stream);

        return stream.ToArray();
    }

    /// <summary>Rgba buffer for one chunk's imagery; PNG row 0 is the northern row of the chunk.</summary>
    public static byte[] ImageryRgba(int chunkX, int chunkY, int side)
    {
        var rgba = new byte[side * side * 4];

        for (var pngRow = 0; pngRow < side; pngRow++)
        {
            for (var column = 0; column < side; column++)
            {
                var offset = ((pngRow * side) + column) * 4;
                var (red, green, blue) = ChunkColor(chunkX, chunkY);
                rgba[offset] = red;
                rgba[offset + 1] = green;
                rgba[offset + 2] = blue;
                rgba[offset + 3] = 255;
            }
        }

        return rgba;
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

        return new GeoPolygon(
            Ring((500000d, 6000000d), (500000d + width, 6000000d), (500000d + width, 6000000d + height), (500000d, 6000000d + height), (500000d, 6000000d)));
    }

    public static List<GeneratedFeature> Features()
    {
        return
        [
            GeneratedFeature.CreateShared(
                GeneratedFeatureKind.Annotation,
                new GeoLineString(
                [
                    new GeoPosition(new Vector2d(500000d, 6000000d)),
                    new GeoPosition(new Vector2d(500000d + 10d, 6000000d + 10d)),
                ]),
                "vectors",
                "note-0",
                "vectors/note-0#0",
                -1,
                null,
                null,
                null,
                null,
                null),
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
            new PayloadSourceProvenance(
                "classification",
                ManifestSourceKind.Geotiff,
                ManifestSourceRole.Classification,
                "sources/landcover.tif",
                new string('3', 64),
                2048L,
                CoordinateSystemId.Wgs84,
                "unknown"),
        ];
    }

    public static GeneratedMapPayloadReport CleanReport(GeneratedMapPayloadMetadata metadata, string payloadSha256Hex)
    {
        return new GeneratedMapPayloadReport(
            [new PayloadDiagnostic("GEO-VAL-001", GeoStage.Validate, GeoSeverity.Info, "Control points matched.")],
            new PayloadTerrainGenerationSummary(
                SamplingSummary(metadata.Grid.ChunksX * metadata.Grid.ChunksY * 64L),
                metadata.Grid.ChunksX * (long)metadata.Grid.ChunksY,
                GlobalHeight(-1, -1),
                GlobalHeight(metadata.Grid.ChunksX * ChunkLayout.IntervalsPerChunk, metadata.Grid.ChunksY * ChunkLayout.IntervalsPerChunk)),
            MasksSummary(metadata.Grid.ChunksX * (long)metadata.Grid.ChunksY),
            FeaturesSummary(Features()),
            [new PayloadValidationCheck("payload", "chunk-inventory", PayloadValidationStatus.Passed, message: "Inventory complete.")],
            payloadSha256Hex,
            metadata.Grid.ImageryPixelsPerSample is null
                ? null
                : new PayloadImageryGenerationSummary(SamplingSummary(64), metadata.Grid.ChunksX * (long)metadata.Grid.ChunksY));
    }

    public static PayloadSamplingSummary SamplingSummary(long sampleCount)
    {
        var counts = new Dictionary<SampleKernel, long>();

        foreach (SampleKernel kernel in Enum.GetValues<SampleKernel>())
        {
            counts[kernel] = kernel == SampleKernel.Nearest ? sampleCount : 0;
        }

        var kernelCounts = Enum.GetValues<SampleKernel>().Select(kernel => new PayloadKernelSampleCount(kernel, counts[kernel])).ToList();

        return new PayloadSamplingSummary(sampleCount, 0, 0, 0, kernelCounts);
    }

    public static PayloadMaskGenerationSummary MasksSummary(long chunkCount)
    {
        return new PayloadMaskGenerationSummary(45d, 15d, 1d, chunkCount, 10, 20, 5, 15, 1, 2, 3, 4);
    }

    public static PayloadFeatureGenerationSummary FeaturesSummary(IReadOnlyList<GeneratedFeature> features)
    {
        var counts = new Dictionary<GeneratedFeatureKind, long>();

        foreach (GeneratedFeatureKind kind in Enum.GetValues<GeneratedFeatureKind>())
        {
            counts[kind] = 0;
        }

        foreach (var feature in features)
        {
            counts[feature.Kind]++;
        }

        return new PayloadFeatureGenerationSummary(
            features.Count,
            Enum.GetValues<GeneratedFeatureKind>().Select(kind => new PayloadFeatureKindCount(kind, counts[kind])).ToList());
    }

    public static byte[] WriteReport(string root, GeneratedMapPayloadReport report)
    {
        using (var stream = File.Create(Path.Combine(root, PayloadChunkPaths.ReportFileName)))
        {
            PayloadJson.WriteReport(stream, report);
        }

        return File.ReadAllBytes(Path.Combine(root, PayloadChunkPaths.ReportFileName));
    }

    /// <summary>Mutates encoded chunk bytes before they are written; descriptors follow the mutated bytes.</summary>
    internal delegate byte[] ChunkMutator(string layer, int chunkX, int chunkY, byte[] encoded);

    /// <summary>Writes one complete payload fixture and returns its verified metadata and payload.json bytes.</summary>
    internal static PayloadFixture CreatePayload(
        string root,
        int chunksX = 2,
        int chunksY = 2,
        int? imageryPixelsPerSample = 1,
        int? imageryPngSideOverride = null,
        bool writeImageryFiles = true,
        long? imageryDescriptorLengthOverride = null,
        ChunkMutator? mutateEncoded = null)
    {
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "terrain"));
        Directory.CreateDirectory(Path.Combine(root, "materials"));
        Directory.CreateDirectory(Path.Combine(root, "masks"));

        var palette = new MaterialPalette(PaletteNames);
        var hasImagery = imageryPixelsPerSample is not null;
        var imagerySide = imageryPngSideOverride ?? (hasImagery && writeImageryFiles ? checked(ChunkLayout.IntervalsPerChunk * imageryPixelsPerSample!.Value) : 0);

        if (hasImagery && writeImageryFiles)
        {
            Directory.CreateDirectory(Path.Combine(root, "imagery"));
        }

        var descriptors = new List<PayloadChunkDescriptor>();

        for (var chunkY = 0; chunkY < chunksY; chunkY++)
        {
            for (var chunkX = 0; chunkX < chunksX; chunkX++)
            {
                var terrain = WriteChunkFile(root, "terrain", chunkX, chunkY, PayloadChunkCodec.EncodeTerrain(new TerrainChunk(chunkX, chunkY, TerrainFromLattice(chunkX, chunkY))), mutateEncoded);
                var materials = WriteChunkFile(root, "materials", chunkX, chunkY, PayloadChunkCodec.EncodeMaterial(new MaterialChunk(chunkX, chunkY, palette, MaterialFromLattice(chunkX, chunkY))), mutateEncoded);
                var masks = WriteChunkFile(root, "masks", chunkX, chunkY, PayloadChunkCodec.EncodeMask(new TerrainMaskChunk(chunkX, chunkY, MaskFromLattice(chunkX, chunkY))), mutateEncoded);
                PayloadFileReference? imagery = null;

                if (hasImagery)
                {
                    if (writeImageryFiles)
                    {
                        var png = EncodeImageryPng(imagerySide, ImageryRgba(chunkX, chunkY, imagerySide));
                        png = mutateEncoded?.Invoke("imagery", chunkX, chunkY, png) ?? png;

                        var path = Path.Combine(root, "imagery", $"{chunkX}_{chunkY}.png");
                        File.WriteAllBytes(path, png);
                        imagery = new PayloadFileReference(PayloadChunkPaths.Imagery(chunkX, chunkY), new FileInfo(path).Length, Sha256Hex(png));
                    }
                    else
                    {
                        // Descriptor-only fixtures stop before the budget precheck, so a
                        // token buffer stands in for the unallocatable encoded PNG.
                        var token = new byte[64];
                        imagery = new PayloadFileReference(
                            PayloadChunkPaths.Imagery(chunkX, chunkY),
                            imageryDescriptorLengthOverride ?? token.Length,
                            Sha256Hex(token));
                    }
                }

                descriptors.Add(new PayloadChunkDescriptor(
                    chunkX,
                    chunkY,
                    terrain,
                    materials,
                    masks,
                    imagery));
            }
        }

        var metadata = new GeneratedMapPayloadMetadata(
            new PayloadGridDefinition(chunksX, chunksY, Transform(), PlayableArea(chunksX, chunksY), imageryPixelsPerSample, new SlopeThresholds(45d, 15d)),
            palette,
            Features(),
            ManifestSha256Hex,
            Provenance(),
            descriptors);

        using (var stream = File.Create(Path.Combine(root, PayloadChunkPaths.MetadataFileName)))
        {
            PayloadJson.WriteMetadata(stream, metadata);
        }

        var payloadJson = File.ReadAllBytes(Path.Combine(root, PayloadChunkPaths.MetadataFileName));
        var report = CleanReport(metadata, Sha256Hex(payloadJson));
        WriteReport(root, report);

        return new PayloadFixture(root, chunksX, chunksY, imageryPixelsPerSample, metadata, payloadJson);
    }

    private static PayloadFileReference WriteChunkFile(string root, string section, int chunkX, int chunkY, byte[] encoded, ChunkMutator? mutateEncoded)
    {
        encoded = mutateEncoded?.Invoke(section, chunkX, chunkY, encoded) ?? encoded;
        var path = Path.Combine(root, section, $"{chunkX}_{chunkY}.bin");
        File.WriteAllBytes(path, encoded);

        return section switch
        {
            "terrain" => new PayloadFileReference(PayloadChunkPaths.Terrain(chunkX, chunkY), encoded.Length, Sha256Hex(encoded)),
            "materials" => new PayloadFileReference(PayloadChunkPaths.Materials(chunkX, chunkY), encoded.Length, Sha256Hex(encoded)),
            _ => new PayloadFileReference(PayloadChunkPaths.Masks(chunkX, chunkY), encoded.Length, Sha256Hex(encoded)),
        };
    }

    private static GeoRing Ring(params (double X, double Y)[] positions)
    {
        var ring = positions.Select(position => new GeoPosition(new Vector2d(position.X, position.Y))).ToList();
        ring.Add(ring[0]);

        return new GeoRing(ring);
    }

    /// <summary>One written payload fixture inside a disposable scratch root.</summary>
    internal sealed class PayloadFixture
    {
        internal PayloadFixture(string root, int chunksX, int chunksY, int? imageryPixelsPerSample, GeneratedMapPayloadMetadata metadata, byte[] payloadJson)
        {
            Root = root;
            ChunksX = chunksX;
            ChunksY = chunksY;
            ImageryPixelsPerSample = imageryPixelsPerSample;
            Metadata = metadata;
            PayloadJson = payloadJson;
        }

        internal string Root { get; }

        internal int ChunksX { get; }

        internal int ChunksY { get; }

        internal int? ImageryPixelsPerSample { get; }

        internal GeneratedMapPayloadMetadata Metadata { get; }

        internal byte[] PayloadJson { get; }

        internal string ChunkPath(string section, int chunkX, int chunkY)
        {
            return Path.Combine(Root, section, $"{chunkX}_{chunkY}.bin");
        }

        internal string ImageryPath(int chunkX, int chunkY)
        {
            return Path.Combine(Root, "imagery", $"{chunkX}_{chunkY}.png");
        }

        internal string PayloadJsonText()
        {
            return System.Text.Encoding.UTF8.GetString(PayloadJson);
        }

        /// <summary>The fixture's clean report with different serialized diagnostics.</summary>
        internal GeneratedMapPayloadReport ReportWithDiagnostics(IReadOnlyList<PayloadDiagnostic> diagnostics)
        {
            var template = CleanReport(Metadata, Sha256Hex(PayloadJson));

            return new GeneratedMapPayloadReport(
                diagnostics,
                template.Terrain,
                template.Masks,
                template.Features,
                template.ValidationChecks,
                template.PayloadSha256Hex,
                template.Imagery);
        }

        /// <summary>The fixture's clean report with different validation checks.</summary>
        internal GeneratedMapPayloadReport ReportWithChecks(IReadOnlyList<PayloadValidationCheck> checks)
        {
            var template = CleanReport(Metadata, Sha256Hex(PayloadJson));

            return new GeneratedMapPayloadReport(
                template.Diagnostics,
                template.Terrain,
                template.Masks,
                template.Features,
                checks,
                template.PayloadSha256Hex,
                template.Imagery);
        }

        internal double MapWidth => ChunksX * (double)ChunkLayout.ChunkSpanGameUnits;

        internal double MapHeight => ChunksY * (double)ChunkLayout.ChunkSpanGameUnits;
    }

    /// <summary>Private scratch directory removed on dispose, links removed as links.</summary>
    internal sealed class ScratchRoot : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "panopticon-payload-reader-tests-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            BclPayloadDirectoryIo.Instance.DeleteDirectory(Root);
        }
    }
}
