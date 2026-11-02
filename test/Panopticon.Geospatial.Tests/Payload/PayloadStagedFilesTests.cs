#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Payload.Storage;
using Xunit;

namespace Panopticon.Geospatial.Tests.Payload;

public class PayloadStagedFilesTests
{
    [Fact]
    public void ChunkWrites_ProduceCanonicalPathsWithExactBoundedContent()
    {
        using var fixture = StagedFilesTestSupport.StageFixture.Create();
        var staged = fixture.CreateStagedFiles();

        var terrainChunk = StagedFilesTestSupport.TerrainChunkAt(0, 0);
        var materialChunk = StagedFilesTestSupport.MaterialChunkAt(0, 0);
        var maskChunk = StagedFilesTestSupport.MaskChunkAt(0, 0);
        var imageryChunk = StagedFilesTestSupport.ImageryChunkAt(0, 0);

        var terrain = staged.WriteTerrain(terrainChunk);
        var material = staged.WriteMaterial(materialChunk);
        var mask = staged.WriteMask(maskChunk);
        var imagery = staged.WriteImagery(imageryChunk);
        var second = staged.WriteTerrain(StagedFilesTestSupport.TerrainChunkAt(12, 3));

        Assert.NotNull(terrain);
        Assert.NotNull(material);
        Assert.NotNull(mask);
        Assert.NotNull(imagery);
        Assert.NotNull(second);
        Assert.False(fixture.Bag.HasErrors);

        Assert.Equal("terrain/0_0.bin", terrain!.Path);
        Assert.Equal("materials/0_0.bin", material!.Path);
        Assert.Equal("masks/0_0.bin", mask!.Path);
        Assert.Equal("imagery/0_0.png", imagery!.Path);
        Assert.Equal("terrain/12_3.bin", second!.Path);

        var terrainBytes = File.ReadAllBytes(fixture.StagePath("terrain/0_0.bin"));
        Assert.Equal(PayloadChunkDescriptor.TerrainChunkBytes, terrainBytes.Length);
        Assert.Equal(terrainBytes.Length, terrain.LengthBytes);
        Assert.Equal(StagedFilesTestSupport.Sha256Hex(terrainBytes), terrain.Sha256Hex);

        Assert.Equal(
            terrainChunk.GetHeight(0, 0),
            BinaryPrimitives.ReadSingleLittleEndian(terrainBytes.AsSpan(0, 4)));

        Assert.Equal(
            terrainChunk.Heights.ToArray(),
            PayloadChunkCodec.DecodeTerrain(0, 0, terrainBytes)!.Heights.ToArray());

        var materialBytes = File.ReadAllBytes(fixture.StagePath("materials/0_0.bin"));
        Assert.Equal(PayloadChunkDescriptor.MaterialChunkBytes, materialBytes.Length);
        Assert.Equal(materialBytes.Length, material.LengthBytes);
        Assert.Equal(StagedFilesTestSupport.Sha256Hex(materialBytes), material.Sha256Hex);
        Assert.Equal(
            materialChunk.Indices.ToArray(),
            PayloadChunkCodec.DecodeMaterial(0, 0, materialBytes, StagedFilesTestSupport.Palette)!.Indices.ToArray());

        var maskBytes = File.ReadAllBytes(fixture.StagePath("masks/0_0.bin"));
        Assert.Equal(PayloadChunkDescriptor.MaskChunkBytes, maskBytes.Length);
        Assert.Equal(maskBytes.Length, mask.LengthBytes);
        Assert.Equal(StagedFilesTestSupport.Sha256Hex(maskBytes), mask.Sha256Hex);
        Assert.Equal(
            maskChunk.Flags.ToArray(),
            PayloadChunkCodec.DecodeMask(0, 0, maskBytes)!.Flags.ToArray());

        var imageryBytes = File.ReadAllBytes(fixture.StagePath("imagery/0_0.png"));
        Assert.Equal(imageryChunk.Png.Length, imageryBytes.Length);
        Assert.Equal(imageryBytes.Length, imagery.LengthBytes);
        Assert.Equal(StagedFilesTestSupport.Sha256Hex(imageryBytes), imagery.Sha256Hex);
        Assert.True(imageryBytes.AsSpan().SequenceEqual(imageryChunk.Png.Span));
    }

