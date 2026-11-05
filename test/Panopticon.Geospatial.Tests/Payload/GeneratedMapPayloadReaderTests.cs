#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Payload.Reading;
using Xunit;
using static Panopticon.Geospatial.Tests.Payload.ReaderTestSupport;
using ReadingCodes = Panopticon.Geospatial.Payload.Reading.PayloadReaderDiagnosticCodes;

namespace Panopticon.Geospatial.Tests.Payload;

public class GeneratedMapPayloadReaderTests
{
    [Fact]
    public void OpenValidatesAndOpensACompletePayloadWithoutDiagnostics()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        var diagnostics = new GeoDiagnosticBag();

        using var reader = GeneratedMapPayloadReader.Open(fixture.Root, diagnostics);

        Assert.NotNull(reader);
        Assert.Empty(diagnostics.Items);
        Assert.Equal(GeneratedMapPayloadMetadata.PayloadKind, reader!.Metadata.Kind);
        Assert.Equal(2, reader.Metadata.Grid.ChunksX);
        Assert.Equal(2, reader.Metadata.Grid.ChunksY);
        Assert.Equal(1, reader.Metadata.Grid.ImageryPixelsPerSample);
        Assert.Equal(4, reader.Metadata.Chunks.Count);
        Assert.Equal(3, reader.Metadata.Palette.Count);
        Assert.Equal(Sha256Hex(fixture.PayloadJson), reader.Report.PayloadSha256Hex);
    }

    [Fact]
    public void OpenRejectsAMissingRootAndAFileRoot()
    {
        using var scratch = new ScratchRoot();
        Directory.CreateDirectory(scratch.Root);
        var missing = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(Path.Combine(scratch.Root, "absent"), missing));
        Assert.Equal(ReadingCodes.RootInvalid, SingleCode(missing));

        var filePath = Path.Combine(scratch.Root, "plain.txt");
        File.WriteAllText(filePath, "not a directory");
        var fileRoot = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(filePath, fileRoot));
        Assert.Equal(ReadingCodes.RootInvalid, SingleCode(fileRoot));
    }

    [Fact]
    public void OpenRejectsASymlinkedRoot()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        var link = Path.Combine(scratch.Root, "payload-link");
        Directory.CreateSymbolicLink(link, fixture.Root);
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(link, diagnostics));
        Assert.Equal(ReadingCodes.RootIsLink, SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsASymlinkedAncestor()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "real", "payload"));
        var link = Path.Combine(scratch.Root, "link");
        Directory.CreateSymbolicLink(link, Path.Combine(scratch.Root, "real"));
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(Path.Combine(link, "payload"), diagnostics));
        Assert.Equal(ReadingCodes.RootIsLink, SingleCode(diagnostics));
    }

    [Theory]
    [InlineData("extra.txt")]
    [InlineData("terrain/extra.bin")]
    public void OpenRejectsExtraInventoryEntries(string extraRelativePath)
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        var extra = Path.Combine(fixture.Root, extraRelativePath.Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(extra, "extra");
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.Equal(ReadingCodes.InventoryMismatch, SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsAnExtraDirectory()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        Directory.CreateDirectory(Path.Combine(fixture.Root, "terrain2"));
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.Equal(ReadingCodes.InventoryMismatch, SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsAMissingChunkFile()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        File.Delete(fixture.ChunkPath("materials", 1, 1));
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.Equal(ReadingCodes.InventoryMismatch, SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsAMissingImagerySectionWhenTheGridDeclaresImagery()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        Directory.Delete(Path.Combine(fixture.Root, "imagery"), recursive: true);
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.Equal(ReadingCodes.InventoryMismatch, SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsAnImagerySectionWhenTheGridDeclaresNone()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"), imageryPixelsPerSample: null);
        Directory.CreateDirectory(Path.Combine(fixture.Root, "imagery"));
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.Equal(ReadingCodes.InventoryMismatch, SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsASymlinkInsideTheInventory()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        File.CreateSymbolicLink(
            Path.Combine(fixture.Root, "terrain", "link.bin"),
            fixture.ChunkPath("terrain", 0, 0));
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.Equal(ReadingCodes.InventoryLink, SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsAFloodOfExtraEntriesWithBoundedInventoryState()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"), chunksX: 1, chunksY: 1);
        var limits = new GeospatialImportLimits { MaxChunkCount = 1 };

        for (var index = 0; index < 5000; index++)
        {
            File.WriteAllText(Path.Combine(fixture.Root, "terrain", $"flood-{index}.bin"), "x");
        }

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics, limits));
        Assert.Equal(ReadingCodes.InventoryMismatch, SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsAMutatedPayloadJsonAgainstTheReportHash()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        File.WriteAllText(
            Path.Combine(fixture.Root, PayloadChunkPaths.MetadataFileName),
            fixture.PayloadJsonText().Replace("\"grass\"", "\"grasz\"", StringComparison.Ordinal));
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.Equal(ReadingCodes.ReportHashMismatch, SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsStrictMetadataViolationsEvenWithAMatchingReportHash()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        var tampered = fixture.PayloadJsonText().Replace("\"kind\":\"panopticon-geospatial-payload\"", "\"kind\":\"panopticon-geospatial-payload\",\"unknownMember\":1", StringComparison.Ordinal);
        var metadataPath = Path.Combine(fixture.Root, PayloadChunkPaths.MetadataFileName);
        File.WriteAllText(metadataPath, tampered);
        WriteReport(fixture.Root, CleanReport(fixture.Metadata, Sha256Hex(File.ReadAllBytes(metadataPath))));
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.Equal("GEO-PAY-001", SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsAnUnknownSchemaVersionWithAMatchingReportHash()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        var metadataPath = Path.Combine(fixture.Root, PayloadChunkPaths.MetadataFileName);
        File.WriteAllText(metadataPath, fixture.PayloadJsonText().Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal));
        WriteReport(fixture.Root, CleanReport(fixture.Metadata, Sha256Hex(File.ReadAllBytes(metadataPath))));
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.Equal("GEO-PAY-001", SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsAReportWithErrorDiagnostics()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        WriteReport(fixture.Root, fixture.ReportWithDiagnostics(
            [new PayloadDiagnostic("GEO-GEN-001", GeoStage.Generate, GeoSeverity.Error, "Generation failed.")]));
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.Equal(ReadingCodes.ReportNotClean, SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsAReportWithAFailedCheck()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        WriteReport(fixture.Root, fixture.ReportWithChecks(
            [new PayloadValidationCheck("payload", "chunk-inventory", PayloadValidationStatus.Failed, message: "Failed.")]));
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.Equal(ReadingCodes.ReportNotClean, SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsAMissingJsonFile()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        File.Delete(Path.Combine(fixture.Root, PayloadChunkPaths.ReportFileName));
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.Equal(ReadingCodes.FileUnreadable, SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsPayloadJsonBeyondTheMetadataByteLimit()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        var diagnostics = new GeoDiagnosticBag();
        var limits = new GeospatialImportLimits { MaxPayloadMetadataBytes = 16 };

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics, limits));
        Assert.Equal("GEO-PAY-002", SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsAMutatedChunkHash()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        File.WriteAllBytes(fixture.ChunkPath("terrain", 0, 0), MutatedCopy(fixture.ChunkPath("terrain", 0, 0)));
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.Equal(ReadingCodes.FileMutated, SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsATruncatedChunkFile()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        var path = fixture.ChunkPath("materials", 1, 0);
        File.WriteAllBytes(path, File.ReadAllBytes(path)[..100]);
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.Equal(ReadingCodes.FileMutated, SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsNonFiniteTerrainHeights()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(
            Path.Combine(scratch.Root, "payload"),
            mutateEncoded: (layer, chunkX, chunkY, encoded) =>
            {
                if (layer == "terrain" && chunkX == 0 && chunkY == 1)
                {
                    Array.Fill(encoded, (byte)0xFF, 12 * sizeof(float), sizeof(float));
                }

                return encoded;
            });
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.Equal("GEO-PAY-011", SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsMaterialIndicesOutsideThePalette()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(
            Path.Combine(scratch.Root, "payload"),
            mutateEncoded: (layer, chunkX, chunkY, encoded) =>
            {
                if (layer == "materials" && chunkX == 1 && chunkY == 0)
                {
                    encoded[(5 * ChunkLayout.PointsPerChunkAxis) + 5] = PaletteEntries;
                }

                return encoded;
            });
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.Equal("GEO-PAY-012", SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsUndefinedMaskBits()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(
            Path.Combine(scratch.Root, "payload"),
            mutateEncoded: (layer, chunkX, chunkY, encoded) =>
            {
                if (layer == "masks" && chunkX == 0 && chunkY == 1)
                {
                    encoded[(7 * ChunkLayout.PointsPerChunkAxis) + 7] = 0x80;
                }

                return encoded;
            });
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.Equal("GEO-PAY-013", SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsATerrainSeamMismatchWithConsistentHashes()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(
            Path.Combine(scratch.Root, "payload"),
            mutateEncoded: (layer, chunkX, chunkY, encoded) =>
            {
                if (layer == "terrain" && chunkX == 1 && chunkY == 0)
                {
                    PatchFloat(encoded, storedX: 1, storedY: 5, value => value + 1f);
                }

                return encoded;
            });
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.Equal("GEO-PAY-019", SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsAMaterialSeamMismatchWithConsistentHashes()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(
            Path.Combine(scratch.Root, "payload"),
            mutateEncoded: (layer, chunkX, chunkY, encoded) =>
            {
                if (layer == "materials" && chunkX == 1 && chunkY == 0)
                {
                    var position = (9 * ChunkLayout.PointsPerChunkAxis) + 0;
                    encoded[position] = (byte)((encoded[position] + 1) % PaletteEntries);
                }

                return encoded;
            });
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.Equal("GEO-PAY-019", SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsAMaskSeamMismatchWithConsistentHashes()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(
            Path.Combine(scratch.Root, "payload"),
            mutateEncoded: (layer, chunkX, chunkY, encoded) =>
            {
                if (layer == "masks" && chunkX == 1 && chunkY == 0)
                {
                    encoded[(21 * ChunkLayout.PointsPerChunkAxis) + 0] ^= 1;
                }

                return encoded;
            });
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.Equal("GEO-PAY-019", SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsACorruptImageryPngWithAConsistentHash()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(
            Path.Combine(scratch.Root, "payload"),
            mutateEncoded: (layer, chunkX, chunkY, encoded) =>
            {
                if (layer == "imagery" && chunkX == 1 && chunkY == 1 && encoded.Length > 64)
                {
                    encoded[64] ^= 0xFF;
                }

                return encoded;
            });
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.StartsWith("GEO-PAY-01", SingleCode(diagnostics), StringComparison.Ordinal);
    }

    [Fact]
    public void OpenRejectsAnUndersizedImageryPng()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"), imageryPngSideOverride: 127);
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.Equal("GEO-PAY-016", SingleCode(diagnostics));
    }

    [Theory]
    [InlineData(65535L, null, null)]
    [InlineData(null, 127, null)]
    [InlineData(null, null, 3663)]
    public void OpenRejectsImageryBeyondDecodeBudgetsBeforeReadingFiles(long? maxDecodedBytes, int? maxRasterWidth, int? pixelsPerSample)
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(
            Path.Combine(scratch.Root, "payload"),
            chunksX: 1,
            chunksY: 1,
            imageryPixelsPerSample: pixelsPerSample ?? 1,
            writeImageryFiles: pixelsPerSample is null);
        var diagnostics = new GeoDiagnosticBag();
        var limits = new GeospatialImportLimits
        {
            MaxRasterDecodedBytes = maxDecodedBytes ?? new GeospatialImportLimits().MaxRasterDecodedBytes,
            MaxRasterWidth = maxRasterWidth ?? new GeospatialImportLimits().MaxRasterWidth,
        };

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics, limits));
        Assert.Equal(ReadingCodes.ImageryBudgetExceeded, SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsImageryBeyondTheEncodedByteCapBeforeReadingFiles()
    {
        using var scratch = new ScratchRoot();
        var limits = new GeospatialImportLimits { MaxRasterDecodedBytes = 4096 };
        var fixture = CreatePayload(
            Path.Combine(scratch.Root, "payload"),
            chunksX: 1,
            chunksY: 1,
            writeImageryFiles: false,
            imageryDescriptorLengthOverride: limits.MaxRasterDecodedBytes + (16L * 1024 * 1024) + 1);
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics, limits));
        Assert.Equal(ReadingCodes.ImageryBudgetExceeded, SingleCode(diagnostics));
    }

    [Fact]
    public void OpenThrowsOnACancelledTokenAndLazyReadsFollow()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => GeneratedMapPayloadReader.Open(fixture.Root, new GeoDiagnosticBag(), cancellationToken: cancelled.Token));

        using var reader = GeneratedMapPayloadReader.Open(fixture.Root, new GeoDiagnosticBag());

        Assert.NotNull(reader);
        Assert.Throws<OperationCanceledException>(() => reader!.ReadTerrain(0, 0, new GeoDiagnosticBag(), cancelled.Token));
    }

    [Fact]
    public void LazyChunkReadsReturnVerifiedDecodedChunks()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        using var reader = GeneratedMapPayloadReader.Open(fixture.Root, new GeoDiagnosticBag());

        Assert.NotNull(reader);

        var terrain = reader!.ReadTerrain(1, 1, new GeoDiagnosticBag());

        Assert.NotNull(terrain);
        Assert.Equal(terrain!.ContentSha256Hex, fixture.Metadata.Chunks[3].Terrain.Sha256Hex);
        Assert.Equal(GlobalHeight((1 * ChunkLayout.IntervalsPerChunk) + 5, (1 * ChunkLayout.IntervalsPerChunk) + 7), terrain.GetHeight(6, 8));

        var material = reader.ReadMaterial(1, 1, new GeoDiagnosticBag());

        Assert.NotNull(material);
        Assert.Equal(GlobalMaterial((1 * ChunkLayout.IntervalsPerChunk) + 5, (1 * ChunkLayout.IntervalsPerChunk) + 7), material!.GetIndex(5, 7));

        var mask = reader.ReadMask(0, 1, new GeoDiagnosticBag());

        Assert.NotNull(mask);
        Assert.Equal((TerrainMaskFlags)GlobalMask(5, (1 * ChunkLayout.IntervalsPerChunk) + 7), mask!.GetFlags(5, 7));

        var imagery = reader.ReadImagery(1, 0, new GeoDiagnosticBag());

        Assert.NotNull(imagery);
        Assert.Equal(imagery!.PngSha256Hex, fixture.Metadata.Chunks[1].Imagery!.Sha256Hex);
        Assert.Equal(ChunkLayout.IntervalsPerChunk, imagery.PixelsPerAxis);
    }

    [Fact]
    public void LazyReadsRejectCoordinatesOutsideTheGrid()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        using var reader = GeneratedMapPayloadReader.Open(fixture.Root, new GeoDiagnosticBag());

        Assert.NotNull(reader);

        foreach (var (chunkX, chunkY) in new[] { (-1, 0), (0, -1), (2, 0), (0, 2) })
        {
            var terrain = new GeoDiagnosticBag();
            var material = new GeoDiagnosticBag();
            var mask = new GeoDiagnosticBag();
            var imagery = new GeoDiagnosticBag();

            Assert.Null(reader!.ReadTerrain(chunkX, chunkY, terrain));
            Assert.Null(reader.ReadMaterial(chunkX, chunkY, material));
            Assert.Null(reader.ReadMask(chunkX, chunkY, mask));
            Assert.Null(reader.ReadImagery(chunkX, chunkY, imagery));

            foreach (var diagnostics in new[] { terrain, material, mask, imagery })
            {
                Assert.Equal(ReadingCodes.ChunkCoordinatesOutOfRange, SingleCode(diagnostics));
            }
        }
    }

    [Fact]
    public void ImageryReadsFailWithADiagnosticWhenThePayloadHasNoImagery()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"), imageryPixelsPerSample: null);
        using var reader = GeneratedMapPayloadReader.Open(fixture.Root, new GeoDiagnosticBag());

        Assert.NotNull(reader);

        var readDiagnostics = new GeoDiagnosticBag();

        Assert.Null(reader!.ReadImagery(0, 0, readDiagnostics));
        Assert.Equal(ReadingCodes.ImageryAbsent, SingleCode(readDiagnostics));

        var sampleDiagnostics = new GeoDiagnosticBag();

        Assert.False(reader.TrySampleImagery(5d, 5d, out _, sampleDiagnostics));
        Assert.Equal(ReadingCodes.ImageryAbsent, SingleCode(sampleDiagnostics));
    }

    [Fact]
    public void CacheHitsRevalidateTheHashAndCatchLaterMutation()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        using var reader = GeneratedMapPayloadReader.Open(fixture.Root, new GeoDiagnosticBag());

        Assert.NotNull(reader);
        Assert.NotNull(reader!.ReadTerrain(0, 0, new GeoDiagnosticBag()));

        var original = File.ReadAllBytes(fixture.ChunkPath("terrain", 0, 0));
        File.WriteAllBytes(fixture.ChunkPath("terrain", 0, 0), MutatedCopy(fixture.ChunkPath("terrain", 0, 0)));

        var mutated = new GeoDiagnosticBag();

        Assert.Null(reader.ReadTerrain(0, 0, mutated));
        Assert.Equal(ReadingCodes.FileMutated, SingleCode(mutated));

        File.WriteAllBytes(fixture.ChunkPath("terrain", 0, 0), original);

        Assert.NotNull(reader.ReadTerrain(0, 0, new GeoDiagnosticBag()));
    }

    [Fact]
    public void LazyReadsRejectAFileSwappedForASymlink()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        using var reader = GeneratedMapPayloadReader.Open(fixture.Root, new GeoDiagnosticBag());

        Assert.NotNull(reader);
        Assert.NotNull(reader!.ReadTerrain(0, 0, new GeoDiagnosticBag()));

        var real = fixture.ChunkPath("terrain", 0, 0);
        var moved = real + ".real";
        File.Move(real, moved);
        File.CreateSymbolicLink(real, moved);

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(reader.ReadTerrain(0, 0, diagnostics));
        Assert.Equal(ReadingCodes.InventoryLink, SingleCode(diagnostics));
    }

    [Fact]
    public void ImagerySamplingRejectsAMutatedPngAfterOpen()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        using var reader = GeneratedMapPayloadReader.Open(fixture.Root, new GeoDiagnosticBag());

        Assert.NotNull(reader);
        Assert.True(reader!.TrySampleImagery(5d, 5d, out _, new GeoDiagnosticBag()));

        File.WriteAllBytes(fixture.ImageryPath(0, 0), MutatedCopy(fixture.ImageryPath(0, 0)));

        var diagnostics = new GeoDiagnosticBag();

        Assert.False(reader.TrySampleImagery(5d, 5d, out _, diagnostics));
        Assert.Equal(ReadingCodes.FileMutated, SingleCode(diagnostics));
    }

    [Fact]
    public void ChunkCachesStayBoundedAtFourEntries()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"), chunksX: 5, chunksY: 1);
        using var reader = GeneratedMapPayloadReader.Open(fixture.Root, new GeoDiagnosticBag());

        Assert.NotNull(reader);
        Assert.True(reader!.TerrainCacheCountForTests <= 4);
        Assert.True(reader.DecodedImageryCacheCountForTests <= 4);

        for (var chunkX = 0; chunkX < 5; chunkX++)
        {
            Assert.NotNull(reader.ReadTerrain(chunkX, 0, new GeoDiagnosticBag()));
            Assert.True(reader.TrySampleImagery((chunkX * 1280d) + 640d, 640d, out _, new GeoDiagnosticBag()));
            Assert.True(reader.TerrainCacheCountForTests <= 4);
            Assert.True(reader.DecodedImageryCacheCountForTests <= 4);
        }

        var evicted = reader.ReadTerrain(0, 0, new GeoDiagnosticBag());

        Assert.NotNull(evicted);
        Assert.Equal(fixture.Metadata.Chunks[0].Terrain.Sha256Hex, evicted!.ContentSha256Hex);
    }

    [Fact]
    public void HeightSamplingMatchesTheGlobalLatticeIncludingSeamsAndCorners()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        using var reader = GeneratedMapPayloadReader.Open(fixture.Root, new GeoDiagnosticBag());

        Assert.NotNull(reader);

        var positions = new (double X, double Y)[]
        {
            (0d, 0d),
            (2560d, 2560d),
            (0d, 2560d),
            (2560d, 0d),
            (5d, 5d),
            (1280d, 7d),
            (7d, 1280d),
            (1285d, 1305d),
            (1275d, 1275d),
            (1280d, 1280d),
            (1999.5d, 555.25d),
        };

        foreach (var (gameX, gameY) in positions)
        {
            var diagnostics = new GeoDiagnosticBag();

            Assert.True(reader!.TrySampleHeight(gameX, gameY, out var gameZ, diagnostics), $"({gameX}, {gameY}) failed");
            Assert.Empty(diagnostics.Items);
            Assert.Equal(ExpectedHeight(gameX, gameY, fixture.ChunksX, fixture.ChunksY), gameZ, 5);
        }

        Assert.Equal(1000d, ExpectedHeight(0d, 0d, fixture.ChunksX, fixture.ChunksY));
    }

    [Fact]
    public void HeightSamplingRejectsPositionsOutsideTheMap()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        using var reader = GeneratedMapPayloadReader.Open(fixture.Root, new GeoDiagnosticBag());

        Assert.NotNull(reader);

        foreach (var (gameX, gameY) in new[] { (-0.1d, 0d), (0d, -0.1d), (2560.1d, 0d), (0d, 2560.1d), (double.NaN, 5d), (5d, double.PositiveInfinity) })
        {
            var diagnostics = new GeoDiagnosticBag();

            Assert.False(reader!.TrySampleHeight(gameX, gameY, out _, diagnostics));
            Assert.Equal(ReadingCodes.SampleOutsideMap, SingleCode(diagnostics));
        }
    }

    [Fact]
    public void HeightSamplingReportsAMutatedChunkDuringSampling()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        using var reader = GeneratedMapPayloadReader.Open(fixture.Root, new GeoDiagnosticBag());

        Assert.NotNull(reader);
        File.WriteAllBytes(fixture.ChunkPath("terrain", 1, 0), MutatedCopy(fixture.ChunkPath("terrain", 1, 0)));

        var diagnostics = new GeoDiagnosticBag();

        Assert.False(reader!.TrySampleHeight(2000d, 100d, out _, diagnostics));
        Assert.Equal(ReadingCodes.FileMutated, SingleCode(diagnostics));
    }

    [Fact]
    public void ImagerySamplingReturnsExactPixelCenters()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        using var reader = GeneratedMapPayloadReader.Open(fixture.Root, new GeoDiagnosticBag());

        Assert.NotNull(reader);

        // Pixel k spans [(k * 10), ((k + 1) * 10)] in game units at one pixel per sample.
        Assert.True(reader!.TrySampleImagery(5d, 5d, out var southWest, new GeoDiagnosticBag()));
        Assert.Equal(new RgbaValue(255, 0, 0, 255), southWest);

        Assert.True(reader.TrySampleImagery(2555d, 2555d, out var northEast, new GeoDiagnosticBag()));
        Assert.Equal(new RgbaValue(255, 255, 0, 255), northEast);

        Assert.True(reader.TrySampleImagery(5d, 2555d, out var northWest, new GeoDiagnosticBag()));
        Assert.Equal(new RgbaValue(0, 0, 255, 255), northWest);

        Assert.True(reader.TrySampleImagery(1285d, 5d, out var seamEast, new GeoDiagnosticBag()));
        Assert.Equal(new RgbaValue(0, 255, 0, 255), seamEast);
    }

    [Fact]
    public void ImagerySamplingInterpolatesAdjacentPixelsAcrossSeams()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        using var reader = GeneratedMapPayloadReader.Open(fixture.Root, new GeoDiagnosticBag());

        Assert.NotNull(reader);

        // Half a pixel either side of the east seam: red and green average channel-wise.
        Assert.True(reader!.TrySampleImagery(1280d, 5d, out var eastSeam, new GeoDiagnosticBag()));
        Assert.Equal(new RgbaValue(128, 128, 0, 255), eastSeam);

        // The north seam averages red and blue.
        Assert.True(reader.TrySampleImagery(5d, 1280d, out var northSeam, new GeoDiagnosticBag()));
        Assert.Equal(new RgbaValue(128, 0, 128, 255), northSeam);

        // The chunk corner averages all four chunk colors; only the blue chunk
        // contributes blue, so blue rounds to 64.
        Assert.True(reader.TrySampleImagery(1280d, 1280d, out var center, new GeoDiagnosticBag()));
        Assert.Equal(new RgbaValue(128, 128, 64, 255), center);
    }

    [Fact]
    public void ImagerySamplingClampsAtTheOuterBoundary()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        using var reader = GeneratedMapPayloadReader.Open(fixture.Root, new GeoDiagnosticBag());

        Assert.NotNull(reader);

        Assert.True(reader!.TrySampleImagery(0d, 0d, out var origin, new GeoDiagnosticBag()));
        Assert.Equal(new RgbaValue(255, 0, 0, 255), origin);

        Assert.True(reader.TrySampleImagery(2560d, 2560d, out var corner, new GeoDiagnosticBag()));
        Assert.Equal(new RgbaValue(255, 255, 0, 255), corner);

        Assert.True(reader.TrySampleImagery(2560d, 0d, out var east, new GeoDiagnosticBag()));
        Assert.Equal(new RgbaValue(0, 255, 0, 255), east);
    }

    [Fact]
    public void ImagerySamplingRejectsPositionsOutsideTheMap()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        using var reader = GeneratedMapPayloadReader.Open(fixture.Root, new GeoDiagnosticBag());

        Assert.NotNull(reader);

        foreach (var (gameX, gameY) in new[] { (-0.1d, 0d), (0d, 2560.5d), (2560.5d, 0d), (double.NaN, double.NaN) })
        {
            var diagnostics = new GeoDiagnosticBag();

            Assert.False(reader!.TrySampleImagery(gameX, gameY, out _, diagnostics));
            Assert.Equal(ReadingCodes.SampleOutsideMap, SingleCode(diagnostics));
        }
    }

    [Fact]
    public void DisposalReleasesCachesAndThrowsOnEveryFurtherUse()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        var reader = GeneratedMapPayloadReader.Open(fixture.Root, new GeoDiagnosticBag());

        Assert.NotNull(reader);
        Assert.NotNull(reader!.ReadTerrain(0, 0, new GeoDiagnosticBag()));

        reader.Dispose();
        reader.Dispose();

        Assert.Throws<ObjectDisposedException>(() => reader.Metadata);
        Assert.Throws<ObjectDisposedException>(() => reader.Report);
        Assert.Throws<ObjectDisposedException>(() => reader.ReadTerrain(0, 0, new GeoDiagnosticBag()));
        Assert.Throws<ObjectDisposedException>(() => reader.ReadMaterial(0, 0, new GeoDiagnosticBag()));
        Assert.Throws<ObjectDisposedException>(() => reader.ReadMask(0, 0, new GeoDiagnosticBag()));
        Assert.Throws<ObjectDisposedException>(() => reader.ReadImagery(0, 0, new GeoDiagnosticBag()));
        Assert.Throws<ObjectDisposedException>(() => reader.TrySampleHeight(5d, 5d, out _, new GeoDiagnosticBag()));
        Assert.Throws<ObjectDisposedException>(() => reader.TrySampleImagery(5d, 5d, out _, new GeoDiagnosticBag()));
    }

    [Fact]
    public void RepeatedReadsAreDeterministicAndConsistentWithTheDescriptors()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        using var reader = GeneratedMapPayloadReader.Open(fixture.Root, new GeoDiagnosticBag());

        Assert.NotNull(reader);

        var first = reader!.ReadTerrain(0, 0, new GeoDiagnosticBag());
        var second = reader.ReadTerrain(0, 0, new GeoDiagnosticBag());

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.ContentSha256Hex, second!.ContentSha256Hex);
        Assert.Equal(fixture.Metadata.Chunks[0].Terrain.Sha256Hex, first.ContentSha256Hex);

        var imageryFirst = reader.ReadImagery(0, 0, new GeoDiagnosticBag());
        var imagerySecond = reader.ReadImagery(0, 0, new GeoDiagnosticBag());

        Assert.NotNull(imageryFirst);
        Assert.NotNull(imagerySecond);
        Assert.Equal(imageryFirst!.PngSha256Hex, imagerySecond!.PngSha256Hex);
        Assert.Equal(fixture.Metadata.Chunks[0].Imagery!.Sha256Hex, imageryFirst.PngSha256Hex);
    }

    [Fact]
    public void OpenRejectsAnExtremeImageryPixelsPerSampleWithATypedDiagnostic()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"), chunksX: 1, chunksY: 1);
        var mutated = fixture.PayloadJsonText().Replace("\"imageryPixelsPerSample\":1", "\"imageryPixelsPerSample\":16777215", StringComparison.Ordinal);

        Assert.Contains("\"imageryPixelsPerSample\":16777215", mutated, StringComparison.Ordinal);

        var metadataPath = Path.Combine(fixture.Root, PayloadChunkPaths.MetadataFileName);
        File.WriteAllText(metadataPath, mutated);
        WriteReport(fixture.Root, CleanReport(fixture.Metadata, Sha256Hex(File.ReadAllBytes(metadataPath))));
        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics));
        Assert.Equal(ReadingCodes.ImageryBudgetExceeded, SingleCode(diagnostics));
    }

    [Fact]
    public void OpenRejectsLargeImagerySidesWithHugeCustomLimits()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(
            Path.Combine(scratch.Root, "payload"),
            chunksX: 1,
            chunksY: 1,
            imageryPixelsPerSample: 2000,
            writeImageryFiles: false);
        var diagnostics = new GeoDiagnosticBag();
        var limits = new GeospatialImportLimits
        {
            MaxRasterDecodedBytes = long.MaxValue,
            MaxRasterWidth = int.MaxValue,
            MaxRasterHeight = int.MaxValue,
        };

        Assert.Null(GeneratedMapPayloadReader.Open(fixture.Root, diagnostics, limits));
        Assert.Equal(ReadingCodes.ImageryBudgetExceeded, SingleCode(diagnostics));
    }

    [Fact]
    public void OpenAcceptsLongMaxBudgetsWithATinyPng()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        var limits = new GeospatialImportLimits
        {
            MaxRasterDecodedBytes = long.MaxValue,
            MaxRasterWidth = int.MaxValue,
            MaxRasterHeight = int.MaxValue,
        };
        var diagnostics = new GeoDiagnosticBag();

        using var reader = GeneratedMapPayloadReader.Open(fixture.Root, diagnostics, limits);

        Assert.NotNull(reader);
        Assert.Empty(diagnostics.Items);
        Assert.True(reader!.TrySampleImagery(5d, 5d, out var value, new GeoDiagnosticBag()));
        Assert.Equal(new RgbaValue(255, 0, 0, 255), value);
    }

    [Fact]
    public void LazyReadsRejectASectionDirectorySwappedForASymlink()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        using var reader = GeneratedMapPayloadReader.Open(fixture.Root, new GeoDiagnosticBag());

        Assert.NotNull(reader);
        Assert.NotNull(reader!.ReadTerrain(0, 0, new GeoDiagnosticBag()));
        Assert.True(reader.TrySampleImagery(5d, 5d, out _, new GeoDiagnosticBag()));

        var moved = Path.Combine(scratch.Root, "terrain-moved");
        Directory.Move(Path.Combine(fixture.Root, "terrain"), moved);
        Directory.CreateSymbolicLink(Path.Combine(fixture.Root, "terrain"), moved);

        var terrain = new GeoDiagnosticBag();

        Assert.Null(reader.ReadTerrain(0, 0, terrain));
        Assert.Equal(ReadingCodes.InventoryLink, SingleCode(terrain));

        var height = new GeoDiagnosticBag();

        Assert.False(reader.TrySampleHeight(5d, 5d, out _, height));
        Assert.Equal(ReadingCodes.InventoryLink, SingleCode(height));

        var imageryMoved = Path.Combine(scratch.Root, "imagery-moved");
        Directory.Move(Path.Combine(fixture.Root, "imagery"), imageryMoved);
        Directory.CreateSymbolicLink(Path.Combine(fixture.Root, "imagery"), imageryMoved);

        var imagery = new GeoDiagnosticBag();

        Assert.False(reader.TrySampleImagery(5d, 5d, out _, imagery));
        Assert.Equal(ReadingCodes.InventoryLink, SingleCode(imagery));
    }

    [Fact]
    public void LazyReadsRejectARootSwappedForASymlink()
    {
        using var scratch = new ScratchRoot();
        var fixture = CreatePayload(Path.Combine(scratch.Root, "payload"));
        using var reader = GeneratedMapPayloadReader.Open(fixture.Root, new GeoDiagnosticBag());

        Assert.NotNull(reader);
        Assert.NotNull(reader!.ReadTerrain(0, 0, new GeoDiagnosticBag()));

        var moved = Path.Combine(scratch.Root, "payload-moved");
        Directory.Move(fixture.Root, moved);
        Directory.CreateSymbolicLink(fixture.Root, moved);

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(reader.ReadTerrain(0, 0, diagnostics));
        Assert.Equal(ReadingCodes.RootIsLink, SingleCode(diagnostics));
    }

    [Fact]
    public void LazyReadsRejectAnAncestorSwappedForASymlink()
    {
        using var scratch = new ScratchRoot();
        var payloadRoot = Path.Combine(scratch.Root, "real", "payload");
        var fixture = CreatePayload(payloadRoot);
        using var reader = GeneratedMapPayloadReader.Open(payloadRoot, new GeoDiagnosticBag());

        Assert.NotNull(reader);
        Assert.NotNull(reader!.ReadTerrain(0, 0, new GeoDiagnosticBag()));

        var real = Path.Combine(scratch.Root, "real");
        var moved = Path.Combine(scratch.Root, "real-moved");
        Directory.Move(real, moved);
        Directory.CreateSymbolicLink(real, moved);

        var diagnostics = new GeoDiagnosticBag();

        Assert.Null(reader.ReadTerrain(0, 0, diagnostics));
        Assert.Equal(ReadingCodes.RootIsLink, SingleCode(diagnostics));
    }

    private static string SingleCode(GeoDiagnosticBag diagnostics)
    {
        var diagnostic = Assert.Single(diagnostics.Items);

        return diagnostic.Code;
    }

    private static byte[] MutatedCopy(string path)
    {
        var content = File.ReadAllBytes(path);
        content[^1] ^= 0xFF;

        return content;
    }

    private static void PatchFloat(byte[] encoded, int storedX, int storedY, Func<float, float> patch)
    {
        var offset = ((storedY * ChunkLayout.StoredPointsPerChunkAxis) + storedX) * sizeof(float);
        var value = BitConverter.ToSingle(encoded, offset);
        BitConverter.GetBytes(patch(value)).CopyTo(encoded, offset);
    }
}
