#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Payload.Storage;
using Panopticon.Geospatial.Payload.Writing;
using Xunit;

namespace Panopticon.Geospatial.Tests.Payload;

public class GeneratedMapPayloadWriterTests
{
    [Fact]
    public void Commit_PublishesAValid1x1PayloadThatRoundTripsThroughTheReader()
    {
        using var root = new ReaderTestSupport.ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        var bag = new GeoDiagnosticBag();

        using var writer = WriterTestSupport.CreateWriter(output, bag);
        var written = WriterTestSupport.WriteFullPayload(writer, chunksX: 1, chunksY: 1);

        Assert.True(writer.TryCommit());
        Assert.Equal(GeneratedMapPayloadWriterStatus.Committed, writer.Status);
        Assert.False(bag.HasErrors);
        Assert.Null(writer.RetainedBackupPath);
        Assert.Null(writer.RecoveryStagePath);

        var payloadJson = File.ReadAllBytes(Path.Combine(output, "payload.json"));
        Assert.Equal(ReaderTestSupport.Sha256Hex(payloadJson), written.PayloadSha256Hex);

        var readBag = new GeoDiagnosticBag();
        var reader = GeneratedMapPayloadReader.Open(output, readBag);

        Assert.NotNull(reader);
        Assert.False(readBag.HasErrors);

        using (reader!)
        {
            Assert.Equal(1, reader!.Metadata.Grid.ChunksX);
            Assert.Equal(1, reader.Metadata.Grid.ChunksY);
            Assert.Equal(written.PayloadSha256Hex, reader.Report.PayloadSha256Hex);
            Assert.Equal(
                ReaderTestSupport.TerrainFromLattice(0, 0),
                reader.ReadTerrain(0, 0, readBag)!.Heights.ToArray());
            Assert.Equal(
                ReaderTestSupport.MaterialFromLattice(0, 0),
                reader.ReadMaterial(0, 0, readBag)!.Indices.ToArray());
            Assert.Equal(
                ReaderTestSupport.MaskFromLattice(0, 0),
                reader.ReadMask(0, 0, readBag)!.Flags.ToArray());
            Assert.NotNull(reader.ReadImagery(0, 0, readBag));
        }
    }

    [Fact]
    public void Commit_PublishesAValid2x2PayloadWithSeamsAndImageryThatRoundTrips()
    {
        using var root = new ReaderTestSupport.ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        var bag = new GeoDiagnosticBag();

        using var writer = WriterTestSupport.CreateWriter(output, bag);
        WriterTestSupport.WriteFullPayload(writer, chunksX: 2, chunksY: 2);

        Assert.True(writer.TryCommit());
        Assert.False(bag.HasErrors);

        var readBag = new GeoDiagnosticBag();
        var reader = GeneratedMapPayloadReader.Open(output, readBag);

        Assert.NotNull(reader);
        Assert.False(readBag.HasErrors);

        using (reader!)
        {
            for (var chunkY = 0; chunkY < 2; chunkY++)
            {
                for (var chunkX = 0; chunkX < 2; chunkX++)
                {
                    Assert.NotNull(reader!.ReadTerrain(chunkX, chunkY, readBag));
                    Assert.NotNull(reader.ReadMaterial(chunkX, chunkY, readBag));
                    Assert.NotNull(reader.ReadMask(chunkX, chunkY, readBag));
                    Assert.NotNull(reader.ReadImagery(chunkX, chunkY, readBag));
                }
            }

            Assert.True(reader!.TrySampleHeight(640d, 961d, out var height, readBag));
            Assert.Equal(
                ReaderTestSupport.ExpectedHeight(640d, 961d, chunksX: 2, chunksY: 2),
                height,
                precision: 5);

            Assert.True(reader.TrySampleImagery(300d, 700d, out var rgba, readBag));
            Assert.Equal(255, rgba.R);
            Assert.Equal(0, rgba.G);
            Assert.Equal(0, rgba.B);
        }
    }