    [Fact]
    public void RepeatedChunkWrite_FailsUnchanged()
    {
        using var fixture = StagedFilesTestSupport.StageFixture.Create();
        var staged = fixture.CreateStagedFiles();
        var chunk = StagedFilesTestSupport.TerrainChunkAt(0, 0);

        Assert.NotNull(staged.WriteTerrain(chunk));
        var bytesBefore = File.ReadAllBytes(fixture.StagePath("terrain/0_0.bin"));

        Assert.Null(staged.WriteTerrain(chunk));

        var diagnostic = SingleError(fixture.Bag, StagedFileDiagnostics.StagedFileAlreadyExists);
        Assert.Equal(GeoStage.Write, diagnostic.Stage);
        Assert.Equal(bytesBefore, File.ReadAllBytes(fixture.StagePath("terrain/0_0.bin")));
    }

    [Fact]
    public void PriorUnownedStageFile_FailsUnchanged()
    {
        using var fixture = StagedFilesTestSupport.StageFixture.Create();
        var staged = fixture.CreateStagedFiles();
        var terrainDirectory = fixture.StagePath("terrain");
        Directory.CreateDirectory(terrainDirectory);
        File.WriteAllText(fixture.StagePath("terrain/0_0.bin"), "operator content");

        Assert.Null(staged.WriteTerrain(StagedFilesTestSupport.TerrainChunkAt(0, 0)));

        SingleError(fixture.Bag, StagedFileDiagnostics.StagedFileAlreadyExists);
        Assert.Equal("operator content", File.ReadAllText(fixture.StagePath("terrain/0_0.bin")));
    }

    [Fact]
    public void WriteMetadata_StreamsBoundedJsonAndReturnsTheActualWrittenHash()
    {
        using var fixture = StagedFilesTestSupport.StageFixture.Create();
        var staged = fixture.CreateStagedFiles();
        var metadata = PayloadTestSupport.Metadata();

        var sha256Hex = staged.WriteMetadata(metadata);

        Assert.NotNull(sha256Hex);
        Assert.False(fixture.Bag.HasErrors);

        var bytes = File.ReadAllBytes(fixture.StagePath("payload.json"));
        Assert.Equal(StagedFilesTestSupport.Sha256Hex(bytes), sha256Hex);

        var diagnostics = new GeoDiagnosticBag();
        var readBack = PayloadTestSupport.ReadMetadata(bytes, diagnostics: diagnostics);

        Assert.NotNull(readBack);
        Assert.False(diagnostics.HasErrors);
        Assert.Equal(bytes, PayloadTestSupport.WriteMetadataBytes(readBack!));
    }

    [Fact]
    public void RepeatedMetadataWrite_FailsAndPreservesTheFirstFileAndHash()
    {
        using var fixture = StagedFilesTestSupport.StageFixture.Create();
        var staged = fixture.CreateStagedFiles();
        var first = staged.WriteMetadata(PayloadTestSupport.Metadata());

        Assert.NotNull(first);
        var bytesBefore = File.ReadAllBytes(fixture.StagePath("payload.json"));

        Assert.Null(staged.WriteMetadata(PayloadTestSupport.Metadata()));

        SingleError(fixture.Bag, StagedFileDiagnostics.StagedFileAlreadyExists);
        Assert.Equal(bytesBefore, File.ReadAllBytes(fixture.StagePath("payload.json")));

        Assert.True(staged.WriteReport(StagedFilesTestSupport.ReportWithHash(first!)));
    }

    [Fact]
    public void WriteReport_BindsTheActualMetadataHashAndRoundTrips()
    {
        using var fixture = StagedFilesTestSupport.StageFixture.Create();
        var staged = fixture.CreateStagedFiles();
        var sha256Hex = staged.WriteMetadata(PayloadTestSupport.Metadata());

        Assert.NotNull(sha256Hex);
        Assert.True(staged.WriteReport(StagedFilesTestSupport.ReportWithHash(sha256Hex!)));
        Assert.False(fixture.Bag.HasErrors);

        var bytes = File.ReadAllBytes(fixture.StagePath("report.json"));

        var diagnostics = new GeoDiagnosticBag();
        var readBack = PayloadTestSupport.ReadReport(bytes, diagnostics: diagnostics);

        Assert.NotNull(readBack);
        Assert.False(diagnostics.HasErrors);
        Assert.Equal(sha256Hex, readBack!.PayloadSha256Hex);
        Assert.Equal(bytes, PayloadTestSupport.WriteReportBytes(readBack));
    }

