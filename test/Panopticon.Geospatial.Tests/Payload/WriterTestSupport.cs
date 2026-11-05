#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Payload.Storage;
using Xunit;

namespace Panopticon.Geospatial.Tests.Payload;

/// <summary>
/// Private writer-test fixture: stages one complete lattice payload through a writer
/// exactly like the importer would, reusing the accepted reader fixtures for chunk
/// content, transform, provenance, and the clean report. Optional hooks tamper with
/// chunk heights or descriptor references while keeping the payload otherwise valid,
/// so exactly one reader check can fail per test.
/// </summary>
internal static class WriterTestSupport
{
    internal static MaterialPalette Palette()
    {
        return new(ReaderTestSupport.PaletteNames);
    }

    internal static GeneratedMapPayloadWriter CreateWriter(
        string outputDirectory,
        GeoDiagnosticBag diagnostics,
        bool force = false,
        GeospatialImportLimits? limits = null)
    {
        var writer = GeneratedMapPayloadWriter.Create(outputDirectory, [], diagnostics, force, limits);

        Assert.NotNull(writer);

        return writer!;
    }

    internal sealed record WrittenPayload(GeneratedMapPayloadMetadata Metadata, string PayloadSha256Hex, GeneratedMapPayloadReport Report);

    /// <summary>Order of parameters matches the writer facade calls the importer makes.</summary>
    internal static WrittenPayload WriteFullPayload(
        GeneratedMapPayloadWriter writer,
        int chunksX = 2,
        int chunksY = 2,
        bool imagery = true,
        Func<int, int, float[], float[]>? mutateTerrainHeights = null,
        Func<string, PayloadFileReference, PayloadFileReference>? mutateReference = null)
    {
        var descriptors = WriteChunks(writer, chunksX, chunksY, imagery, mutateTerrainHeights, mutateReference);
        var metadata = BuildMetadata(descriptors, chunksX, chunksY, imagery);

        var payloadSha256Hex = writer.WriteMetadata(metadata);

        Assert.NotNull(payloadSha256Hex);

        var report = ReaderTestSupport.CleanReport(metadata, payloadSha256Hex!);

        Assert.True(writer.WriteReport(report));

        return new WrittenPayload(metadata, payloadSha256Hex!, report);
    }

    internal static List<PayloadChunkDescriptor> WriteChunks(
        GeneratedMapPayloadWriter writer,
        int chunksX,
        int chunksY,
        bool imagery = true,
        Func<int, int, float[], float[]>? mutateTerrainHeights = null,
        Func<string, PayloadFileReference, PayloadFileReference>? mutateReference = null)
    {
        var palette = Palette();
        var descriptors = new List<PayloadChunkDescriptor>();

        for (var chunkY = 0; chunkY < chunksY; chunkY++)
        {
            for (var chunkX = 0; chunkX < chunksX; chunkX++)
            {
                var heights = ReaderTestSupport.TerrainFromLattice(chunkX, chunkY);
                heights = mutateTerrainHeights?.Invoke(chunkX, chunkY, heights) ?? heights;

                var terrain = writer.WriteTerrain(new TerrainChunk(chunkX, chunkY, heights));
                var materials = writer.WriteMaterial(
                    new MaterialChunk(chunkX, chunkY, palette, ReaderTestSupport.MaterialFromLattice(chunkX, chunkY)));
                var masks = writer.WriteMask(new TerrainMaskChunk(chunkX, chunkY, ReaderTestSupport.MaskFromLattice(chunkX, chunkY)));

                Assert.NotNull(terrain);
                Assert.NotNull(materials);
                Assert.NotNull(masks);

                PayloadFileReference? imageryReference = null;

                if (imagery)
                {
                    var side = ChunkLayout.IntervalsPerChunk;
                    var png = ReaderTestSupport.EncodeImageryPng(side, ReaderTestSupport.ImageryRgba(chunkX, chunkY, side));
                    imageryReference = writer.WriteImagery(new ImageryChunk(chunkX, chunkY, side, png));

                    Assert.NotNull(imageryReference);
                }

                descriptors.Add(new PayloadChunkDescriptor(
                    chunkX,
                    chunkY,
                    Mutate("terrain", terrain!, mutateReference),
                    Mutate("materials", materials!, mutateReference),
                    Mutate("masks", masks!, mutateReference),
                    imageryReference is null ? null : Mutate("imagery", imageryReference, mutateReference)));
            }
        }

        return descriptors;
    }

    internal static GeneratedMapPayloadMetadata BuildMetadata(
        List<PayloadChunkDescriptor> descriptors,
        int chunksX,
        int chunksY,
        bool imagery = true,
        MaterialPalette? paletteOverride = null)
    {
        return new GeneratedMapPayloadMetadata(
            new PayloadGridDefinition(
                chunksX,
                chunksY,
                ReaderTestSupport.Transform(),
                ReaderTestSupport.PlayableArea(chunksX, chunksY),
                imagery ? 1 : null,
                new SlopeThresholds(45d, 15d)),
            paletteOverride ?? Palette(),
            ReaderTestSupport.Features(),
            ReaderTestSupport.ManifestSha256Hex,
            ReaderTestSupport.Provenance(),
            descriptors);
    }

    /// <summary>Snapshot of a directory tree as sorted relative paths with file bytes, for byte-exact backup comparison.</summary>
    internal static Dictionary<string, byte[]> SnapshotFiles(string root)
    {
        var snapshot = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order())
        {
            snapshot[Path.GetRelativePath(root, path)] = File.ReadAllBytes(path);
        }

        return snapshot;
    }

    private static PayloadFileReference Mutate(
        string layer,
        PayloadFileReference reference,
        Func<string, PayloadFileReference, PayloadFileReference>? mutateReference)
    {
        return mutateReference?.Invoke(layer, reference) ?? reference;
    }
}