    [Fact]
    public void CreateWithoutForce_FailsUnchangedOnExistingOutput()
    {
        using var root = new ReaderTestSupport.ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        Directory.CreateDirectory(Path.Combine(output, "sub"));
        File.WriteAllText(Path.Combine(output, "sub", "keep.txt"), "operator");

        var bag = new GeoDiagnosticBag();

        Assert.Null(GeneratedMapPayloadWriter.Create(output, [], bag, force: false));

        Assert.True(bag.HasErrors);
        Assert.Equal(PayloadStorageDiagnostics.OutputDirectoryExists, Assert.Single(bag.Items).Code);
        Assert.Equal("operator", File.ReadAllText(Path.Combine(output, "sub", "keep.txt")));
    }

    [Fact]
    public void ForceCommit_RetainsABackupOfThePreviousOutputByteExact()
    {
        using var root = new ReaderTestSupport.ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        Directory.CreateDirectory(Path.Combine(output, "sub"));
        File.WriteAllText(Path.Combine(output, "old-a.bin"), "old-a");
        File.WriteAllText(Path.Combine(output, "sub", "old-b.bin"), "old-b");
        var before = WriterTestSupport.SnapshotFiles(output);

        var bag = new GeoDiagnosticBag();

        using (var writer = WriterTestSupport.CreateWriter(output, bag, force: true))
        {
            WriterTestSupport.WriteFullPayload(writer, chunksX: 1, chunksY: 1);

            Assert.True(writer.TryCommit());
            Assert.NotNull(writer.RetainedBackupPath);
            Assert.Null(writer.RecoveryStagePath);
        }

        Assert.False(bag.HasErrors);
        Assert.True(File.Exists(Path.Combine(output, "payload.json")));
        Assert.Equal(before, WriterTestSupport.SnapshotFiles(RetainedBackup(root, output)));
    }

    private static string RetainedBackup(ReaderTestSupport.ScratchRoot root, string output)
    {
        // Exactly one retained backup sibling of the output remains after the commit.
        var backup = Directory.GetDirectories(root.Root, "out.backup-*").Single();

        Assert.NotEqual(output, backup);

        return backup;
    }

    [Fact]
    public void TamperedTerrainHash_BlocksCommitBeforeThePublishedPathExists()
    {
        using var root = new ReaderTestSupport.ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        var bag = new GeoDiagnosticBag();

        using (var writer = WriterTestSupport.CreateWriter(output, bag))
        {
            WriterTestSupport.WriteFullPayload(
                writer,
                chunksX: 1,
                chunksY: 1,
                mutateReference: (layer, reference) => layer == "terrain"
                    ? new PayloadFileReference(reference.Path, reference.LengthBytes, ReaderTestSupport.Sha256Hex([1, 2, 3]))
                    : reference);

            Assert.False(writer.TryCommit());
            Assert.Equal(GeneratedMapPayloadWriterStatus.Failed, writer.Status);
        }

        Assert.True(bag.HasErrors);
        Assert.False(Directory.Exists(output));
        AssertStageCleaned(root, output);
    }

    [Fact]
    public void SeamMismatch_BlocksCommitBeforeThePublishedPathExists()
    {
        using var root = new ReaderTestSupport.ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        var bag = new GeoDiagnosticBag();

        using (var writer = WriterTestSupport.CreateWriter(output, bag))
        {
            WriterTestSupport.WriteFullPayload(
                writer,
                chunksX: 2,
                chunksY: 1,
                imagery: false,
                mutateTerrainHeights: (chunkX, chunkY, heights) =>
                {
                    if (chunkX == 1)
                    {
                        for (var index = 0; index < heights.Length; index++)
                        {
                            heights[index] += 1f;
                        }
                    }

                    return heights;
                });

            Assert.False(writer.TryCommit());
        }

        Assert.True(bag.HasErrors);
        Assert.False(Directory.Exists(output));
        AssertStageCleaned(root, output);
    }

