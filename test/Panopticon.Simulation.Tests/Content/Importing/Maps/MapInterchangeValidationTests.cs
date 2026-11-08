using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Panopticon.Content.Importing.Maps;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Maps;

/// <summary>
/// WI-H2-02: model-level validation of MapInterchange per the H2 deep dive check list. The
/// synthetic 2x1 model is pure-unit and boots no game files. Stored samples are filled from one
/// shared lattice value function so seams and aprons agree by construction.
/// </summary>
public sealed class MapInterchangeValidationTests
{
    [Fact]
    public void ValidSyntheticModelPasses()
    {
        Assert.Empty(MapInterchangeValidator.Validate(BuildValidModel()));
    }

    [Fact]
    public void WrongSchemaVersionFails()
    {
        AssertOnlyCode(BuildValidModel() with { SchemaVersion = MapInterchange.CurrentSchemaVersion + 1 }, MapInterchangeDiagnostic.SchemaVersion);
    }

    [Fact]
    public void MissingChunkFails()
    {
        var model = BuildValidModel();
        var layout = model.Layout with { Chunks = [model.Layout.Chunks[0]] };

        AssertOnlyCode(model with { Layout = layout }, MapInterchangeDiagnostic.ChunkInventory);
    }

    [Fact]
    public void ExtraChunkFails()
    {
        var model = BuildValidModel();
        var layout = model.Layout with { Chunks = [.. model.Layout.Chunks, BuildChunk(2, 0)] };

        AssertOnlyCode(model with { Layout = layout }, MapInterchangeDiagnostic.ChunkInventory);
    }

    [Fact]
    public void TruncatedHeightLayerFails()
    {
        var chunk = MutateHeights(BuildValidModel().Layout.Chunks[0], samples => [.. samples.Take(MapChunkGeometry.StoredHeightSamplesPerChunk - 1)]);

        AssertOnlyCode(WithChunk(BuildValidModel(), 0, chunk), MapInterchangeDiagnostic.HeightLength);
    }

    [Fact]
    public void NonFiniteHeightFails()
    {
        // Interior core point (i=5, j=5): untouched by seam and apron comparisons.
        var chunk = MutateStoredSample(BuildValidModel().Layout.Chunks[0], StoredRow(5), StoredColumn(5), float.NaN);

        AssertOnlyCode(WithChunk(BuildValidModel(), 0, chunk), MapInterchangeDiagnostic.HeightFinite);
    }

    [Fact]
    public void TruncatedMaterialLayerFails()
    {
        var chunk = BuildValidModel().Layout.Chunks[0] with
        {
            Materials = new MaterialLayer(new byte[MapChunkGeometry.CoreCellsPerChunk - 1]),
        };

        AssertOnlyCode(WithChunk(BuildValidModel(), 0, chunk), MapInterchangeDiagnostic.MaterialLength);
    }

    [Fact]
    public void TruncatedMaskLayerFails()
    {
        var chunk = BuildValidModel().Layout.Chunks[0] with
        {
            Masks = new MaskLayer(new byte[MapChunkGeometry.CoreCellsPerChunk - 1]),
        };

        AssertOnlyCode(WithChunk(BuildValidModel(), 0, chunk), MapInterchangeDiagnostic.MaskLength);
    }

    [Fact]
    public void UnknownMaskBitFails()
    {
        var flags = new byte[MapChunkGeometry.CoreCellsPerChunk];
        flags[100] = 0x80;
        var chunk = BuildValidModel().Layout.Chunks[0] with { Masks = new MaskLayer(flags) };

        AssertOnlyCode(WithChunk(BuildValidModel(), 0, chunk), MapInterchangeDiagnostic.MaskBits);
    }

    [Fact]
    public void OversizedPaletteFails()
    {
        var palette = Enumerable.Range(0, 257)
            .Select(i => new MaterialPaletteEntry((byte)(i % 256), $"material-{i}", new AssetReference("terrain", $"material-{i}")))
            .ToArray();
        var blend = new BlendModel(palette);

        AssertOnlyCode(BuildValidModel() with { Blend = blend }, MapInterchangeDiagnostic.PaletteCount);
    }