    [Fact]
    public void WriteReport_BeforeMetadata_FailsWithTheKnownCode()
    {
        using var fixture = StagedFilesTestSupport.StageFixture.Create();
        var staged = fixture.CreateStagedFiles();

        Assert.False(staged.WriteReport(StagedFilesTestSupport.ReportWithHash(PayloadTestSupport.PayloadSha256Hex)));

        SingleError(fixture.Bag, StagedFileDiagnostics.ReportBeforeMetadata);
        Assert.False(File.Exists(fixture.StagePath("report.json")));
    }

    [Fact]
    public void WriteReport_WithWrongPayloadHash_FailsWithoutWriting()
    {
        using var fixture = StagedFilesTestSupport.StageFixture.Create();
        var staged = fixture.CreateStagedFiles();
        Assert.NotNull(staged.WriteMetadata(PayloadTestSupport.Metadata()));

        Assert.False(staged.WriteReport(StagedFilesTestSupport.ReportWithHash(PayloadTestSupport.Sha256(999))));

        SingleError(fixture.Bag, StagedFileDiagnostics.ReportHashMismatch);
        Assert.False(File.Exists(fixture.StagePath("report.json")));
    }

    [Fact]
    public void RewriteReport_AtomicallyReplacesTheOwnedReportWithoutTempLeftovers()
    {
        using var fixture = StagedFilesTestSupport.StageFixture.Create();
        var staged = fixture.CreateStagedFiles();
        var metadata = PayloadTestSupport.Metadata();
        var sha256Hex = staged.WriteMetadata(metadata);
        Assert.NotNull(sha256Hex);

        var initial = StagedFilesTestSupport.ReportWithHash(sha256Hex!);
        Assert.True(staged.WriteReport(initial));

        var rewritten = StagedFilesTestSupport.ReportWithHash(
            sha256Hex!,
            [new PayloadValidationCheck("payload", "chunk-inventory", PayloadValidationStatus.Passed, message: "Rewritten after the staged validators ran")]);

        Assert.True(staged.WriteReport(rewritten));
        Assert.False(fixture.Bag.HasErrors);

        Assert.Equal(PayloadTestSupport.WriteReportBytes(rewritten), File.ReadAllBytes(fixture.StagePath("report.json")));
        Assert.Equal(PayloadTestSupport.WriteMetadataBytes(metadata), File.ReadAllBytes(fixture.StagePath("payload.json")));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(fixture.Stage, "*", SearchOption.AllDirectories),
            path => Path.GetFileName(path).Contains(".tmp-"));