    [Fact]
    public void TamperedMaskReference_BlocksCommitBeforeThePublishedPathExists()
    {
        using var root = new ReaderTestSupport.ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        var bag = new GeoDiagnosticBag();

        using (var writer = WriterTestSupport.CreateWriter(output, bag))
        {
            WriterTestSupport.WriteFullPayload(
                writer,
                chunksX: 1,
                chunksY: 1,
                mutateReference: (layer, reference) => layer == "masks"
                    ? new PayloadFileReference(reference.Path, reference.LengthBytes, ReaderTestSupport.Sha256Hex(new byte[reference.LengthBytes]))
                    : reference);

            Assert.False(writer.TryCommit());
        }

        Assert.True(bag.HasErrors);
        Assert.False(Directory.Exists(output));
        AssertStageCleaned(root, output);
    }

    [Fact]
    public void ExtraStageEntry_BlocksCommitBeforeThePublishedPathExists()
    {
        using var root = new ReaderTestSupport.ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        var bag = new GeoDiagnosticBag();

        using (var writer = WriterTestSupport.CreateWriter(output, bag))
        {
            WriterTestSupport.WriteFullPayload(writer, chunksX: 1, chunksY: 1);
            File.WriteAllText(Path.Combine(writer.StageDirectory, "terrain", "9_9.bin"), "extra");

            Assert.False(writer.TryCommit());
        }

        Assert.True(bag.HasErrors);
        Assert.False(Directory.Exists(output));
        AssertStageCleaned(root, output);
    }

    [Fact]
    public void FailedValidationCheck_BlocksCommitBeforeThePublishedPathExists()
    {
        using var root = new ReaderTestSupport.ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        var bag = new GeoDiagnosticBag();

        using (var writer = WriterTestSupport.CreateWriter(output, bag))
        {
            var written = WriterTestSupport.WriteFullPayload(writer, chunksX: 1, chunksY: 1);
            var template = written.Report;
            var failed = new GeneratedMapPayloadReport(
                template.Diagnostics,
                template.Terrain,
                template.Masks,
                template.Features,
                [new PayloadValidationCheck("control-point", "waypoint-z", PayloadValidationStatus.Failed, message: "Mismatch")],
                template.PayloadSha256Hex,
                template.Imagery);

            Assert.True(writer.WriteReport(failed));
            Assert.False(writer.TryCommit());
        }

        Assert.True(bag.HasErrors);
        Assert.False(Directory.Exists(output));
        AssertStageCleaned(root, output);
    }

    [Fact]
    public void ErrorDiagnosticInReport_BlocksCommitBeforeThePublishedPathExists()
    {
        using var root = new ReaderTestSupport.ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        var bag = new GeoDiagnosticBag();

        using (var writer = WriterTestSupport.CreateWriter(output, bag))
        {
            var written = WriterTestSupport.WriteFullPayload(writer, chunksX: 1, chunksY: 1);
            var template = written.Report;
            var withError = new GeneratedMapPayloadReport(
                [new PayloadDiagnostic("GEO-GEN-001", GeoStage.Generate, GeoSeverity.Error, "Generation failed a source.")],
                template.Terrain,
                template.Masks,
                template.Features,
                template.ValidationChecks,
                template.PayloadSha256Hex,
                template.Imagery);

            Assert.True(writer.WriteReport(withError));
            Assert.False(writer.TryCommit());
        }

        Assert.True(bag.HasErrors);
        Assert.False(Directory.Exists(output));
        AssertStageCleaned(root, output);
    }