    [Fact]
    public void MaterialIndexOutsidePaletteFails()
    {
        var indices = new byte[MapChunkGeometry.CoreCellsPerChunk];
        indices[42] = 5;
        var chunk = BuildValidModel().Layout.Chunks[1] with { Materials = new MaterialLayer(indices) };

        AssertOnlyCode(WithChunk(BuildValidModel(), 1, chunk), MapInterchangeDiagnostic.PaletteIndex);
    }

    [Fact]
    public void DuplicatePaletteIndexFails()
    {
        var blend = new BlendModel(
        [
            new MaterialPaletteEntry(0, "grass", new AssetReference("terrain", "grass")),
            new MaterialPaletteEntry(0, "rock", new AssetReference("terrain", "rock")),
        ]);

        AssertOnlyCode(BuildValidModel() with { Blend = blend }, MapInterchangeDiagnostic.PaletteDuplicateIndex);
    }

    [Fact]
    public void PartialImageryCoverageFails()
    {
        var chunk = BuildValidModel().Layout.Chunks[0] with { Imagery = BuildImagery(4, 4) };

        AssertOnlyCode(WithChunk(BuildValidModel(), 0, chunk), MapInterchangeDiagnostic.ImageryConsistency);
    }

    [Fact]
    public void ImageryPixelCountMismatchFails()
    {
        var mismatched = new ImageryLayer(2, 2, new byte[15]);
        var first = WithChunk(BuildValidModel(), 0, BuildValidModel().Layout.Chunks[0] with { Imagery = BuildImagery(2, 2) });
        var model = WithChunk(first, 1, first.Layout.Chunks[1] with { Imagery = mismatched });

        AssertOnlyCode(model, MapInterchangeDiagnostic.ImageryConsistency);
    }

    [Fact]
    public void DuplicateStableIdAcrossSetsFails()
    {
        var model = BuildValidModel();
        var waypoints = new WaypointSet([model.Waypoints!.Waypoints[0] with { StableId = "object-1" }]);

        AssertOnlyCode(model with { Waypoints = waypoints }, MapInterchangeDiagnostic.StableIdDuplicate);
    }

    [Fact]
    public void UnknownRetainedSectionNameFails()
    {
        var model = BuildValidModel();
        var retained = WithSection(model.RetainedSections[0], name: "NotALegacySection");

        AssertOnlyCode(model with { RetainedSections = [retained] }, MapInterchangeDiagnostic.RetainedName);
    }

    [Fact]
    public void NegativeRetainedSectionVersionFails()
    {
        var model = BuildValidModel();
        var retained = WithSection(model.RetainedSections[0], version: -1);

        AssertOnlyCode(model with { RetainedSections = [retained] }, MapInterchangeDiagnostic.RetainedVersion);
    }

    [Fact]
    public void RetainedSectionDigestMismatchFails()
    {
        var model = BuildValidModel();
        var retained = WithSection(model.RetainedSections[0], sha256: "deadbeef");

        AssertOnlyCode(model with { RetainedSections = [retained] }, MapInterchangeDiagnostic.RetainedDigest);
    }

    [Fact]
    public void EmptyRetainedSectionBytesFail()
    {
        var model = BuildValidModel();
        var retained = WithSection(model.RetainedSections[0], bytes: ReadOnlyMemory<byte>.Empty, sha256: Sha256Hex([]));

        AssertOnlyCode(model with { RetainedSections = [retained] }, MapInterchangeDiagnostic.RetainedDigest);
    }

    [Fact]
    public void BrokenHorizontalSeamFails()
    {
        // Perturbs west chunk core column 128 (stored column 129) at core row 10. No apron sample
        // references that point (the east chunk's west apron is core column 127), so only the seam fires.
        var chunk = MutateStoredSample(BuildValidModel().Layout.Chunks[0], StoredRow(10), StoredColumn(MapChunkGeometry.CorePointsPerAxis - 1), 999.5f);

        AssertOnlyCode(WithChunk(BuildValidModel(), 0, chunk), MapInterchangeDiagnostic.SeamEquality);
    }

    [Fact]
    public void BrokenEastApronFails()
    {
        // Perturbs west chunk east apron (stored column 130) at core row 10: one stored sample.
        var chunk = MutateStoredSample(BuildValidModel().Layout.Chunks[0], StoredRow(10), MapChunkGeometry.StoredPointsPerAxis - 1, 777.25f);

        AssertOnlyCode(WithChunk(BuildValidModel(), 0, chunk), MapInterchangeDiagnostic.ApronAgreement);
    }