        Assert.True(staged.WriteReport(initial));
        Assert.Equal(PayloadTestSupport.WriteReportBytes(initial), File.ReadAllBytes(fixture.StagePath("report.json")));
    }

    [Fact]
    public void RewriteReport_OverASwappedSymlinkFailsClosedAndLeavesTheTargetUntouched()
    {
        using var fixture = StagedFilesTestSupport.StageFixture.Create();
        var staged = fixture.CreateStagedFiles();
        var sha256Hex = staged.WriteMetadata(PayloadTestSupport.Metadata());
        Assert.NotNull(sha256Hex);
        Assert.True(staged.WriteReport(StagedFilesTestSupport.ReportWithHash(sha256Hex!)));

        var outsideTarget = Path.Combine(Path.GetTempPath(), "panopticon-staged-tests-outside-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(outsideTarget, "operator report");

        try
        {
            File.Delete(fixture.StagePath("report.json"));
            File.CreateSymbolicLink(fixture.StagePath("report.json"), outsideTarget);

            var rewritten = StagedFilesTestSupport.ReportWithHash(
                sha256Hex!,
                [new PayloadValidationCheck("payload", "chunk-inventory", PayloadValidationStatus.Passed, message: "Never written")]);

            Assert.False(staged.WriteReport(rewritten));

            SingleError(fixture.Bag, StagedFileDiagnostics.StagedFileAlreadyExists);
            Assert.Equal("operator report", File.ReadAllText(outsideTarget));
            Assert.NotNull(File.ResolveLinkTarget(fixture.StagePath("report.json"), returnFinalTarget: false));
        }
        finally
        {
            File.Delete(outsideTarget);
        }
    }

    [Fact]
    public void StageRootSymbolicLink_FailsClosedAndNeverWritesThroughIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "panopticon-staged-tests-" + Guid.NewGuid().ToString("N"));
        var realStage = Path.Combine(root, "real-stage");
        var linkedStage = Path.Combine(root, "linked-stage");
        Directory.CreateDirectory(realStage);

        try
        {
            Directory.CreateSymbolicLink(linkedStage, realStage);
            var bag = new GeoDiagnosticBag();
            var staged = new PayloadStagedFiles(linkedStage, new GeospatialImportLimits(), bag);

            Assert.Null(staged.WriteTerrain(StagedFilesTestSupport.TerrainChunkAt(0, 0)));

            var diagnostic = SingleError(bag, StagedFileDiagnostics.StageRootInvalid);
            Assert.Equal(GeoStage.Write, diagnostic.Stage);
            Assert.Empty(Directory.EnumerateFileSystemEntries(realStage));
        }
        finally
        {
            BclPayloadDirectoryIo.Instance.DeleteDirectory(root);
        }
    }

    [Fact]
    public void SubdirectorySymbolicLink_FailsClosedAndNeverWritesThroughIt()
    {
        using var fixture = StagedFilesTestSupport.StageFixture.Create();
        var outside = Path.Combine(Path.GetDirectoryName(fixture.Stage)!, "outside-terrain");
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(fixture.StagePath("terrain"), outside);

        var staged = fixture.CreateStagedFiles();

        Assert.Null(staged.WriteTerrain(StagedFilesTestSupport.TerrainChunkAt(0, 0)));

        SingleError(fixture.Bag, StagedFileDiagnostics.StageSubdirectoryInvalid);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        Assert.False(File.Exists(fixture.StagePath("terrain/0_0.bin")));
    }

    [Fact]
    public void TargetFileSymbolicLink_FailsClosedAndNeverWritesThroughIt()
    {
        using var fixture = StagedFilesTestSupport.StageFixture.Create();
        var masks = fixture.StagePath("masks");
        Directory.CreateDirectory(masks);
        var outsideTarget = Path.Combine(Path.GetDirectoryName(fixture.Stage)!, "outside-mask.bin");
        File.CreateSymbolicLink(fixture.StagePath("masks/0_0.bin"), outsideTarget);

        var staged = fixture.CreateStagedFiles();

        Assert.Null(staged.WriteMask(StagedFilesTestSupport.MaskChunkAt(0, 0)));

        SingleError(fixture.Bag, StagedFileDiagnostics.StagedFileAlreadyExists);
        Assert.False(File.Exists(outsideTarget));
        Assert.NotNull(File.ResolveLinkTarget(fixture.StagePath("masks/0_0.bin"), returnFinalTarget: false));
    }

    [Fact]
    public void MetadataOverTheByteBound_FailsBoundedWithoutAPartialFile()
    {
        using var fixture = StagedFilesTestSupport.StageFixture.Create();
        var staged = fixture.CreateStagedFiles(new GeospatialImportLimits { MaxPayloadMetadataBytes = 512 });

        Assert.Null(staged.WriteMetadata(PayloadTestSupport.Metadata()));

        SingleError(fixture.Bag, StagedFileDiagnostics.MetadataLimitExceeded);
        Assert.False(File.Exists(fixture.StagePath("payload.json")));
    }

    [Fact]
    public void ReportOverTheByteBound_FailsBoundedWithoutReportOrTempFiles()
    {
        using var fixture = StagedFilesTestSupport.StageFixture.Create();
        var metadata = PayloadTestSupport.Metadata();
        var bound = PayloadTestSupport.WriteMetadataBytes(metadata).Length + 1;
        var staged = fixture.CreateStagedFiles(new GeospatialImportLimits { MaxPayloadMetadataBytes = bound });
        var sha256Hex = staged.WriteMetadata(metadata);

        Assert.NotNull(sha256Hex);
        Assert.False(staged.WriteReport(StagedFilesTestSupport.ReportWithHash(sha256Hex!, StagedFilesTestSupport.ManyChecks(5000))));

        SingleError(fixture.Bag, StagedFileDiagnostics.MetadataLimitExceeded);
        Assert.False(File.Exists(fixture.StagePath("report.json")));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(fixture.Stage, "*", SearchOption.AllDirectories),
            path => Path.GetFileName(path).Contains(".tmp-"));
    }

    [Fact]
    public void CancelledToken_PropagatesAndLeavesNoFile()
    {
        using var fixture = StagedFilesTestSupport.StageFixture.Create();
        var staged = fixture.CreateStagedFiles();
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => staged.WriteMetadata(PayloadTestSupport.Metadata(), source.Token));
        Assert.Throws<OperationCanceledException>(() => staged.WriteTerrain(StagedFilesTestSupport.TerrainChunkAt(0, 0), source.Token));

        Assert.False(fixture.Bag.HasErrors);
        Assert.False(File.Exists(fixture.StagePath("payload.json")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.Stage));
    }

    [Fact]
    public void CancellationDuringAttributeEnumeration_RemovesTheOwnedPartialFile()
    {
        using var fixture = StagedFilesTestSupport.StageFixture.Create();
        var staged = fixture.CreateStagedFiles();
        using var source = new CancellationTokenSource();
        var payloadPath = fixture.StagePath("payload.json");

        var attributes = new ArmedAttributeDictionary();
        var metadata = StagedFilesTestSupport.MetadataWithSharedAttributes(attributes);
        var existedWhenEnumerated = false;
        var bytesWhenEnumerated = 0L;

        // Attribute enumeration follows a flush, proving cancellation after owned bytes exist.
        attributes.Arm(() =>
        {
            existedWhenEnumerated = File.Exists(payloadPath);
            bytesWhenEnumerated = new FileInfo(payloadPath).Length;
            source.Cancel();
        });

        Assert.ThrowsAny<OperationCanceledException>(() => staged.WriteMetadata(metadata, source.Token));

        Assert.True(attributes.KeysEnumerated, "the attribute enumeration hook must fire during serialization");
        Assert.True(existedWhenEnumerated, "the owned payload.json must be open when cancellation fires");
        Assert.True(bytesWhenEnumerated > 0, "flushed bytes must already be on disk when cancellation fires");
        Assert.False(File.Exists(payloadPath), "the owned partial payload.json must not remain after cancellation");
    }

    [Fact]
    public void CancelledImageryWrite_PropagatesAndLeavesNoFile()
    {
        using var fixture = StagedFilesTestSupport.StageFixture.Create();
        var staged = fixture.CreateStagedFiles();
        var chunk = StagedFilesTestSupport.ImageryChunkAt(0, 0);
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => staged.WriteImagery(chunk, source.Token));

        Assert.False(fixture.Bag.HasErrors);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.Stage));
    }

    [Fact]
    public void MalformedImageryPng_FailsWithTheKnownHeaderErrorAndWritesNothing()
    {
        using var fixture = StagedFilesTestSupport.StageFixture.Create();
        var staged = fixture.CreateStagedFiles();
        var png = new byte[64];
        PngSignature.CopyTo(png);
        var chunk = new ImageryChunk(2, 3, 16, png);

        Assert.Null(staged.WriteImagery(chunk));

        SingleError(fixture.Bag, StagedFileDiagnostics.ImageryHeaderInvalid);
        Assert.False(File.Exists(fixture.StagePath("imagery/2_3.png")));
    }

    [Fact]
    public void ImageryBeyondTheRasterDimensionLimits_FailsBounded()
    {
        using var fixture = StagedFilesTestSupport.StageFixture.Create();
        var staged = fixture.CreateStagedFiles(new GeospatialImportLimits { MaxRasterWidth = 8, MaxRasterHeight = 8 });

        Assert.Null(staged.WriteImagery(StagedFilesTestSupport.ImageryChunkAt(0, 0, pixelsPerAxis: 16)));

        SingleError(fixture.Bag, StagedFileDiagnostics.ImageryBudgetExceeded);
        Assert.False(File.Exists(fixture.StagePath("imagery/0_0.png")));
    }

    [Fact]
    public void MaterialChunkPaletteMismatch_FailsAndPreservesExistingFiles()
    {
        using var fixture = StagedFilesTestSupport.StageFixture.Create();
        var staged = fixture.CreateStagedFiles();

        Assert.NotNull(staged.WriteMaterial(StagedFilesTestSupport.MaterialChunkAt(0, 0)));

        var reordered = new MaterialPalette(StagedFilesTestSupport.Palette.Names.Reverse().ToArray());

        Assert.Null(staged.WriteMaterial(StagedFilesTestSupport.MaterialChunkWith(reordered, 1, 0)));

        SingleError(fixture.Bag, StagedFileDiagnostics.MaterialPaletteMismatch);
        Assert.True(File.Exists(fixture.StagePath("materials/0_0.bin")));
        Assert.False(File.Exists(fixture.StagePath("materials/1_0.bin")));
    }

    [Fact]
    public void EqualPaletteContentInAnyInstance_BindsInBothOrders()
    {
        using var materialFirst = StagedFilesTestSupport.StageFixture.Create();
        var ordered = new MaterialPalette(ReaderTestSupport.PaletteNames);

        Assert.NotNull(materialFirst.CreateStagedFiles().WriteMaterial(StagedFilesTestSupport.MaterialChunkWith(ordered, 0, 0)));
        Assert.NotNull(materialFirst.CreateStagedFiles().WriteMetadata(StagedFilesTestSupport.MetadataWithPalette(new MaterialPalette(ReaderTestSupport.PaletteNames))));
        Assert.False(materialFirst.Bag.HasErrors);

        using var metadataFirst = StagedFilesTestSupport.StageFixture.Create();
        var staged = metadataFirst.CreateStagedFiles();

        Assert.NotNull(staged.WriteMetadata(StagedFilesTestSupport.MetadataWithPalette(new MaterialPalette(ReaderTestSupport.PaletteNames))));
        Assert.NotNull(staged.WriteMaterial(StagedFilesTestSupport.MaterialChunkWith(ordered, 0, 0)));
        Assert.NotNull(staged.WriteMaterial(StagedFilesTestSupport.MaterialChunkWith(new MaterialPalette(ReaderTestSupport.PaletteNames), 1, 0)));
        Assert.False(metadataFirst.Bag.HasErrors);
    }

    [Fact]
    public void MetadataPaletteReorderedAgainstStagedMaterials_FailsWithoutWritingPayloadJson()
    {
        using var fixture = StagedFilesTestSupport.StageFixture.Create();
        var staged = fixture.CreateStagedFiles();

        Assert.NotNull(staged.WriteMaterial(StagedFilesTestSupport.MaterialChunkAt(0, 0)));

        var reordered = new MaterialPalette(StagedFilesTestSupport.Palette.Names.Reverse().ToArray());

        Assert.Null(staged.WriteMetadata(StagedFilesTestSupport.MetadataWithPalette(reordered)));

        SingleError(fixture.Bag, StagedFileDiagnostics.MaterialPaletteMismatch);
        Assert.False(File.Exists(fixture.StagePath("payload.json")));
    }

    [Fact]
    public void MaterialAfterMetadata_MustMatchTheMetadataPalette()
    {
        using var fixture = StagedFilesTestSupport.StageFixture.Create();
        var staged = fixture.CreateStagedFiles();

        Assert.NotNull(staged.WriteMetadata(StagedFilesTestSupport.MetadataWithPalette(StagedFilesTestSupport.Palette)));

        var reordered = new MaterialPalette(StagedFilesTestSupport.Palette.Names.Reverse().ToArray());

        Assert.Null(staged.WriteMaterial(StagedFilesTestSupport.MaterialChunkWith(reordered, 0, 0)));

        SingleError(fixture.Bag, StagedFileDiagnostics.MaterialPaletteMismatch);
        Assert.False(File.Exists(fixture.StagePath("materials/0_0.bin")));
        Assert.True(File.Exists(fixture.StagePath("payload.json")));
    }

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static GeoDiagnostic SingleError(GeoDiagnosticBag bag, string code)
    {
        var diagnostic = Assert.Single(bag.Items, item => item.Code == code);
        Assert.Equal(GeoSeverity.Error, diagnostic.Severity);

        return diagnostic;
    }

    /// <summary>
    /// Cancels deterministically during serialization through the trusted shared-attribute seam.
    /// </summary>
    private sealed class ArmedAttributeDictionary : IReadOnlyDictionary<string, string>
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal)
        {
            ["armed"] = "attribute",
        };

        private Action? _whenKeysEnumerated;

        internal bool KeysEnumerated { get; private set; }

        internal void Arm(Action whenKeysEnumerated)
        {
            _whenKeysEnumerated = whenKeysEnumerated;
        }

        public int Count => _values.Count;

        public IEnumerable<string> Keys
        {
            get
            {
                if (_whenKeysEnumerated is not null && !KeysEnumerated)
                {
                    KeysEnumerated = true;
                    _whenKeysEnumerated();
                }

                return _values.Keys;
            }
        }

        public IEnumerable<string> Values => _values.Values;

        public string this[string key] => _values[key];

        public bool ContainsKey(string key) => _values.ContainsKey(key);

        public bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