    [Fact]
    public void JobBagErrors_BlockCommitBeforeReaderValidation()
    {
        using var root = new ReaderTestSupport.ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        var bag = new GeoDiagnosticBag();
        bag.Add(new GeoDiagnostic("GEO-GEN-001", GeoStage.Generate, GeoSeverity.Error, "A generation step failed."));

        using (var writer = WriterTestSupport.CreateWriter(output, bag))
        {
            WriterTestSupport.WriteFullPayload(writer, chunksX: 1, chunksY: 1);

            Assert.False(writer.TryCommit());
            Assert.Equal(GeneratedMapPayloadWriterStatus.Failed, writer.Status);
        }

        Assert.Contains(bag.Items, diagnostic => diagnostic.Code == PayloadWriterDiagnostics.CommitBlockedByJobErrors);
        Assert.False(Directory.Exists(output));
        AssertStageCleaned(root, output);
    }

    [Fact]
    public void MetadataPaletteReorderedAgainstStagedMaterials_BlocksBeforePublish()
    {
        using var root = new ReaderTestSupport.ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        var bag = new GeoDiagnosticBag();

        using (var writer = WriterTestSupport.CreateWriter(output, bag))
        {
            var descriptors = WriterTestSupport.WriteChunks(writer, chunksX: 1, chunksY: 1, imagery: false);
            var metadata = WriterTestSupport.BuildMetadata(
                descriptors,
                chunksX: 1,
                chunksY: 1,
                imagery: false,
                paletteOverride: new MaterialPalette(ReaderTestSupport.PaletteNames.Reverse().ToArray()));

            Assert.Null(writer.WriteMetadata(metadata));
            Assert.False(writer.TryCommit());
        }

        Assert.Contains(bag.Items, diagnostic => diagnostic.Code == StagedFileDiagnostics.MaterialPaletteMismatch);
        Assert.False(Directory.Exists(output));
        AssertStageCleaned(root, output);
    }

