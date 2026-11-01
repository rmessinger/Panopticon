#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Payload.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Panopticon.Geospatial.Tests.Payload;

/// <summary>
/// Private fixtures for PayloadStagedFilesTests: deterministic chunk payloads, a real
/// transaction-backed staging fixture, and a report builder bound to an injectable
/// payload hash. Metadata fixtures come from the accepted PayloadTestSupport.
/// </summary>
internal static class StagedFilesTestSupport
{
    internal static MaterialPalette Palette { get; } = new(["grass", "dirt", "rock"]);

    internal static string Sha256Hex(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    internal static TerrainChunk TerrainChunkAt(int chunkX, int chunkY)
    {
        var axis = ChunkLayout.StoredPointsPerChunkAxis;
        var heights = new float[axis * axis];

        for (var y = 0; y < axis; y++)
        {
            for (var x = 0; x < axis; x++)
            {
                heights[(y * axis) + x] = (x + (2 * y) + (7 * chunkX) + (13 * chunkY)) * 0.25f;
            }
        }

        return new TerrainChunk(chunkX, chunkY, heights);
    }

    internal static MaterialChunk MaterialChunkAt(int chunkX, int chunkY)
    {
        var axis = ChunkLayout.PointsPerChunkAxis;
        var indices = new byte[axis * axis];

        for (var y = 0; y < axis; y++)
        {
            for (var x = 0; x < axis; x++)
            {
                indices[(y * axis) + x] = (byte)((x + (3 * y) + chunkX + chunkY) % Palette.Count);
            }
        }

        return new MaterialChunk(chunkX, chunkY, Palette, indices);
    }

    internal static TerrainMaskChunk MaskChunkAt(int chunkX, int chunkY)
    {
        var axis = ChunkLayout.PointsPerChunkAxis;
        var flags = new byte[axis * axis];

        for (var y = 0; y < axis; y++)
        {
            for (var x = 0; x < axis; x++)
            {
                flags[(y * axis) + x] = (byte)((x + y + chunkX + chunkY) % 4);
            }
        }

        return new TerrainMaskChunk(chunkX, chunkY, flags);
    }

    internal static ImageryChunk ImageryChunkAt(int chunkX, int chunkY, int pixelsPerAxis = 16)
    {
        return new ImageryChunk(chunkX, chunkY, pixelsPerAxis, EncodePng(pixelsPerAxis, GradientRgba(pixelsPerAxis)));
    }

    /// <summary>
    /// Metadata whose single annotation feature shares the caller's attribute dictionary
    /// reference through the internal CreateShared path, so a lazy test dictionary stays
    /// untouched until the JSON writer enumerates it.
    /// </summary>
    internal static GeneratedMapPayloadMetadata MetadataWithSharedAttributes(IReadOnlyDictionary<string, string> attributes)
    {
        var feature = GeneratedFeature.CreateShared(
            GeneratedFeatureKind.Annotation,
            PayloadTestSupport.Line((0d, 0d, null), (1d, 1d, null)),
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
            PayloadTestSupport.Grid(),
            new MaterialPalette(["grass"]),
            [feature],
            PayloadTestSupport.ManifestSha256Hex,
            PayloadTestSupport.Provenance(),
            PayloadTestSupport.ChunkDescriptors());
    }

    /// <summary>A report bound to the given payload hash; optional checks replace the default single check.</summary>
    internal static GeneratedMapPayloadReport ReportWithHash(string payloadSha256Hex, IReadOnlyList<PayloadValidationCheck>? checks = null)
    {
        return new GeneratedMapPayloadReport(
            [new PayloadDiagnostic("GEO-VAL-001", GeoStage.Validate, GeoSeverity.Info, "Control points matched.")],
            new PayloadTerrainGenerationSummary(
                PayloadTestSupport.SamplingSummary(10_000, 3, 12, 5, minGsd: 8d, maxGsd: 12.5d, minRatio: 1d, maxRatio: 1.5d),
                2,
                1.5f,
                87.25f),
            PayloadTestSupport.MasksSummary(),
            PayloadTestSupport.FeaturesSummary(PayloadTestSupport.Features()),
            checks ?? [new PayloadValidationCheck("payload", "chunk-inventory", PayloadValidationStatus.Passed, message: "Every staged file is present")],
            payloadSha256Hex,
            new PayloadImageryGenerationSummary(
                PayloadTestSupport.SamplingSummary(20, 0, 0, 0, minGsd: 0.5d, maxGsd: 0.5d, minRatio: 1d, maxRatio: 1d),
                2));
    }

    /// <summary>Enough checks to push a report serialization far past a metadata-sized byte bound.</summary>
    internal static IReadOnlyList<PayloadValidationCheck> ManyChecks(int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => new PayloadValidationCheck(
                "synthetic",
                $"check-{index}",
                PayloadValidationStatus.Passed,
                expected: index,
                actual: index,
                tolerance: 0d,
                message: "Filler check for the report byte bound."))
            .ToArray();
    }

    /// <summary>
    /// Real PayloadDirectoryTransaction staging fixture: the stage directory is the
    /// transaction's own unique sibling of the output directory.
    /// </summary>
    internal sealed class StageFixture : IDisposable
    {
        private readonly string _root;
        private readonly PayloadDirectoryTransaction _transaction;

        internal GeoDiagnosticBag Bag { get; }

        internal string Stage => _transaction.StageDirectory;

        private StageFixture(string root, PayloadDirectoryTransaction transaction, GeoDiagnosticBag bag)
        {
            _root = root;
            _transaction = transaction;
            Bag = bag;
        }

        internal static StageFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "panopticon-staged-tests-" + Guid.NewGuid().ToString("N"));
            var bag = new GeoDiagnosticBag();
            var transaction = PayloadDirectoryTransaction.Create(
                Path.Combine(root, "payload-out"), force: true, [], bag, CancellationToken.None);

            Assert.NotNull(transaction);

            return new StageFixture(root, transaction!, bag);
        }

        internal PayloadStagedFiles CreateStagedFiles(GeospatialImportLimits? limits = null)
        {
            return new PayloadStagedFiles(Stage, limits ?? new GeospatialImportLimits(), Bag);
        }

        internal string StagePath(string relativePath)
        {
            return Path.Combine(Stage, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        public void Dispose()
        {
            _transaction.Dispose();
            BclPayloadDirectoryIo.Instance.DeleteDirectory(_root);
        }
    }

    private static byte[] GradientRgba(int pixels)
    {
        var rgba = new byte[pixels * pixels * 4];

        for (var y = 0; y < pixels; y++)
        {
            for (var x = 0; x < pixels; x++)
            {
                var offset = ((y * pixels) + x) * 4;
                rgba[offset] = (byte)(x * (255 / Math.Max(1, pixels - 1)));
                rgba[offset + 1] = (byte)(y * (255 / Math.Max(1, pixels - 1)));
                rgba[offset + 2] = (byte)((x + y) % 256);
                rgba[offset + 3] = 255;
            }
        }

        return rgba;
    }

    private static byte[] EncodePng(int pixels, byte[] rgba)
    {
        using var image = new Image<Rgba32>(pixels, pixels);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < pixels; y++)
            {
                var row = accessor.GetRowSpan(y);

                for (var x = 0; x < pixels; x++)
                {
                    var offset = ((y * pixels) + x) * 4;
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

        image.Save(stream, encoder);

        return stream.ToArray();
    }
}