    [Fact]
    public void BrokenWestApronFails()
    {
        // Perturbs east chunk west apron (stored column 0) at core row 10: one stored sample.
        var chunk = MutateStoredSample(BuildValidModel().Layout.Chunks[1], StoredRow(10), 0, 777.25f);

        AssertOnlyCode(WithChunk(BuildValidModel(), 1, chunk), MapInterchangeDiagnostic.ApronAgreement);
    }

    [Fact]
    public void ValidatePreservesSourceQuantities()
    {
        var model = BuildValidModel();
        var spacing = model.Layout.SampleSpacing;

        Assert.Empty(MapInterchangeValidator.Validate(model));

        Assert.Equal(new MapQuantity(10, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy), model.Layout.SampleSpacing);
        Assert.Equal(spacing, model.Layout.SampleSpacing);
        Assert.Equal(10, model.Objects!.Objects[0].X.Value);
    }

    [Fact]
    public void ConstructionDefensivelyCopiesCallerOwnedState()
    {
        var heights = BuildSampleArray(0, 0);
        var materials = new byte[MapChunkGeometry.CoreCellsPerChunk];
        var masks = new byte[MapChunkGeometry.CoreCellsPerChunk];
        var palette = new List<MaterialPaletteEntry>
        {
            new(0, "grass", new AssetReference("terrain", "grass")),
            new(1, "rock", new AssetReference("terrain", "rock")),
        };
        var legacyProperties = new Dictionary<string, string> { ["team"] = "america" };
        var bytes = new byte[] { 1, 2, 3, 4 };

        var chunk = new Chunk(new ChunkKey(0, 0), new HeightLayer(heights), new MaterialLayer(materials), new MaskLayer(masks), null);
        var placed = new PlacedObject(
            "object-1",
            new MapQuantity(10, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
            new MapQuantity(20, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
            new MapQuantity(0, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalZ),
            0.0,
            new AssetReference("units", "infantry"),
            legacyProperties);

        var model = BuildValidModel();
        model = WithChunk(model, 0, chunk);
        model = model with { Blend = new BlendModel(palette), Objects = new PlacedObjectSet([placed]) };
        model = model with { RetainedSections = [new RetainedSection("GlobalVersion", 8, bytes, Sha256Hex(bytes))] };

        Assert.Empty(MapInterchangeValidator.Validate(model));

        heights[0] = 1234.5f;
        palette.Add(new MaterialPaletteEntry(9, "late", new AssetReference("terrain", "late")));
        legacyProperties["team"] = "mutated";
        bytes[0] = 200;

        Assert.Empty(MapInterchangeValidator.Validate(model));
        Assert.Equal(BuildSampleArray(0, 0)[0], model.Layout.Chunks[0].Heights.Samples[0]);
        Assert.Equal(2, model.Blend.Palette.Count);
        Assert.Equal("america", model.Objects!.Objects[0].LegacyProperties["team"]);
        Assert.Equal(1, model.RetainedSections[0].Bytes.Span[0]);
    }

    [Fact]
    public void NullBlendIsReportedWithoutThrowing()
    {
        AssertOnlyCode(BuildValidModel() with { Blend = null! }, MapInterchangeDiagnostic.NullComponent);
    }

    [Fact]
    public void NullLayoutIsReportedWithoutThrowing()
    {
        AssertOnlyCode(BuildValidModel() with { Layout = null! }, MapInterchangeDiagnostic.NullComponent);
    }

    [Fact]
    public void HugeDeclaredGridCompletesWithInventoryDiagnostic()
    {
        var model = BuildValidModel();
        var layout = model.Layout with { ChunksX = int.MaxValue };

        AssertOnlyCode(model with { Layout = layout }, MapInterchangeDiagnostic.ChunkInventory);
    }

    [Fact]
    public void NanSpacingFails()
    {
        var model = BuildValidModel();
        var layout = model.Layout with { SampleSpacing = new MapQuantity(double.NaN, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy) };

        AssertOnlyCode(model with { Layout = layout }, MapInterchangeDiagnostic.QuantityFinite);
    }

    [Fact]
    public void ZeroSpacingFails()
    {
        var model = BuildValidModel();
        var layout = model.Layout with { SampleSpacing = new MapQuantity(0, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy) };

        AssertOnlyCode(model with { Layout = layout }, MapInterchangeDiagnostic.QuantityFinite);
    }

    [Fact]
    public void NanWaypointZFails()
    {
        var model = BuildValidModel();
        var waypoints = new WaypointSet(
        [
            model.Waypoints!.Waypoints[0] with { Z = new MapQuantity(double.NaN, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalZ) },
        ]);

        AssertOnlyCode(model with { Waypoints = waypoints }, MapInterchangeDiagnostic.QuantityFinite);
    }

    [Fact]
    public void ZeroMetersPerGameUnitFails()
    {
        var model = BuildValidModel();
        var layout = model.Layout with { GeospatialTransform = model.Layout.GeospatialTransform with { MetersPerGameUnit = 0 } };

        AssertOnlyCode(model with { Layout = layout }, MapInterchangeDiagnostic.QuantityFinite);
    }

    [Fact]
    public void SpacingFrameOutsideMapLocalXyFails()
    {
        var model = BuildValidModel();
        var layout = model.Layout with { SampleSpacing = new MapQuantity(10, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineXy) };

        AssertOnlyCode(model with { Layout = layout }, MapInterchangeDiagnostic.CoordinateFrame);
    }

    [Fact]
    public void WaypointMixedHorizontalFramesFail()
    {
        var model = BuildValidModel();
        var waypoints = new WaypointSet(
        [
            model.Waypoints!.Waypoints[0] with { Y = new MapQuantity(25, MapQuantityUnit.Meters, MapQuantityFrame.SourceCrs) },
        ]);

        AssertOnlyCode(model with { Waypoints = waypoints }, MapInterchangeDiagnostic.CoordinateFrame);
    }

    [Fact]
    public void WaypointZFrameOutsideVerticalFails()
    {
        var model = BuildValidModel();
        var waypoints = new WaypointSet(
        [
            model.Waypoints!.Waypoints[0] with { Z = new MapQuantity(0, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy) },
        ]);

        AssertOnlyCode(model with { Waypoints = waypoints }, MapInterchangeDiagnostic.CoordinateFrame);
    }

    [Fact]
    public void MapInterchangeCopiesRetainedSectionsAtConstruction()
    {
        var model = BuildValidModel();
        var sections = new List<RetainedSection> { model.RetainedSections[0] };
        var copied = new MapInterchange(
            model.SchemaVersion,
            model.Identity,
            model.Layout,
            model.Blend,
            model.Objects,
            model.Waypoints,
            model.Provenance,
            model.Report,
            sections);

        sections.Clear();

        Assert.Single(copied.RetainedSections);
        Assert.Empty(MapInterchangeValidator.Validate(copied));
    }

    [Fact]
    public void TerrainLayoutCopiesChunksAtConstruction()
    {
        var model = BuildValidModel();
        var chunks = new List<Chunk>(model.Layout.Chunks);
        var layout = new TerrainLayout(
            model.Layout.SampleSpacing,
            model.Layout.ChunksX,
            model.Layout.ChunksY,
            model.Layout.Border,
            model.Layout.GeospatialTransform,
            model.Layout.NodataPolicy,
            chunks);
        var result = model with { Layout = layout };

        chunks.Clear();

        Assert.Equal(2, result.Layout.Chunks.Count);
        Assert.Empty(MapInterchangeValidator.Validate(result));
    }

    [Fact]
    public void GeospatialOriginsInDeclaredFramesPass()
    {
        var model = BuildValidModel();
        var transform = model.Layout.GeospatialTransform with
        {
            OriginEasting = new MapQuantity(500000, MapQuantityUnit.Meters, MapQuantityFrame.SourceCrs),
            OriginNorthing = new MapQuantity(-12.5, MapQuantityUnit.Degrees, MapQuantityFrame.SourceCrs),
            VerticalOrigin = new MapQuantity(0, MapQuantityUnit.Meters, MapQuantityFrame.SourceVertical),
        };
        var layout = model.Layout with { GeospatialTransform = transform };
        var sources = new[] { new SourceRecord("dem", [], new MapQuantity(3.5, MapQuantityUnit.Degrees, MapQuantityFrame.SourceCrs), null) };
        var provenance = new Provenance(null, sources, model.Provenance.GeneratedFeatures);

        Assert.Empty(MapInterchangeValidator.Validate(model with { Layout = layout, Provenance = provenance }));
    }

    [Fact]
    public void NanTransformOriginFails()
    {
        var model = BuildValidModel();
        var transform = model.Layout.GeospatialTransform with
        {
            OriginEasting = new MapQuantity(double.NaN, MapQuantityUnit.Meters, MapQuantityFrame.SourceCrs),
        };
        var layout = model.Layout with { GeospatialTransform = transform };

        AssertOnlyCode(model with { Layout = layout }, MapInterchangeDiagnostic.QuantityFinite);
    }

    [Fact]
    public void DegreesPositionInMapLocalXyFails()
    {
        var model = BuildValidModel();
        var waypoints = new WaypointSet(
        [
            model.Waypoints!.Waypoints[0] with { X = new MapQuantity(15, MapQuantityUnit.Degrees, MapQuantityFrame.MapLocalXy) },
        ]);

        AssertOnlyCode(model with { Waypoints = waypoints }, MapInterchangeDiagnostic.UnitFrame);
    }

    [Fact]
    public void MetersPositionInMapLocalXyFails()
    {
        var model = BuildValidModel();
        var waypoints = new WaypointSet(
        [
            model.Waypoints!.Waypoints[0] with { Y = new MapQuantity(25, MapQuantityUnit.Meters, MapQuantityFrame.MapLocalXy) },
        ]);

        AssertOnlyCode(model with { Waypoints = waypoints }, MapInterchangeDiagnostic.UnitFrame);
    }

    [Fact]
    public void GameUnitSourceCrsOriginFails()
    {
        var model = BuildValidModel();

        AssertOnlyCode(WithSourceOrigin(model, new MapQuantity(3.5, MapQuantityUnit.GameUnit, MapQuantityFrame.SourceCrs)), MapInterchangeDiagnostic.UnitFrame);
    }

    [Fact]
    public void SourceRecordOriginWrongFrameFails()
    {
        var model = BuildValidModel();

        AssertOnlyCode(WithSourceOrigin(model, new MapQuantity(3.5, MapQuantityUnit.Meters, MapQuantityFrame.LegacyEngineXy)), MapInterchangeDiagnostic.UnitFrame);
    }

    [Fact]
    public void EmptyImageryDimensionsFail()
    {
        var model = BuildValidModel();
        var first = WithChunk(model, 0, model.Layout.Chunks[0] with { Imagery = BuildImagery(0, 0) });
        var second = WithChunk(first, 1, first.Layout.Chunks[1] with { Imagery = BuildImagery(0, 0) });

        AssertOnlyCode(second, MapInterchangeDiagnostic.ImageryConsistency);
    }

    [Fact]
    public void OverflowingImageryDimensionsFail()
    {
        var chunk = BuildValidModel().Layout.Chunks[0] with { Imagery = new ImageryLayer(65536, 65536, new byte[16]) };

        AssertOnlyCode(WithChunk(BuildValidModel(), 0, chunk), MapInterchangeDiagnostic.ImageryConsistency);
    }

    private static MapInterchange WithSourceOrigin(MapInterchange model, MapQuantity originX)
    {
        var sources = new[] { new SourceRecord("dem", [], originX, null) };

        return model with { Provenance = new Provenance(null, sources, model.Provenance.GeneratedFeatures) };
    }

    private static void AssertOnlyCode(MapInterchange model, string expected)
    {
        Assert.Equal([expected], Codes(model));
    }

    private static string[] Codes(MapInterchange model)
    {
        return MapInterchangeValidator.Validate(model).Select(d => d.Code).Distinct().ToArray();
    }

    private static int StoredRow(int coreJ) => coreJ + 1;

    private static int StoredColumn(int coreI) => coreI + 1;

    private static Chunk MutateStoredSample(Chunk chunk, int row, int column, float value)
    {
        var samples = chunk.Heights.Samples.ToArray();
        samples[(row * MapChunkGeometry.StoredPointsPerAxis) + column] = value;

        return chunk with { Heights = new HeightLayer(samples) };
    }

    private static Chunk MutateHeights(Chunk chunk, Func<IReadOnlyList<float>, IReadOnlyList<float>> mutate)
    {
        return chunk with { Heights = new HeightLayer(mutate(chunk.Heights.Samples)) };
    }

    private static MapInterchange WithChunk(MapInterchange model, int index, Chunk chunk)
    {
        var chunks = model.Layout.Chunks.ToArray();
        chunks[index] = chunk;

        return model with { Layout = model.Layout with { Chunks = chunks } };
    }

    private static RetainedSection WithSection(RetainedSection section, string? name = null, int? version = null, ReadOnlyMemory<byte>? bytes = null, string? sha256 = null)
    {
        return new RetainedSection(name ?? section.SectionName, version ?? section.SectionVersion, bytes ?? section.Bytes, sha256 ?? section.Sha256);
    }

    private static ImageryLayer BuildImagery(int width, int height)
    {
        return new ImageryLayer(width, height, new byte[width * height * 4]);
    }

    private static string Sha256Hex(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private const int IntervalsPerChunkAxis = MapChunkGeometry.CorePointsPerAxis - 1;

    private const int StoredAxis = MapChunkGeometry.StoredPointsPerAxis;

    private const int ChunksX = 2;

    private const int ChunksY = 1;

    private static MapInterchange BuildValidModel()
    {
        var layout = new TerrainLayout(
            new MapQuantity(10, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
            ChunksX,
            ChunksY,
            0,
            new GeospatialTransform(null, null, null, 1.0, null, null, null),
            MapNodataPolicy.Fail,
            [BuildChunk(0, 0), BuildChunk(1, 0)]);

        var blend = new BlendModel(
        [
            new MaterialPaletteEntry(0, "grass", new AssetReference("terrain", "grass")),
            new MaterialPaletteEntry(1, "rock", new AssetReference("terrain", "rock")),
        ]);

        var objects = new PlacedObjectSet(
        [
            new PlacedObject(
                "object-1",
                new MapQuantity(10, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
                new MapQuantity(20, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
                new MapQuantity(0, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalZ),
                0.0,
                new AssetReference("units", "infantry"),
                new Dictionary<string, string>()),
        ]);

        var waypoints = new WaypointSet(
        [
            new Waypoint(
                "waypoint-1",
                "start",
                new MapQuantity(15, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
                new MapQuantity(25, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
                new MapQuantity(0, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalZ)),
        ]);

        var provenance = new Provenance(
            null,
            [],
            [new GeneratedFeature("feature-1", "lake", new Dictionary<string, string>())]);

        var report = new ImportReport([], [], 0, 0, []);

        var bytes = new byte[] { 1, 2, 3, 4 };
        var retained = new RetainedSection("GlobalVersion", 8, bytes, Sha256Hex(bytes));

        return new MapInterchange(
            MapInterchange.CurrentSchemaVersion,
            new MapIdentity("synthetic", MapSourceKind.Generated, null),
            layout,
            blend,
            objects,
            waypoints,
            provenance,
            report,
            [retained]);
    }

    private static Chunk BuildChunk(int cx, int cy)
    {
        return new Chunk(
            new ChunkKey(cx, cy),
            new HeightLayer(BuildSampleArray(cx, cy)),
            new MaterialLayer(new byte[MapChunkGeometry.CoreCellsPerChunk]),
            new MaskLayer(new byte[MapChunkGeometry.CoreCellsPerChunk]),
            null);
    }

    private static float[] BuildSampleArray(int cx, int cy)
    {
        var samples = new float[MapChunkGeometry.StoredHeightSamplesPerChunk];

        for (var row = 0; row < StoredAxis; row++)
        {
            for (var column = 0; column < StoredAxis; column++)
            {
                samples[(row * StoredAxis) + column] = LatticeSample(cx, cy, row, column);
            }
        }

        return samples;
    }

    private static float LatticeSample(int cx, int cy, int row, int column)
    {
        var x = (cx * IntervalsPerChunkAxis) + column - 1;
        var y = (cy * IntervalsPerChunkAxis) + row - 1;

        return x + y;
    }
}