    [Fact]
    public void PreCancelledCommit_ThrowsAndDisposalPreservesThePreviousOutput()
    {
        using var root = new ReaderTestSupport.ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "old.bin"), "operator");
        var before = WriterTestSupport.SnapshotFiles(output);
        var bag = new GeoDiagnosticBag();

        using var writer = WriterTestSupport.CreateWriter(output, bag, force: true);
        WriterTestSupport.WriteFullPayload(writer, chunksX: 1, chunksY: 1);
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => writer.TryCommit(source.Token));

        writer.Dispose();

        Assert.Equal(before, WriterTestSupport.SnapshotFiles(output));
        AssertStageCleaned(root, output);
        Assert.Null(writer.RetainedBackupPath);
    }

    [Fact]
    public void LateCancellation_DoesNotFalseFailTheCommittedOutput()
    {
        using var root = new ReaderTestSupport.ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        var bag = new GeoDiagnosticBag();
        using var source = new CancellationTokenSource();

        using (var writer = WriterTestSupport.CreateWriter(output, bag))
        {
            WriterTestSupport.WriteFullPayload(writer, chunksX: 1, chunksY: 1);

            Assert.True(writer.TryCommit(source.Token));

            source.Cancel();

            Assert.True(writer.TryCommit(source.Token));
            Assert.Equal(GeneratedMapPayloadWriterStatus.Committed, writer.Status);
        }

        Assert.False(bag.HasErrors);

        var readBag = new GeoDiagnosticBag();
        var reader = GeneratedMapPayloadReader.Open(output, readBag);

        Assert.NotNull(reader);
        reader?.Dispose();
    }

    [Fact]
    public void RepeatedCommitAndDispose_AreIdempotentAndPathsStayReadable()
    {
        using var root = new ReaderTestSupport.ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        var bag = new GeoDiagnosticBag();
        GeneratedMapPayloadWriter writer = WriterTestSupport.CreateWriter(output, bag);

        using (writer)
        {
            WriterTestSupport.WriteFullPayload(writer, chunksX: 1, chunksY: 1);

            Assert.True(writer.TryCommit());
            Assert.True(writer.TryCommit());

            var stageDirectory = writer.StageDirectory;
            var backup = writer.RetainedBackupPath;

            writer.Dispose();
            writer.Dispose();

            Assert.Equal(GeneratedMapPayloadWriterStatus.Committed, writer.Status);
            Assert.Equal(output, writer.OutputDirectory);
            Assert.Equal(stageDirectory, writer.StageDirectory);
            Assert.Equal(backup, writer.RetainedBackupPath);
        }

        Assert.True(File.Exists(Path.Combine(output, "payload.json")));
    }

    [Fact]
    public void OperationsAfterDispose_ThrowWhileStatusAndPathsStayReadable()
    {
        using var root = new ReaderTestSupport.ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        var bag = new GeoDiagnosticBag();
        var writer = WriterTestSupport.CreateWriter(output, bag);
        writer.Dispose();

        var terrain = ReaderTestSupport.TerrainFromLattice(0, 0);

        Assert.Throws<ObjectDisposedException>(() => writer.WriteTerrain(new TerrainChunk(0, 0, terrain)));
        Assert.Throws<ObjectDisposedException>(() => writer.WriteReport(null!));
        Assert.Throws<ObjectDisposedException>(() => writer.TryCommit());
        Assert.Equal(GeneratedMapPayloadWriterStatus.Staging, writer.Status);
        Assert.Equal(output, writer.OutputDirectory);
        Assert.NotNull(writer.StageDirectory);
    }

    [Fact]
    public void WritesAfterACommitAttempt_ThrowInsteadOfStagingAgain()
    {
        using var root = new ReaderTestSupport.ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        var bag = new GeoDiagnosticBag();

        using var writer = WriterTestSupport.CreateWriter(output, bag);
        var written = WriterTestSupport.WriteFullPayload(writer, chunksX: 1, chunksY: 1);

        Assert.True(writer.TryCommit());

        Assert.Throws<InvalidOperationException>(
            () => writer.WriteTerrain(new TerrainChunk(0, 0, ReaderTestSupport.TerrainFromLattice(0, 0))));
        Assert.Throws<InvalidOperationException>(() => writer.WriteMetadata(written.Metadata));
        Assert.Throws<InvalidOperationException>(() => writer.WriteReport(written.Report));
        Assert.Throws<InvalidOperationException>(() => writer.WriteImagery(null!));
    }

    [Fact]
    public void StagedFilesAccessor_ExposesTheSameHelperTheFacadeWritesThrough()
    {
        using var root = new ReaderTestSupport.ScratchRoot();
        var output = Path.Combine(root.Root, "out");
        var bag = new GeoDiagnosticBag();

        using var writer = WriterTestSupport.CreateWriter(output, bag);

        Assert.Same(writer.StagedFiles, writer.StagedFiles);

        var descriptors = WriterTestSupport.WriteChunks(writer, chunksX: 1, chunksY: 1, imagery: false);
        var metadata = WriterTestSupport.BuildMetadata(descriptors, chunksX: 1, chunksY: 1, imagery: false);

        Assert.NotNull(writer.WriteMetadata(metadata));
        Assert.True(File.Exists(Path.Combine(writer.StageDirectory, "payload.json")));

        // The palette bound by the facade's material and metadata writes is visible
        // through the accessor, so generation and the importer can never stage around
        // the guard through a second helper.
        var mismatched = new MaterialPalette(ReaderTestSupport.PaletteNames.Reverse().ToArray());

        Assert.Null(writer.StagedFiles.WriteMaterial(StagedFilesTestSupport.MaterialChunkWith(mismatched, 2, 0)));
        Assert.Contains(bag.Items, diagnostic => diagnostic.Code == StagedFileDiagnostics.MaterialPaletteMismatch);
    }

    private static void AssertStageCleaned(ReaderTestSupport.ScratchRoot root, string output)
    {
        Assert.Empty(Directory.GetDirectories(root.Root, $"{Path.GetFileName(output)}.tmp-*"));
    }
}
