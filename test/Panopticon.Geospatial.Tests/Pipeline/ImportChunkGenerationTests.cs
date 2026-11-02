#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Payload.Storage;
using Panopticon.Geospatial.Pipeline.Generation;
using Panopticon.Geospatial.Sampling;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using static Panopticon.Geospatial.Tests.Pipeline.ChunkGenerationTestSupport;

namespace Panopticon.Geospatial.Tests.Pipeline;

public class ImportChunkGenerationTests
{
    [Fact]
    public void Run_OneChunk_WritesStagedFilesBuildsMetadataAndIndependentSummaries()
    {
        var (plan, planDiagnostics) = TryPlan(
            withImagery: true,
            withClassification: true,
            classificationOf: static (x, y) => x < 640d ? 1 : 7,
            materialRules: [new MaterialRule("rock", ClassificationValue: 1)]);
        Assert.False(planDiagnostics.HasErrors);
        Assert.NotNull(plan);
        using var stage = StageFixture.Create();
        var diagnostics = new GeoDiagnosticBag();

        var output = ImportChunkGeneration.Run(plan!, stage.Files, stage.Stage, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(output);

        // Every staged file exists with its canonical fixed length; only chunk files
        // are written, never payload.json or report.json.
        Assert.Equal(PayloadChunkDescriptor.TerrainChunkBytes, new FileInfo(StagePath(stage.Stage, "terrain/0_0.bin")).Length);
        Assert.Equal(PayloadChunkDescriptor.MaterialChunkBytes, new FileInfo(StagePath(stage.Stage, "materials/0_0.bin")).Length);
        Assert.Equal(PayloadChunkDescriptor.MaskChunkBytes, new FileInfo(StagePath(stage.Stage, "masks/0_0.bin")).Length);
        Assert.True(new FileInfo(StagePath(stage.Stage, "imagery/0_0.png")).Length > 0);
        Assert.False(File.Exists(StagePath(stage.Stage, PayloadChunkPaths.MetadataFileName)));
        Assert.False(File.Exists(StagePath(stage.Stage, PayloadChunkPaths.ReportFileName)));

        var metadata = output!.Metadata;
        Assert.Equal(GeneratedMapPayloadMetadata.PayloadKind, metadata.Kind);
        Assert.Equal(GeneratedMapPayloadMetadata.CurrentSchemaVersion, metadata.SchemaVersion);
        Assert.Equal(plan!.GridDefinition, metadata.Grid);
        Assert.Equal(plan.Palette, metadata.Palette);
        Assert.Equal(plan.ManifestSha256, metadata.ManifestSha256Hex);
        Assert.Equal(plan.Provenance, metadata.Provenance);
        Assert.Equal(1, metadata.Grid.TotalChunkCount);

        var descriptor = Assert.Single(metadata.Chunks);
        Assert.Equal(0, descriptor.ChunkX);
        Assert.Equal(0, descriptor.ChunkY);
        Assert.Equal("terrain/0_0.bin", descriptor.Terrain.Path);
        Assert.Equal("materials/0_0.bin", descriptor.Materials.Path);
        Assert.Equal("masks/0_0.bin", descriptor.Masks.Path);
        Assert.Equal("imagery/0_0.png", descriptor.Imagery!.Path);

        // The three sampling streams stay independent: terrain counts every stored
        // sample, imagery every channel of every pixel, classification every core cell.
        Assert.Equal(131L * 131L, output.Terrain.Sampling.SampleCount);
        Assert.Equal(128L * 128L * 3L, output.Imagery!.Sampling.SampleCount);
        Assert.Equal(129L * 129L, output.ClassificationSampling.SampleCount);
        Assert.Equal(1, output.Terrain.ChunkCount);
        Assert.Equal(1, output.Imagery.ChunkCount);
        Assert.Equal(1, output.Masks.ChunkCount);
        Assert.Equal(0, output.Terrain.Sampling.FailedSampleCount);

        // One-to-one spacing keeps Auto inside its continuous pair: bilinear or
        // area-weighted, split by nanometer round-off at the exact ratio, and both are
        // exact on the plane. The categorical classification source always selects
        // nearest.
        Assert.Equal(
            131L * 131L,
            KernelCount(output.Terrain.Sampling, SampleKernel.Bilinear) + KernelCount(output.Terrain.Sampling, SampleKernel.AreaWeighted));
        Assert.Equal(
            128L * 128L * 3L,
            KernelCount(output.Imagery.Sampling, SampleKernel.Bilinear) + KernelCount(output.Imagery.Sampling, SampleKernel.AreaWeighted));
        Assert.Equal(129L * 129L, KernelCount(output.ClassificationSampling, SampleKernel.Nearest));

        Assert.Equal(
            2d * PlaneHeight(-10d, -10d),
            output.Terrain.MinHeightGameUnits!.Value,
            3);
        Assert.Equal(
            2d * PlaneHeight(1290d, 1290d),
            output.Terrain.MaxHeightGameUnits!.Value,
            3);

        // Imagery rows run north first: row py is global pixel row 127 - py, so R
        // tracks the column and G tracks the reversed row against the analytic source.
        Assert.Equal(new Rgba32(0, 127, 60, 255), PixelAt(stage.Stage, 0, 0, 0, 0));
        Assert.Equal(new Rgba32(127, 0, 60, 255), PixelAt(stage.Stage, 0, 0, 127, 127));
        Assert.Equal(new Rgba32(64, 63, 60, 255), PixelAt(stage.Stage, 0, 0, 64, 64));
    }

    [Fact]
    public void Run_AnalyticPlaneMatchesTerrainHeightsWithExaggeration()
    {
        var (plan, _) = TryPlan();
        using var stage = StageFixture.Create();
        var diagnostics = new GeoDiagnosticBag();

        var output = ImportChunkGeneration.Run(plan!, stage.Files, stage.Stage, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(output);
        var chunk = PayloadChunkCodec.DecodeTerrain(0, 0, ReadStaged(stage.Stage, "terrain/0_0.bin"), diagnostics);
        Assert.NotNull(chunk);

        // Stored position (sx, sy) is global lattice sample (sx - 1, sy - 1); the
        // plane converts with exaggeration 2 and no vertical origin.
        foreach (var (sx, sy) in new[] { (0, 0), (1, 1), (65, 33), (130, 130) })
        {
            var expected = 2d * PlaneHeight(10d * (sx - 1), 10d * (sy - 1));
            Assert.Equal(expected, chunk!.GetHeight(sx, sy), 3);
        }
    }

    [Fact]
    public void Run_WaypointSamplesTerrainZAndWaterRegionsMap()
    {
        var (plan, _) = TryPlan(
            vectorFeatures:
            [
                VectorPoint("wp", 100d, 200d),
                VectorPolygon("lake", 100d, 100d, 100d, 100d),
            ],
            featureMappings:
            [
                new FeatureMappingRule("vectors", ManifestFeatureTarget.Waypoint, Geometry: ManifestFeatureGeometry.Point),
                new FeatureMappingRule("vectors", ManifestFeatureTarget.Water, Geometry: ManifestFeatureGeometry.Polygon),
            ]);
        using var stage = StageFixture.Create();
        var diagnostics = new GeoDiagnosticBag();

        var output = ImportChunkGeneration.Run(plan!, stage.Files, stage.Stage, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(output);
        var waypoint = Assert.Single(output!.Metadata.Features, static feature => feature.Kind == GeneratedFeatureKind.Waypoint);
        Assert.Equal(100d, waypoint.Position!.Value.X, 6);
        Assert.Equal(200d, waypoint.Position.Value.Y, 6);

        // The point sits on a lattice sample, so the bilinear terrain Z is the exact
        // exaggerated plane value 2 * plane(100, 200) = 30.
        Assert.Equal(30d, waypoint.Position.Value.Z, 6);
        var water = Assert.Single(output.Metadata.Features, static feature => feature.Kind == GeneratedFeatureKind.Water);
        Assert.Equal("vectors/lake#0", water.PartId);
        Assert.Equal(1, water.RuleIndex);
        Assert.Equal(2, output.Features.TotalCount);
        Assert.Equal(1, KindCount(output.Features, GeneratedFeatureKind.Waypoint));
        Assert.Equal(1, KindCount(output.Features, GeneratedFeatureKind.Water));
    }

    [Fact]
    public void Run_PaletteOrderDefaultClassificationAndRegionPainting()
    {
        var (plan, _) = TryPlan(
            withClassification: true,
            classificationOf: static (x, y) => x < 640d ? 1 : 7,
            vectorFeatures:
            [
                VectorPolygon("beach", 200d, 200d, 200d, 200d, "ground", "sand"),
                VectorPolygon("trail", 300d, 300d, 100d, 100d, "folder", "trail"),
            ],
            materialRules:
            [
                new MaterialRule("rock", ClassificationValue: 1),
                new MaterialRule("sand", SourceId: "vectors", Attribute: "ground", Value: "sand"),
            ],
            featureMappings: [new FeatureMappingRule("vectors", ManifestFeatureTarget.Material, Geometry: ManifestFeatureGeometry.Polygon, Material: "path", LayerName: "trail")]);
        using var stage = StageFixture.Create();
        var diagnostics = new GeoDiagnosticBag();

        var output = ImportChunkGeneration.Run(plan!, stage.Files, stage.Stage, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(output);

        // Default first, classification rules in order, attribute rules, then
        // material-target feature mappings.
        Assert.Equal(["dirt", "rock", "sand", "path"], output!.Metadata.Palette.Names);

        var material = PayloadChunkCodec.DecodeMaterial(0, 0, ReadStaged(stage.Stage, "materials/0_0.bin"), plan!.Palette, diagnostics);
        Assert.NotNull(material);

        // (600, 600): classification rock, no polygon coverage.
        Assert.Equal((byte)1, material!.GetIndex(60, 60));

        // (250, 250): classification rock, beach attribute polygon only.
        Assert.Equal((byte)2, material.GetIndex(25, 25));

        // (350, 350): classification rock, beach, then the later mapping rule wins.
        Assert.Equal((byte)3, material.GetIndex(35, 35));

        // (700, 700): unmapped classification value falls back to the default.
        Assert.Equal((byte)0, material.GetIndex(70, 70));
    }

    [Fact]
    public void Run_MaskCountsArePerRuleApplicationsVersusUniqueFinalCells()
    {
        var (plan, _) = TryPlan(
            demHeight: static (x, y) => 10d,
            vectorFeatures:
            [
                VectorPolygon("lakeA", 100d, 100d, 100d, 100d),
                VectorPolygon("lakeB", 150d, 150d, 100d, 100d),
            ],
            featureMappings:
            [
                new FeatureMappingRule("vectors", ManifestFeatureTarget.Water, Geometry: ManifestFeatureGeometry.Polygon),
                new FeatureMappingRule("vectors", ManifestFeatureTarget.Water, Geometry: ManifestFeatureGeometry.Polygon),
            ]);
        using var stage = StageFixture.Create();
        var diagnostics = new GeoDiagnosticBag();

        var output = ImportChunkGeneration.Run(plan!, stage.Files, stage.Stage, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(output);
        var masks = output!.Masks;

        // Flat terrain flags nothing by slope. Both water rules match both lakes (region
        // rules retain every match), so each rule counts every covered cell: 2 rules x
        // 2 lakes x 121 cells = 484 applications over 206 unique cells.
        Assert.Equal(0, masks.SlopeImpassableCellCount);
        Assert.Equal(0, masks.SlopeUnbuildableCellCount);
        Assert.Equal(484, masks.WaterRuleApplicationCount);
        Assert.Equal(0, masks.PassabilityRuleApplicationCount);
        Assert.Equal(0, masks.BuildabilityRuleApplicationCount);

        // The extent is 1279 m wide inside a 1280 m chunk, so core sample 128 on each
        // axis lies outside the playable area: 129 x 129 - 128 x 128 = 257 forced cells.
        Assert.Equal(257, masks.PlayableForcedCellCount);
        Assert.Equal(206 + 257, masks.ImpassableCellCount);
        Assert.Equal(206 + 257, masks.UnbuildableCellCount);
        Assert.Equal(45d, masks.ImpassableThresholdDegrees);
        Assert.Equal(15d, masks.UnbuildableThresholdDegrees);
        Assert.Equal(2d, masks.VerticalExaggeration);

        var mask = PayloadChunkCodec.DecodeMask(0, 0, ReadStaged(stage.Stage, "masks/0_0.bin"), diagnostics);
        Assert.NotNull(mask);
        var both = TerrainMaskFlags.Impassable | TerrainMaskFlags.Unbuildable;
        Assert.Equal(both, mask!.GetFlags(15, 15));
        Assert.Equal(both, mask.GetFlags(128, 0));
        Assert.Equal(TerrainMaskFlags.None, mask.GetFlags(64, 64));
    }

    [Fact]
    public void Run_TwoByTwoGridSeamsValidateAndRunsAreDeterministic()
    {
        var (plan, _) = TryPlan(chunksX: 2, chunksY: 2);
        using var first = StageFixture.Create();
        using var second = StageFixture.Create();
        var diagnostics = new GeoDiagnosticBag();

        var output = ImportChunkGeneration.Run(plan!, first.Files, first.Stage, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(output);
        Assert.Equal(4, output!.Metadata.Chunks.Count);
        Assert.Equal([(0, 0), (1, 0), (0, 1), (1, 1)], output.Metadata.Chunks.Select(static chunk => (chunk.ChunkX, chunk.ChunkY)));

        // Adjacent staged chunks agree bitwise on their shared seams.
        var bag = new GeoDiagnosticBag();
        Assert.True(PayloadChunkCodec.ValidateTerrainHorizontalSeam(TerrainOf(first.Stage, 0, 0), TerrainOf(first.Stage, 1, 0), bag));
        Assert.True(PayloadChunkCodec.ValidateTerrainVerticalSeam(TerrainOf(first.Stage, 0, 0), TerrainOf(first.Stage, 0, 1), bag));
        Assert.True(PayloadChunkCodec.ValidateMaterialHorizontalSeam(MaterialOf(first.Stage, plan!.Palette, 0, 0), MaterialOf(first.Stage, plan.Palette, 1, 0), bag));
        Assert.True(PayloadChunkCodec.ValidateMaskHorizontalSeam(MaskOf(first.Stage, 0, 0), MaskOf(first.Stage, 1, 0), bag));
        Assert.False(bag.HasErrors);

        // A second run over identical inputs produces byte-identical chunk files.
        var repeatDiagnostics = new GeoDiagnosticBag();
        var repeat = ImportChunkGeneration.Run(plan, second.Files, second.Stage, repeatDiagnostics);
        Assert.False(repeatDiagnostics.HasErrors);
        Assert.NotNull(repeat);
        Assert.Equal(
            output.Metadata.Chunks.SelectMany(ChunkHashes),
            repeat!.Metadata.Chunks.SelectMany(ChunkHashes));
    }

    [Fact]
    public void Run_MoreChunksThanTheCacheCapacity_ResolvesTerrainZ()
    {
        var waypoints = new List<GeospatialFeature>();
        for (var cy = 0; cy < 3; cy++)
        {
            for (var cx = 0; cx < 2; cx++)
            {
                var x = (cx * ChunkLayout.ChunkSpanGameUnits) + 640d;
                var y = (cy * ChunkLayout.ChunkSpanGameUnits) + 640d;
                waypoints.Add(VectorPoint($"wp-{cx}-{cy}", x, y));
            }
        }

        var (plan, _) = TryPlan(
            chunksX: 2,
            chunksY: 3,
            vectorFeatures: waypoints,
            featureMappings: [new FeatureMappingRule("vectors", ManifestFeatureTarget.Waypoint, Geometry: ManifestFeatureGeometry.Point)]);
        using var stage = StageFixture.Create();
        var diagnostics = new GeoDiagnosticBag();

        var output = ImportChunkGeneration.Run(plan!, stage.Files, stage.Stage, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(output);

        // Six waypoints span six chunks; the four-entry cache evicts and re-reads staged
        // terrain while every Z stays the exact exaggerated plane value.
        Assert.Equal(6, output!.Features.TotalCount);
        foreach (var feature in output.Metadata.Features)
        {
            var position = feature.Position!.Value;
            Assert.Equal(2d * PlaneHeight(position.X, position.Y), position.Z, 3);
        }

        // No imagery layer in this import: summaries and descriptors stay null.
        Assert.Null(output.Imagery);
        Assert.All(output.Metadata.Chunks, static chunk => Assert.Null(chunk.Imagery));
        Assert.Null(output.Metadata.Grid.ImageryPixelsPerSample);
        Assert.Equal(0, output.ClassificationSampling.SampleCount);
    }

    [Fact]
    public void Run_DescriptorMetadataBudgetTinyCap_FailsTypedBeforeAnyFile()
    {
        var (plan, _) = TryPlan();
        using var stage = StageFixture.Create(new GeospatialImportLimits { MaxPayloadMetadataBytes = 191 });
        var diagnostics = new GeoDiagnosticBag();

        var output = ImportChunkGeneration.Run(plan!, stage.Files, stage.Stage, diagnostics, stage.Limits);

        Assert.Null(output);
        var diagnostic = Assert.Single(diagnostics.Items, static item => item.Severity == GeoSeverity.Error);
        Assert.Equal(ImportChunkGenerationDiagnostics.ChunkMetadataBudgetExceededCode, diagnostic.Code);
        Assert.Equal(GeoStage.Generate, diagnostic.Stage);
        Assert.Empty(Directory.EnumerateFileSystemEntries(stage.Stage));
    }

    [Fact]
    public void Run_DemCoverageMissingFailsTypedWithoutFallbackFill()
    {
        // The raster covers only the core rectangle [0, 1280]; the southern apron sample
        // at (-10, -10) lies outside and the default Fail edge policy must reject it
        // instead of filling it.
        var transform = new AffineTransform(0d, 10d, 0d, 1280d, 0d, -10d);
        var coreOnly = new GeospatialRaster<double>(
            128,
            128,
            1,
            transform,
            PlanTestSupport.ExtentCrs,
            Enumerable.Repeat(10d, 128 * 128).ToArray(),
            sampleFormat: RasterSampleFormat.Float64);
        var (plan, _) = TryPlan(demRaster: coreOnly);
        using var stage = StageFixture.Create();
        var diagnostics = new GeoDiagnosticBag();

        var output = ImportChunkGeneration.Run(plan!, stage.Files, stage.Stage, diagnostics);

        Assert.Null(output);
        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics.Items, static item => item.Code == SamplingDiagnosticCodes.EdgePolicyFailure);
        Assert.False(File.Exists(StagePath(stage.Stage, "terrain/0_0.bin")));
    }

    [Fact]
    public void Run_CancelledToken_Throws()
    {
        var (plan, _) = TryPlan();
        using var stage = StageFixture.Create();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => ImportChunkGeneration.Run(plan!, stage.Files, stage.Stage, new GeoDiagnosticBag(), cancellationToken: cancelled.Token));
    }

    [Fact]
    public void Cache_MutatedAfterCacheHit_FailsTypedClosed()
    {
        using var stage = StageFixture.Create();
        var (references, _) = WriteFiveTerrainChunks(stage);
        var diagnostics = new GeoDiagnosticBag();
        var cache = new StagedTerrainChunkCache(stage.Stage, 5, 1, references, diagnostics, CancellationToken.None);

        // Prime the cache, then mutate the written bytes: the cached decode of the old
        // bytes must not be served without re-verification.
        Assert.NotNull(cache.Resolve(0, 0));
        Assert.False(diagnostics.HasErrors);
        var path = StagePath(stage.Stage, "terrain/0_0.bin");
        var mutated = ReadStaged(stage.Stage, "terrain/0_0.bin");
        mutated[0] ^= 0x1;
        File.WriteAllBytes(path, mutated);

        Assert.Null(cache.Resolve(0, 0));
        Assert.True(diagnostics.HasErrors);
        Assert.Contains(
            diagnostics.Items,
            static item => item.Code == ImportChunkGenerationDiagnostics.StagedTerrainCacheMutatedCode);
    }

    [Fact]
    public void Cache_SymlinkSwappedAfterCacheHit_FailsClosed()
    {
        using var stage = StageFixture.Create();
        var (references, chunks) = WriteFiveTerrainChunks(stage);
        var diagnostics = new GeoDiagnosticBag();
        var cache = new StagedTerrainChunkCache(stage.Stage, 5, 1, references, diagnostics, CancellationToken.None);
        Assert.NotNull(cache.Resolve(0, 0));

        // The link target holds the valid written bytes, so only the no-follow link
        // check can reject the swapped entry.
        var target = Path.Combine(Path.GetTempPath(), "panopticon-cache-target-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(target, PayloadChunkCodec.EncodeTerrain(chunks[0]));
        var path = StagePath(stage.Stage, "terrain/0_0.bin");
        File.Delete(path);
        File.CreateSymbolicLink(path, target);

        try
        {
            Assert.Null(cache.Resolve(0, 0));
            Assert.True(diagnostics.HasErrors);
            Assert.Contains(
                diagnostics.Items,
                static item => item.Code == ImportChunkGenerationDiagnostics.StagedTerrainCacheInvalidCode);
        }
        finally
        {
            File.Delete(target);
        }
    }

    [Fact]
    public void Cache_MissingAfterCacheHit_FailsClosed()
    {
        using var stage = StageFixture.Create();
        var (references, _) = WriteFiveTerrainChunks(stage);
        var diagnostics = new GeoDiagnosticBag();
        var cache = new StagedTerrainChunkCache(stage.Stage, 5, 1, references, diagnostics, CancellationToken.None);
        Assert.NotNull(cache.Resolve(2, 0));
        File.Delete(StagePath(stage.Stage, "terrain/2_0.bin"));

        Assert.Null(cache.Resolve(2, 0));
        Assert.True(diagnostics.HasErrors);
        Assert.Contains(
            diagnostics.Items,
            static item => item.Code == ImportChunkGenerationDiagnostics.StagedTerrainCacheInvalidCode);
    }

    [Fact]
    public void Cache_OversizedOrShortStagedTerrainFile_FailsTyped()
    {
        using var stage = StageFixture.Create();
        var (references, _) = WriteFiveTerrainChunks(stage);
        var diagnostics = new GeoDiagnosticBag();
        var cache = new StagedTerrainChunkCache(stage.Stage, 5, 1, references, diagnostics, CancellationToken.None);
        Assert.NotNull(cache.Resolve(1, 0));

        var path = StagePath(stage.Stage, "terrain/1_0.bin");
        var written = ReadStaged(stage.Stage, "terrain/1_0.bin");

        // Extra trailing bytes: the fixed-length read plus the one-byte probe rejects
        // the excess without allocating from the declared length.
        File.WriteAllBytes(path, [.. written, 0x41]);
        Assert.Null(cache.Resolve(1, 0));
        Assert.Contains(
            diagnostics.Items,
            static item => item.Code == ImportChunkGenerationDiagnostics.StagedTerrainCacheMutatedCode);

        // A truncated file fails from the actual short read.
        File.WriteAllBytes(path, written[..100]);
        Assert.Null(cache.Resolve(1, 0));
        Assert.True(diagnostics.HasErrors);
    }

    [Fact]
    public void Cache_ResolvesBeyondCapacityWithFreshReads()
    {
        using var stage = StageFixture.Create();
        var (references, chunks) = WriteFiveTerrainChunks(stage);
        var diagnostics = new GeoDiagnosticBag();
        var cache = new StagedTerrainChunkCache(stage.Stage, 5, 1, references, diagnostics, CancellationToken.None);

        // Filling and overflowing the four-entry capacity, then re-resolving evicted
        // chunks, must re-read and verify the staged files every time.
        for (var i = 0; i < 5; i++)
        {
            var chunk = cache.Resolve(i, 0);
            Assert.NotNull(chunk);
            Assert.Equal(i, chunk!.ChunkX);
            Assert.Equal(chunks[i].ContentSha256Hex, chunk.ContentSha256Hex);
        }

        for (var i = 4; i >= 0; i--)
        {
            var chunk = cache.Resolve(i, 0);
            Assert.NotNull(chunk);
            Assert.Equal(chunks[i].ContentSha256Hex, chunk!.ContentSha256Hex);
        }

        Assert.False(diagnostics.HasErrors);
    }

    private static (PayloadFileReference[] References, TerrainChunk[] Chunks) WriteFiveTerrainChunks(StageFixture stage)
    {
        var references = new PayloadFileReference[5];
        var chunks = new TerrainChunk[5];
        var axis = ChunkLayout.StoredPointsPerChunkAxis;

        for (var i = 0; i < 5; i++)
        {
            var heights = new float[axis * axis];

            for (var y = 0; y < axis; y++)
            {
                for (var x = 0; x < axis; x++)
                {
                    heights[(y * axis) + x] = (x + (2 * y) + (7 * i)) * 0.25f;
                }
            }

            chunks[i] = new TerrainChunk(i, 0, heights);
            var reference = stage.Files.WriteTerrain(chunks[i]);
            Assert.NotNull(reference);
            references[i] = reference!;
        }

        return (references, chunks);
    }

    private static Rgba32 PixelAt(string stageDirectory, int chunkX, int chunkY, int px, int py)
    {
        using var image = Image.Load<Rgba32>(ReadStaged(stageDirectory, PayloadChunkPaths.Imagery(chunkX, chunkY)));

        return image[px, py];
    }

    private static TerrainChunk TerrainOf(string stageDirectory, int chunkX, int chunkY)
    {
        var chunk = PayloadChunkCodec.DecodeTerrain(chunkX, chunkY, ReadStaged(stageDirectory, PayloadChunkPaths.Terrain(chunkX, chunkY)));
        Assert.NotNull(chunk);

        return chunk!;
    }

    private static MaterialChunk MaterialOf(string stageDirectory, MaterialPalette palette, int chunkX, int chunkY)
    {
        var chunk = PayloadChunkCodec.DecodeMaterial(chunkX, chunkY, ReadStaged(stageDirectory, PayloadChunkPaths.Materials(chunkX, chunkY)), palette);
        Assert.NotNull(chunk);

        return chunk!;
    }

    private static TerrainMaskChunk MaskOf(string stageDirectory, int chunkX, int chunkY)
    {
        var chunk = PayloadChunkCodec.DecodeMask(chunkX, chunkY, ReadStaged(stageDirectory, PayloadChunkPaths.Masks(chunkX, chunkY)));
        Assert.NotNull(chunk);

        return chunk!;
    }

    private static long KernelCount(PayloadSamplingSummary summary, SampleKernel kernel)
    {
        return summary.KernelCounts.Single(entry => entry.Kernel == kernel).Count;
    }

    private static long KindCount(PayloadFeatureGenerationSummary summary, GeneratedFeatureKind kind)
    {
        return summary.CountsByKind.Single(entry => entry.Kind == kind).Count;
    }

    private static IEnumerable<string> ChunkHashes(PayloadChunkDescriptor descriptor)
    {
        yield return descriptor.Terrain.Sha256Hex;
        yield return descriptor.Materials.Sha256Hex;
        yield return descriptor.Masks.Sha256Hex;

        if (descriptor.Imagery is { } imagery)
        {
            yield return imagery.Sha256Hex;
        }
    }
}
