using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using Xunit;

namespace Panopticon.Geospatial.Preview.Tests;

public sealed class GeneratedMapPackageConverterTests
{
    private const string MapName = "fixture-map";
    private const string HeightQuantizationPrefix = GeneratedMapPackageDiagnosticCodes.HeightQuantization + ":";
    private const string HeightQuantizationSummaryPrefix = GeneratedMapPackageDiagnosticCodes.HeightQuantizationSummary + ":";

    [Fact]
    public void GeneratedMapPackageWritesFixturePayloadThatReloadsIntact()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.StartsWith(HeightQuantizationSummaryPrefix, Assert.Single(result.Diagnostics), StringComparison.Ordinal);
        Assert.Equal(target, result.PackageRoot);
        Assert.NotNull(result.Model);
        Assert.True(File.Exists(Path.Combine(target, MapPackageFormat.ManifestFileName)));

        var grid = reader.Metadata.Grid;
        Assert.Equal(grid.ChunksX * grid.ChunksY, result.ChunkCount);

        var model = new PackageReader(target).Read();
        Assert.Equal(MapName, model.Identity.Name);

        var latticeWidth = (grid.ChunksX * ChunkLayout.IntervalsPerChunk) + 1;
        var latticeHeight = (grid.ChunksY * ChunkLayout.IntervalsPerChunk) + 1;
        Assert.Equal(latticeWidth, model.Layout.LatticeWidth);
        Assert.Equal(latticeHeight, model.Layout.LatticeHeight);
        Assert.Equal(grid.ChunksX, model.Layout.ChunksX);
        Assert.Equal(grid.ChunksY, model.Layout.ChunksY);
        Assert.Equal(0, model.Layout.Border);
        Assert.Equal((uint)(latticeWidth * latticeHeight), model.Layout.MapArea);

        var expectedKeys = ChunkCoordinates(grid.ChunksX, grid.ChunksY).ToArray();
        Assert.Equal(expectedKeys, model.Layout.Chunks.Select(chunk => (chunk.Key.Cx, chunk.Key.Cy)).ToArray());

        for (var index = 0; index < expectedKeys.Length; index++)
        {
            var (chunkX, chunkY) = expectedKeys[index];
            var chunk = model.Layout.Chunks[index];

            Assert.Equal(
                PreviewPayloadFixture.TerrainFromLattice(chunkX, chunkY).Select(PreviewPayloadFixture.ResidentGridHeight).ToArray(),
                chunk.Heights.Samples.ToArray());
            Assert.Equal(PreviewPayloadFixture.MaterialFromLattice(chunkX, chunkY), chunk.Materials!.Indices.ToArray());
            Assert.Equal(PreviewPayloadFixture.MaskFromLattice(chunkX, chunkY), chunk.Masks!.Flags.ToArray());
        }

        var palette = model.Blend.Palette;
        Assert.Equal(PreviewPayloadFixture.PaletteNames, palette.Select(entry => entry.Name));
        Assert.Equal(Enumerable.Range(0, palette.Count).Select(index => (byte)index), palette.Select(entry => entry.Index));

        var sources = reader.Metadata.Provenance;
        Assert.Equal(sources.Count, model.Provenance.Sources.Count);
        Assert.Equal(sources.Select(source => source.Kind.ToString()), model.Provenance.Sources.Select(source => source.Kind));
        Assert.Equal(
            sources.Select(source => source.Sha256Hex),
            model.Provenance.Sources.Select(source => Assert.Single(source.Sha256Digests)));

        // SourceSchemaVersion carries the payload version, not the interchange schema version.
        Assert.Equal(
            reader.Metadata.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            model.Provenance.SourceSchemaVersion);

        var transform = reader.Metadata.Grid.Transform;
        var layoutTransform = model.Layout.GeospatialTransform;
        Assert.Equal(transform.ProjectedCoordinateSystem.ToString(), layoutTransform.SourceCrs);
        Assert.Equal<double?>(transform.OriginEastingNorthing.X, layoutTransform.OriginEasting?.Value);
        Assert.Equal<double?>(transform.OriginEastingNorthing.Y, layoutTransform.OriginNorthing?.Value);
        Assert.Equal(transform.MetersPerGameUnit, layoutTransform.MetersPerGameUnit);
        Assert.Equal(transform.VerticalDatum, layoutTransform.VerticalDatum);
        Assert.Equal<double?>(transform.VerticalOriginMeters, layoutTransform.VerticalOrigin?.Value);
        Assert.Equal<double?>(transform.VerticalExaggeration, layoutTransform.VerticalExaggeration);
    }

    [Fact]
    public void GeneratedMapPackageIsByteIdenticalAcrossRuns()
    {
        // Byte identity across independently staged runs is claimed; permutation invariance
        // of the input feature order is explicitly not (dedupe keeps the first occurrence in
        // input order).
        using var firstScratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(firstScratch.Root);
        using var secondScratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(secondScratch.Root);
        var firstTarget = Path.Combine(firstScratch.Root, "package");
        var secondTarget = Path.Combine(secondScratch.Root, "package");

        using (var firstReader = PreviewPayloadFixture.OpenPayload(firstScratch.Root))
        {
            var firstResult = GeneratedMapPackageConverter.Convert(firstReader, MapName, firstTarget, PreviewPayloadFixture.DefaultTextureMap());
            Assert.True(firstResult.Success, string.Join("; ", firstResult.Diagnostics));
        }

        using (var secondReader = PreviewPayloadFixture.OpenPayload(secondScratch.Root))
        {
            var secondResult = GeneratedMapPackageConverter.Convert(secondReader, MapName, secondTarget, PreviewPayloadFixture.DefaultTextureMap());
            Assert.True(secondResult.Success, string.Join("; ", secondResult.Diagnostics));
        }

        var firstTree = PackageFileDigests(firstTarget);
        var secondTree = PackageFileDigests(secondTarget);

        Assert.NotEmpty(firstTree);
        Assert.Equal(firstTree, secondTree);
    }

    [Fact]
    public void GeneratedMapPackageWithoutLineMaterialRulesIsByteIdenticalAcrossRuns()
    {
        // Opt-in dormancy guard (WP1026): the fixture payload declares no line-material
        // rules, so the line corridor pass must not perturb conversion; two independently
        // staged payload plus package runs produce identical package bytes.
        using var firstScratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(firstScratch.Root);
        using var secondScratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(secondScratch.Root);

        var firstPackage = Convert(firstScratch.Root, "package-a");
        var secondPackage = Convert(secondScratch.Root, "package-b");

        var firstTree = PackageFileDigests(firstPackage);
        var secondTree = PackageFileDigests(secondPackage);

        Assert.NotEmpty(firstTree);
        Assert.Equal(firstTree, secondTree);

        static string Convert(string scratchRoot, string packageName)
        {
            var target = Path.Combine(scratchRoot, packageName);
            using var reader = PreviewPayloadFixture.OpenPayload(scratchRoot);
            var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

            Assert.True(result.Success, string.Join("; ", result.Diagnostics));

            return target;
        }
    }

    [Fact]
    public void GeneratedMapPackageHeightsAgreeWithResidentMaterialization()
    {
        using var unrepresentableScratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(unrepresentableScratch.Root);
        using var unrepresentableReader = PreviewPayloadFixture.OpenPayload(unrepresentableScratch.Root);
        var unrepresentableTarget = Path.Combine(unrepresentableScratch.Root, "package");
        Assert.True(GeneratedMapPackageConverter.Convert(unrepresentableReader, MapName, unrepresentableTarget, PreviewPayloadFixture.DefaultTextureMap()).Success);

        // The fixture's 0.5 game unit Y term is off the 0.0390625 grid; the converter quantizes it so the bridge accepts the package.
        AssertResidentHeights(
            unrepresentableTarget,
            (latticeX, latticeY) => PreviewPayloadFixture.ResidentGridHeight(PreviewPayloadFixture.GlobalHeight(latticeX, latticeY)));

        using var representableScratch = new PreviewPayloadFixture.ScratchRoot();
        Func<long, long, float> heightOverride = (latticeX, latticeY) => 1000f + (1.25f * latticeX);
        PreviewPayloadFixture.CreatePayload(representableScratch.Root, heightOverride: heightOverride);
        using var representableReader = PreviewPayloadFixture.OpenPayload(representableScratch.Root);
        var representableTarget = Path.Combine(representableScratch.Root, "package");
        var representableResult = GeneratedMapPackageConverter.Convert(representableReader, MapName, representableTarget, PreviewPayloadFixture.DefaultTextureMap());
        Assert.True(representableResult.Success, string.Join("; ", representableResult.Diagnostics));
        Assert.Empty(representableResult.Diagnostics);
        var representableModel = new PackageReader(representableTarget).Read();

        var expectedKeys = ChunkCoordinates(representableReader.Metadata.Grid.ChunksX, representableReader.Metadata.Grid.ChunksY).ToArray();

        for (var index = 0; index < expectedKeys.Length; index++)
        {
            var (chunkX, chunkY) = expectedKeys[index];

            Assert.Equal(
                PreviewPayloadFixture.TerrainFromLattice(chunkX, chunkY, heightOverride),
                representableModel.Layout.Chunks[index].Heights.Samples.ToArray());
        }

        AssertResidentHeights(representableTarget, heightOverride);
    }

    [Fact]
    public void GeneratedMapPackageQuantizesTiesToEvenSteps()
    {
        // Half-step ties 0.5, 1.5, 2.5 and 3.5 round to 0, 2, 2 and 4 steps under round-half-even.
        float[] ties = [0.01953125f, 0.05859375f, 0.09765625f, 0.13671875f];
        Func<long, long, float> heightOverride = (latticeX, latticeY) => ties[((latticeX % 4) + 4) % 4];
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root, heightOverride: heightOverride);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var model = new PackageReader(target).Read();
        var stored = model.Layout.Chunks
            .SelectMany(chunk => chunk.Heights.Samples.ToArray())
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        Assert.Equal(new[] { 0f, 0.078125f, 0.15625f }, stored);
    }

    [Fact]
    public void GeneratedMapPackageKeepsGridMaximumAndRoundsHalfStepBelowZeroToZero()
    {
        // 2559.9609375 is the largest representable height (65535 steps); -0.01953125 is half a step below zero.
        Func<long, long, float> heightOverride = (latticeX, latticeY) =>
        {
            if (latticeX == 0 && latticeY == 0)
            {
                return 2559.9609375f;
            }

            if (latticeX == 1 && latticeY == 0)
            {
                return -0.01953125f;
            }

            return PreviewPayloadFixture.GlobalHeight(latticeX, latticeY);
        };
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root, heightOverride: heightOverride);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var model = new PackageReader(target).Read();
        var samples = model.Layout.Chunks.Single(chunk => chunk.Key.Cx == 0 && chunk.Key.Cy == 0).Heights.Samples.ToArray();
        var stride = ChunkLayout.StoredPointsPerChunkAxis;
        Assert.Equal(2559.9609375f, samples[(1 * stride) + 1]);
        Assert.Equal(0f, samples[(1 * stride) + 2]);
    }

    [Fact]
    public void GeneratedMapPackageRejectsTerrainBeyondResidentRangeWithoutPackage()
    {
        // 2559.98046875 is 65535.5 steps, the first value past the representable range.
        Func<long, long, float> heightOverride = (latticeX, latticeY) =>
            latticeX == 0 && latticeY == 0 ? 2559.98046875f : PreviewPayloadFixture.GlobalHeight(latticeX, latticeY);
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root, heightOverride: heightOverride);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");
        var grid = reader.Metadata.Grid;

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.False(result.Success);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.StartsWith(HeightQuantizationPrefix, diagnostic, StringComparison.Ordinal);
        var storedSamples = grid.TotalChunkCount * ChunkLayout.StoredPointsPerChunkAxis * ChunkLayout.StoredPointsPerChunkAxis;
        Assert.Contains($"1 of {storedSamples} terrain samples", diagnostic, StringComparison.Ordinal);
        Assert.Null(result.Model);
        Assert.Null(result.PackageRoot);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void GeneratedMapPackageConversionLeavesPayloadTerrainUnchanged()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        Assert.True(GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap()).Success);

        var terrain = reader.ReadTerrain(0, 0, new GeoDiagnosticBag());
        Assert.NotNull(terrain);
        Assert.Equal(PreviewPayloadFixture.TerrainFromLattice(0, 0), terrain!.Heights.ToArray());
    }

    [Fact]
    public void GeneratedMapPackageReportsHeightQuantizationCountAndMaxError()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");
        var grid = reader.Metadata.Grid;

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var diagnostic = Assert.Single(result.Diagnostics);

        var total = 0;
        var changed = 0;
        var maxError = 0d;

        foreach (var (chunkX, chunkY) in ChunkCoordinates(grid.ChunksX, grid.ChunksY))
        {
            foreach (var value in PreviewPayloadFixture.TerrainFromLattice(chunkX, chunkY))
            {
                var quantized = PreviewPayloadFixture.ResidentGridHeight(value);
                total++;

                if (quantized != value)
                {
                    changed++;
                    maxError = Math.Max(maxError, Math.Abs((double)quantized - value));
                }
            }
        }

        var maxErrorText = maxError.ToString("R", CultureInfo.InvariantCulture);
        Assert.True(changed > 0);
        Assert.True(maxError <= 0.01953125d);
        Assert.StartsWith(HeightQuantizationSummaryPrefix, diagnostic, StringComparison.Ordinal);
        Assert.Contains($"Quantized {changed} of {total} terrain samples", diagnostic, StringComparison.Ordinal);
        Assert.Contains($"max error {maxErrorText} game height units", diagnostic, StringComparison.Ordinal);
        Assert.Contains(result.Limitations, limitation => limitation.Contains("quantized to the resident height grid", StringComparison.Ordinal));
    }

    [Fact]
    public void GeneratedMapPackageWaypointAndFeaturePreservation()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var features = new[]
        {
            PreviewPayloadFixture.Waypoint("wp-alpha", 640d, 1280d, 1100d, "alpha"),
            PreviewPayloadFixture.Landmark("landmark-0", 300d, 300d),
            PreviewPayloadFixture.AnnotationLine(),
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var model = new PackageReader(target).Read();

        Assert.NotNull(model.Waypoints);
        var waypoint = Assert.Single(model.Waypoints!.Waypoints);
        Assert.Equal("wp-alpha", waypoint.StableId);
        Assert.Equal("alpha", waypoint.Name);
        Assert.Equal(640d, waypoint.X.Value);
        Assert.Equal(1280d, waypoint.Y.Value);
        Assert.Equal(1100d, waypoint.Z.Value);

        var generated = model.Provenance.GeneratedFeatures;
        Assert.Equal(reader.Metadata.Features.Count, generated.Count);

        foreach (var feature in reader.Metadata.Features)
        {
            var stableId = $"{feature.FeatureId}/{feature.PartId}";
            var entry = Assert.Single(generated, candidate => candidate.StableId == stableId);
            Assert.Equal(feature.Kind.ToString(), entry.Kind);
            Assert.Equal(feature.SourceId, entry.Attributes["sourceId"]);
            Assert.Equal(feature.RuleIndex.ToString(CultureInfo.InvariantCulture), entry.Attributes["ruleIndex"]);
            Assert.False(string.IsNullOrWhiteSpace(entry.Attributes["geometry"]));
        }

        var waypointEntry = Assert.Single(generated, candidate => candidate.StableId == "wp-alpha/wp-alpha#0");
        Assert.Equal("alpha", waypointEntry.Attributes["name"]);
        Assert.Equal("Point(500640 6001280)", waypointEntry.Attributes["geometry"]);

        var annotationEntry = Assert.Single(generated, candidate => candidate.StableId == "note-0/vectors/note-0#0");
        Assert.Equal("LineString(500000 6000000, 500010 600010)", annotationEntry.Attributes["geometry"]);

        Assert.NotEmpty(result.Limitations);
        Assert.Contains(result.Limitations, limitation => limitation.Contains("package vector layer", StringComparison.Ordinal));
    }

    [Fact]
    public void GeneratedMapPackagePlacedObjectsRoundTripThroughReload()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var features = new[]
        {
            PreviewPayloadFixture.Waypoint("wp-alpha", 640d, 1280d, 1100d, "alpha"),
            PreviewPayloadFixture.ObjectCandidate("tree-0", 512d, 768d, 1050d, "TreeOak", 90d),
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var model = new PackageReader(target).Read();

        // Only object-candidate features become placed objects; the waypoint does not.
        Assert.NotNull(model.Objects);
        var placed = Assert.Single(model.Objects!.Objects);
        Assert.Equal("tree-0", placed.StableId);
        Assert.Equal(512d, placed.X.Value);
        Assert.Equal(768d, placed.Y.Value);
        Assert.Equal(1050d, placed.Z.Value);
        Assert.Equal(MapQuantityUnit.GameUnit, placed.X.Unit);
        Assert.Equal(MapQuantityFrame.MapLocalXy, placed.X.Frame);
        Assert.Equal(MapQuantityFrame.MapLocalZ, placed.Z.Frame);
        Assert.Equal(90d * Math.PI / 180d, placed.OrientationRadians);
        Assert.Equal("legacy", placed.Type.Scope);
        Assert.Equal("TreeOak", placed.Type.Name);
        Assert.Equal(0u, placed.RoadType);
        Assert.Empty(placed.LegacyProperties);
    }

    [Fact]
    public void GeneratedMapPackageCarriesConverterImportReport()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.NotNull(result.Model);
        var report = result.Model!.Report;

        var diagnostic = Assert.Single(report.Diagnostics);
        Assert.Equal("GEO-VAL-001 Info: Control points matched.", diagnostic);
        Assert.Equal(new[] { "GsdRatioX [0.98, 1.02]", "GsdRatioY [0.97, 1.03]" }, report.KernelAndSampleDistanceRatios);

        // Only control-point category checks survive; the payload-category inventory check is dropped.
        var check = Assert.Single(report.ControlPointResults);
        Assert.Equal("corner-offset: Passed: Corner offset within tolerance.", check);
        Assert.Equal(0, report.OutsideFootprintCellCount);
        Assert.Equal(0, report.NodataCellCount);

        // The package format stores objects, waypoints, and provenance but not the import
        // report ("Import result, not package content"), so a reload reads back the empty
        // placeholder instead of the converter's report.
        var model = new PackageReader(target).Read();
        Assert.Empty(model.Report.Diagnostics);
        Assert.Empty(model.Report.KernelAndSampleDistanceRatios);
        Assert.Empty(model.Report.ControlPointResults);
        Assert.Equal(0, model.Report.OutsideFootprintCellCount);
        Assert.Equal(0, model.Report.NodataCellCount);
    }

    [Fact]
    public void GeneratedMapPackageDeduplicatesIdenticalDuplicateFeatures()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var duplicate = PreviewPayloadFixture.ObjectCandidate("tree-0", 512d, 768d, 1050d, "TreeOak", 90d);
        var features = new[]
        {
            PreviewPayloadFixture.Waypoint("wp-alpha", 640d, 1280d, 1100d, "alpha"),
            duplicate,
            duplicate,
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var diagnostic = Assert.Single(NonHeightDiagnostics(result));
        Assert.Contains("tree-0/tree-0#0", diagnostic, StringComparison.Ordinal);

        var model = new PackageReader(target).Read();
        Assert.NotNull(model.Objects);
        Assert.Single(model.Objects!.Objects);
        Assert.Single(model.Waypoints!.Waypoints);
        Assert.Equal(2, model.Provenance.GeneratedFeatures.Count);
    }

    [Fact]
    public void GeneratedMapPackageConflictingDuplicateFeatureFailsWithoutPackage()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var features = new[]
        {
            PreviewPayloadFixture.ObjectCandidate("tree-0", 512d, 768d, 1050d, "TreeOak", 90d),
            PreviewPayloadFixture.ObjectCandidate("tree-0", 512d, 768d, 1055d, "TreeOak", 90d),
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Contains("stable-id-duplicate", StringComparison.Ordinal) && diagnostic.Contains("tree-0/tree-0#0", StringComparison.Ordinal));
        Assert.Null(result.Model);
        Assert.Null(result.PackageRoot);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void GeneratedMapPackageMergesSamePartMaskFeatureProvenance()
    {
        // Real-pair shape: one source part mapped by rule 0 into a passability feature and by
        // rule 1 into a buildability feature. Keep-first retains the passability entry because
        // it is first in the input order (permutation invariance is explicitly not claimed);
        // the chunk masks still carry both projected bits from the payload lattice.
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var features = new[]
        {
            PreviewPayloadFixture.MaskRegion(GeneratedFeatureKind.Passability, "BATIMENT-0", "sedan-proof-buildings/BATIMENT-0#0", flag: false, ruleIndex: 0),
            PreviewPayloadFixture.MaskRegion(GeneratedFeatureKind.Buildability, "BATIMENT-0", "sedan-proof-buildings/BATIMENT-0#0", flag: false, ruleIndex: 1),
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var diagnostic = Assert.Single(NonHeightDiagnostics(result));
        Assert.Contains("BATIMENT-0/sedan-proof-buildings/BATIMENT-0#0", diagnostic, StringComparison.Ordinal);

        var model = new PackageReader(target).Read();
        var entry = Assert.Single(model.Provenance.GeneratedFeatures);
        Assert.Equal("BATIMENT-0/sedan-proof-buildings/BATIMENT-0#0", entry.StableId);
        Assert.Equal("Passability", entry.Kind);
        Assert.Equal(4, entry.Attributes.Count);
        Assert.Equal("vectors", entry.Attributes["sourceId"]);
        Assert.Equal("0", entry.Attributes["ruleIndex"]);
        Assert.Equal("Passability", entry.Attributes["kind"]);
        Assert.Equal(ExpectedMaskGeometryText(span: 10d), entry.Attributes["geometry"]);

        var expectedFlags = PreviewPayloadFixture.MaskFromLattice(0, 0);
        Assert.Equal(expectedFlags, model.Layout.Chunks[0].Masks!.Flags.ToArray());
        Assert.Contains(expectedFlags, flag => flag == (byte)TerrainMaskFlags.Impassable);
        Assert.Contains(expectedFlags, flag => flag == (byte)TerrainMaskFlags.Unbuildable);

        Assert.Null(model.Objects);
        Assert.Null(model.Waypoints);
    }

    [Fact]
    public void GeneratedMapPackageMergeKeepsFirstInReversedInputOrder()
    {
        // The same same-part pair with the opposite list order: keep-first retains the
        // buildability entry instead, pinning that retention follows input order rather than
        // any rule-kind preference.
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var features = new[]
        {
            PreviewPayloadFixture.MaskRegion(GeneratedFeatureKind.Buildability, "BATIMENT-0", "sedan-proof-buildings/BATIMENT-0#0", flag: false, ruleIndex: 1),
            PreviewPayloadFixture.MaskRegion(GeneratedFeatureKind.Passability, "BATIMENT-0", "sedan-proof-buildings/BATIMENT-0#0", flag: false, ruleIndex: 0),
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Single(NonHeightDiagnostics(result));

        var model = new PackageReader(target).Read();
        var entry = Assert.Single(model.Provenance.GeneratedFeatures);
        Assert.Equal("BATIMENT-0/sedan-proof-buildings/BATIMENT-0#0", entry.StableId);
        Assert.Equal("Buildability", entry.Kind);
        Assert.Equal("1", entry.Attributes["ruleIndex"]);
        Assert.Equal("Buildability", entry.Attributes["kind"]);
        Assert.Equal("vectors", entry.Attributes["sourceId"]);
        Assert.Equal(ExpectedMaskGeometryText(span: 10d), entry.Attributes["geometry"]);
    }

    [Fact]
    public void GeneratedMapPackageSourceIdentityConflictFailsWithoutPackage()
    {
        // Same stable id, same source part, but the members' source geometry differs: the
        // same-part merge proof fails and the conversion must fail without a package.
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var features = new[]
        {
            PreviewPayloadFixture.MaskRegion(GeneratedFeatureKind.Passability, "BATIMENT-0", "sedan-proof-buildings/BATIMENT-0#0", flag: false, ruleIndex: 0),
            PreviewPayloadFixture.MaskRegion(GeneratedFeatureKind.Buildability, "BATIMENT-0", "sedan-proof-buildings/BATIMENT-0#0", flag: false, ruleIndex: 1, span: 20d),
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Contains(GeneratedMapPackageDiagnosticCodes.StableIdSourceConflict, StringComparison.Ordinal)
                && diagnostic.Contains("BATIMENT-0/sedan-proof-buildings/BATIMENT-0#0", StringComparison.Ordinal)
                && diagnostic.Contains("geometry", StringComparison.Ordinal));
        Assert.Null(result.Model);
        Assert.Null(result.PackageRoot);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void GeneratedMapPackageMaskFlagsParticipateInIdentityKey()
    {
        // Same vector emission tuple (stable id, kind, rule index) with the passable flag differing:
        // the vector layer cannot hold two emissions under one tuple, so the conversion fails.
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var features = new[]
        {
            PreviewPayloadFixture.MaskRegion(GeneratedFeatureKind.Passability, "zone-0", "vectors/zone-0#0", flag: true, ruleIndex: 0),
            PreviewPayloadFixture.MaskRegion(GeneratedFeatureKind.Passability, "zone-0", "vectors/zone-0#0", flag: false, ruleIndex: 0),
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.StartsWith(GeneratedMapPackageDiagnosticCodes.VectorFeatureConflict + ":", StringComparison.Ordinal)
                && diagnostic.Contains("zone-0/vectors/zone-0#0", StringComparison.Ordinal));
        Assert.Null(result.Model);
        Assert.Null(result.PackageRoot);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void GeneratedMapPackageSamePartMaskPairLandsBothVectorFeatures()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var features = new[]
        {
            PreviewPayloadFixture.MaskRegion(GeneratedFeatureKind.Passability, "BATIMENT-0", "sedan-proof-buildings/BATIMENT-0#0", flag: false, ruleIndex: 0),
            PreviewPayloadFixture.MaskRegion(GeneratedFeatureKind.Buildability, "BATIMENT-0", "sedan-proof-buildings/BATIMENT-0#0", flag: false, ruleIndex: 1),
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var model = new PackageReader(target).Read();

        Assert.NotNull(model.VectorLayers);
        Assert.Equal(2, model.VectorLayers!.Features.Count);

        var passability = model.VectorLayers.Features[0];
        Assert.Equal("BATIMENT-0/sedan-proof-buildings/BATIMENT-0#0", passability.SourceStableId);
        Assert.Equal("Passability", passability.Kind);
        Assert.Equal(0, passability.RuleIndex);
        Assert.Equal<bool?>(false, passability.Passable);
        Assert.Null(passability.Buildable);

        var buildability = model.VectorLayers.Features[1];
        Assert.Equal("BATIMENT-0/sedan-proof-buildings/BATIMENT-0#0", buildability.SourceStableId);
        Assert.Equal("Buildability", buildability.Kind);
        Assert.Equal(1, buildability.RuleIndex);
        Assert.Equal<bool?>(false, buildability.Buildable);
        Assert.Null(buildability.Passable);

        var entry = Assert.Single(model.Provenance.GeneratedFeatures);
        Assert.Equal("BATIMENT-0/sedan-proof-buildings/BATIMENT-0#0", entry.StableId);
    }

    [Fact]
    public void GeneratedMapPackageVectorGeometryRoundTripsExactly()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var road = new GeoLineString(
        [
            new GeoPosition(new Vector2d(500010.5d, 6000020.25d), 5.5d),
            new GeoPosition(new Vector2d(500040d, 6000020.25d), 7.25d),
            new GeoPosition(new Vector2d(500040d, 6000050d)),
        ]);
        var outer = new GeoRing(
        [
            new GeoPosition(new Vector2d(500000d, 6000000d), 1d),
            new GeoPosition(new Vector2d(500000d, 6000100d)),
            new GeoPosition(new Vector2d(500100d, 6000100d), 2.5d),
            new GeoPosition(new Vector2d(500100d, 6000000d)),
            new GeoPosition(new Vector2d(500000d, 6000000d), 1d),
        ]);
        var hole = new GeoRing(
        [
            new GeoPosition(new Vector2d(500020d, 6000020d)),
            new GeoPosition(new Vector2d(500080d, 6000020d)),
            new GeoPosition(new Vector2d(500080d, 6000080d)),
            new GeoPosition(new Vector2d(500020d, 6000080d)),
            new GeoPosition(new Vector2d(500020d, 6000020d)),
        ]);
        var multiLine = new GeoLineString(
        [
            new GeoPosition(new Vector2d(500200d, 6000200d), 3d),
            new GeoPosition(new Vector2d(500210d, 6000210d), 4d),
        ]);
        var multiPoint = new GeoPointGeometry(new GeoPosition(new Vector2d(500300d, 6000300d), 9d));
        var multiSquare = new GeoRing(
        [
            new GeoPosition(new Vector2d(500400d, 6000400d)),
            new GeoPosition(new Vector2d(500420d, 6000400d)),
            new GeoPosition(new Vector2d(500420d, 6000420d)),
            new GeoPosition(new Vector2d(500400d, 6000420d)),
            new GeoPosition(new Vector2d(500400d, 6000400d)),
        ]);
        var multi = new GeoMultiGeometry([multiLine, multiPoint, new GeoPolygon(multiSquare)]);
        var features = new[]
        {
            PreviewPayloadFixture.Annotation("road-0", road),
            PreviewPayloadFixture.PassabilityPolygon("zone-hole", new GeoPolygon(outer, [hole]), ruleIndex: 0, passable: false),
            PreviewPayloadFixture.Annotation("multi-0", multi),
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var model = new PackageReader(target).Read();
        Assert.NotNull(model.VectorLayers);
        var vectors = model.VectorLayers!.Features;
        Assert.Equal(3, vectors.Count);

        var roadLine = Assert.IsType<VectorLineStringGeometry>(vectors[0].Geometry);
        Assert.Equal(PositionKeys(road.Positions.Select(ToVectorPosition)), PositionKeys(roadLine.Positions));

        Assert.Equal("Passability", vectors[1].Kind);
        Assert.Equal<bool?>(false, vectors[1].Passable);
        var zone = Assert.IsType<VectorPolygonGeometry>(vectors[1].Geometry);
        Assert.Equal(PositionKeys(outer.Positions.Select(ToVectorPosition)), PositionKeys(zone.OuterRing.Positions));
        Assert.True(SignedArea(zone.OuterRing.Positions) < 0d);
        Assert.Equal(PositionKeys(hole.Positions.Select(ToVectorPosition)), PositionKeys(Assert.Single(zone.InnerRings).Positions));

        var collection = Assert.IsType<VectorMultiGeometry>(vectors[2].Geometry);
        Assert.Equal(3, collection.Geometries.Count);
        Assert.Equal(PositionKeys(multiLine.Positions.Select(ToVectorPosition)), PositionKeys(Assert.IsType<VectorLineStringGeometry>(collection.Geometries[0]).Positions));
        Assert.Equal(PositionKeys([ToVectorPosition(multiPoint.Position)]), PositionKeys([Assert.IsType<VectorPointGeometry>(collection.Geometries[1]).Position]));
        var child = Assert.IsType<VectorPolygonGeometry>(collection.Geometries[2]);
        Assert.Equal(PositionKeys(multiSquare.Positions.Select(ToVectorPosition)), PositionKeys(child.OuterRing.Positions));
    }

    [Fact]
    public void GeneratedMapPackageCollapsesIdenticalDuplicateVectorEmission()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var line = PreviewPayloadFixture.AnnotationLine();
        var features = new[]
        {
            PreviewPayloadFixture.Waypoint("wp-alpha", 640d, 1280d, 1100d, "alpha"),
            line,
            line,
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.NotEmpty(NonHeightDiagnostics(result));
        Assert.All(NonHeightDiagnostics(result), diagnostic => Assert.StartsWith(GeneratedMapPackageDiagnosticCodes.StableIdDeduplicated + ":", diagnostic, StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains("identical duplicate vector emissions were collapsed", StringComparison.Ordinal));

        var model = new PackageReader(target).Read();
        var vector = Assert.Single(model.VectorLayers!.Features);
        Assert.Equal("note-0/vectors/note-0#0", vector.SourceStableId);
        Assert.Equal(2, model.Provenance.GeneratedFeatures.Count);
    }

    [Fact]
    public void GeneratedMapPackageConflictingVectorEmissionFailsWithoutPackage()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var features = new[]
        {
            PreviewPayloadFixture.AnnotationLine(),
            PreviewPayloadFixture.Annotation(
                "note-0",
                new GeoLineString(
                [
                    new GeoPosition(new Vector2d(500000d, 6000000d)),
                    new GeoPosition(new Vector2d(500020d, 6000020d)),
                ])),
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.StartsWith(GeneratedMapPackageDiagnosticCodes.VectorFeatureConflict + ":", StringComparison.Ordinal)
                && diagnostic.Contains("note-0/vectors/note-0#0", StringComparison.Ordinal));
        Assert.Null(result.Model);
        Assert.Null(result.PackageRoot);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void GeneratedMapPackageWithoutVectorFeaturesWritesVersionOnePackage()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var features = new[]
        {
            PreviewPayloadFixture.Waypoint("wp-alpha", 640d, 1280d, 1100d, "alpha"),
            PreviewPayloadFixture.Landmark("landmark-0", 300d, 300d),
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Null(result.Model!.VectorLayers);

        var (manifest, diagnostics) = MapPackageManifestJson.ParseAndValidate(File.ReadAllText(Path.Combine(target, MapPackageFormat.ManifestFileName)));
        Assert.Empty(diagnostics);
        Assert.Equal(MapPackageFormat.CurrentPackageVersion, manifest!.PackageVersion);
        Assert.Null(manifest.VectorLayer);
        Assert.False(File.Exists(Path.Combine(target, MapPackageFormat.VectorLayerPath)));
        Assert.Null(new PackageReader(target).Read().VectorLayers);
    }

    [Fact]
    public void GeneratedMapPackageImageryRoundTripsThroughReload()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root, imageryPixelsPerSample: 1);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var model = new PackageReader(target).Read();

        var pixelsPerAxis = ChunkLayout.IntervalsPerChunk * reader.Metadata.Grid.ImageryPixelsPerSample!.Value;
        var expectedKeys = ChunkCoordinates(reader.Metadata.Grid.ChunksX, reader.Metadata.Grid.ChunksY).ToArray();

        for (var index = 0; index < expectedKeys.Length; index++)
        {
            var (chunkX, chunkY) = expectedKeys[index];
            var imagery = model.Layout.Chunks[index].Imagery;

            Assert.NotNull(imagery);
            Assert.Equal(pixelsPerAxis, imagery!.Width);
            Assert.Equal(pixelsPerAxis, imagery.Height);
            Assert.Equal(PreviewPayloadFixture.ImageryPixels(chunkX, chunkY, pixelsPerAxis), imagery.Rgba);
        }

        var (manifest, manifestDiagnostics) = MapPackageManifestJson.ParseAndValidate(File.ReadAllText(Path.Combine(target, MapPackageFormat.ManifestFileName)));
        Assert.NotNull(manifest);
        Assert.Empty(manifestDiagnostics);

        var imageryEntries = manifest!.Layers.Where(layer => layer.LayerKind == MapPackageFormat.LayerKindImagery).ToArray();
        Assert.Equal(expectedKeys.Length, imageryEntries.Length);

        for (var index = 0; index < expectedKeys.Length; index++)
        {
            var (chunkX, chunkY) = expectedKeys[index];
            var entry = Assert.Single(imageryEntries, layer => layer.ChunkX == chunkX && layer.ChunkY == chunkY);
            Assert.Equal($"layers/imagery_{chunkX}_{chunkY}.raw", entry.Path);
            Assert.Equal(MapPackageFormat.EncodingRgba8, entry.Encoding);
            Assert.Equal(pixelsPerAxis, entry.Width);
            Assert.Equal(pixelsPerAxis, entry.Height);
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(PreviewPayloadFixture.ImageryPixels(chunkX, chunkY, pixelsPerAxis))).ToLowerInvariant(),
                entry.Sha256);
        }
    }

    private static string ExpectedMaskGeometryText(double span)
    {
        var far = string.Create(CultureInfo.InvariantCulture, $"{500000d + span}");
        var north = string.Create(CultureInfo.InvariantCulture, $"{6000000d + span}");

        return $"Polygon((500000 6000000, {far} 6000000, {far} {north}, 500000 {north}, 500000 6000000, 500000 6000000))";
    }

    [Fact]
    public void GeneratedMapPackageMissingChunkFailsWithoutPackage()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        // Open leaves every terrain chunk cached, so the converter's read re-verifies the file and fails there.
        File.Delete(Path.Combine(scratch.Root, PayloadChunkPaths.Terrain(1, 1)));

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.StartsWith("GEO-PAY-", diagnostic, StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Contains(PayloadChunkPaths.Terrain(1, 1), StringComparison.Ordinal));
        Assert.Null(result.Model);
        Assert.Null(result.PackageRoot);
        Assert.Empty(result.Limitations);
        Assert.False(File.Exists(Path.Combine(target, MapPackageFormat.ManifestFileName)));
    }

    [Fact]
    public void GeneratedMapPackageCorruptChunkFailsAtOpenWithoutPackage()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);
        var terrainPath = Path.Combine(scratch.Root, PayloadChunkPaths.Terrain(0, 0));
        var bytes = File.ReadAllBytes(terrainPath);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(terrainPath, bytes);
        var target = Path.Combine(scratch.Root, "package");

        // Open verifies every chunk before returning, so the corrupt file is rejected before any conversion runs.
        var diagnostics = new GeoDiagnosticBag();
        var reader = GeneratedMapPayloadReader.Open(scratch.Root, diagnostics);

        Assert.Null(reader);
        Assert.True(diagnostics.HasErrors);
        Assert.Contains(diagnostics.Items, diagnostic => diagnostic.Message.Contains(PayloadChunkPaths.Terrain(0, 0), StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(target, MapPackageFormat.ManifestFileName)));
    }

    private static VectorPosition ToVectorPosition(GeoPosition position) =>
        new(position.Coordinate.X, position.Coordinate.Y, position.AltitudeMeters);

    // Bit patterns, not decimal text, so the comparison is exact for every coordinate and altitude.
    private static string[] PositionKeys(IEnumerable<VectorPosition> positions) =>
        positions.Select(position => string.Create(
            CultureInfo.InvariantCulture,
            $"{BitConverter.DoubleToInt64Bits(position.X):x16} {BitConverter.DoubleToInt64Bits(position.Y):x16} {(position.AltitudeMeters is { } altitude ? BitConverter.DoubleToInt64Bits(altitude).ToString("x16", CultureInfo.InvariantCulture) : "none")}"))
            .ToArray();

    private static double SignedArea(IReadOnlyList<VectorPosition> ring)
    {
        var sum = 0d;

        for (var index = 0; index < ring.Count - 1; index++)
        {
            sum += (ring[index].X * ring[index + 1].Y) - (ring[index + 1].X * ring[index].Y);
        }

        return sum / 2d;
    }

    private static void AssertResidentHeights(string packageRoot, Func<long, long, float> expected)
    {
        var model = new PackageReader(packageRoot).Read();
        var heights = RuntimeBridge.MaterializeHeightMapData(model);

        for (var latticeY = 0; latticeY < model.Layout.LatticeHeight; latticeY++)
        {
            for (var latticeX = 0; latticeX < model.Layout.LatticeWidth; latticeX++)
            {
                var resident = heights.Elevations[latticeX, latticeY] * heights.VerticalScale;
                Assert.Equal(expected(latticeX, latticeY), resident);
            }
        }
    }

    [Fact]
    public void BlendTiles_MultiMaterialAndImpassability_RecoveredByFreshPackageReader()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));

        var grid = reader.Metadata.Grid;
        var latticeWidth = (grid.ChunksX * ChunkLayout.IntervalsPerChunk) + 1;
        var latticeHeight = (grid.ChunksY * ChunkLayout.IntervalsPerChunk) + 1;
        var blend = RuntimeBridge.MaterializeBlendTileData(new PackageReader(target).Read());

        Assert.NotNull(blend);
        Assert.Equal(new[] { "GrassType1", "DirtMediumType1", "RockType1" }, blend.Textures.Select(texture => texture.Name).ToArray());
        Assert.Equal(latticeWidth, blend.Tiles.GetLength(0));
        Assert.Equal(latticeHeight, blend.Tiles.GetLength(1));

        var mismatches = 0;

        for (var y = 0; y < latticeHeight; y++)
        {
            for (var x = 0; x < latticeWidth; x++)
            {
                var expectedTile = (ushort)(PreviewPayloadFixture.GlobalMaterial(x, y) * 64);
                var expectedImpassable = (PreviewPayloadFixture.GlobalMask(x, y) & (byte)TerrainMaskFlags.Impassable) != 0;

                if (blend.Tiles[x, y] != expectedTile || blend.Impassability[x, y] != expectedImpassable)
                {
                    mismatches++;
                }
            }
        }

        Assert.Equal(0, mismatches);
    }

    [Fact]
    public void Lighting_NeutralDaylightAndEmptyWaypointPaths_RecoveredByFreshPackageReader()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));

        var package = new PackageReader(target).Read();
        var lighting = RuntimeBridge.MaterializeGlobalLighting(package);
        var waypoints = RuntimeBridge.MaterializeWaypointsList(package);

        Assert.NotNull(lighting);
        Assert.Equal((ushort)6, lighting!.Version);
        Assert.Equal(4, lighting.LightingConfigurations.Count);
        Assert.InRange(lighting.LightingConfigurations.Values.First().TerrainSun.Direction.Length(), 0.999f, 1.001f);
        Assert.NotNull(waypoints);
        Assert.Empty(waypoints!.WaypointPaths);
    }

    [Fact]
    public void BlendTiles_NoncontiguousPaletteIndex_UsesTheOrdinalOfItsTexture()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root, materialAt: NoncontiguousMaterial);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");
        var textureMap = new PreviewTextureMap(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grass"] = "GrassType1",
                ["dirt"] = "GrassType1",
                ["rock"] = "RockType1",
            },
            new PreviewPayloadFixture.FakeCatalog());

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, textureMap);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));

        var blend = RuntimeBridge.MaterializeBlendTileData(new PackageReader(target).Read());

        Assert.NotNull(blend);
        Assert.Equal(new[] { "GrassType1", "RockType1" }, blend.Textures.Select(texture => texture.Name).ToArray());

        for (var y = 0; y < blend.Tiles.GetLength(1); y++)
        {
            for (var x = 0; x < blend.Tiles.GetLength(0); x++)
            {
                var expectedTile = NoncontiguousMaterial(x, y) == 2 ? 64 : 0;
                Assert.Equal((ushort)expectedTile, blend.Tiles[x, y]);
            }
        }
    }

    [Fact]
    public void BlendTiles_UnboundOrMissingTextureMap_FailsBeforePublishing()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var unboundTarget = Path.Combine(scratch.Root, "unbound");
        var missingTarget = Path.Combine(scratch.Root, "missing");
        var partialMap = new PreviewTextureMap(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["grass"] = "GrassType1", ["rock"] = "RockType1" },
            new PreviewPayloadFixture.FakeCatalog());

        var unbound = GeneratedMapPackageConverter.Convert(reader, MapName, unboundTarget, partialMap);
        var missing = GeneratedMapPackageConverter.Convert(reader, MapName, missingTarget, null);

        Assert.False(unbound.Success);
        Assert.Equal(GeneratedMapPackageDiagnosticCodes.BlendTextureUnresolved + ": Palette entries without a resolvable texture binding: dirt; no package was written.", Assert.Single(unbound.Diagnostics));
        Assert.False(Directory.Exists(unboundTarget));

        Assert.False(missing.Success);
        Assert.StartsWith(GeneratedMapPackageDiagnosticCodes.BlendTextureUnresolved + ":", Assert.Single(missing.Diagnostics), StringComparison.Ordinal);
        Assert.False(Directory.Exists(missingTarget));
    }

    [Fact]
    public void BlendTiles_ConversionLeavesThePayloadUnmodified()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var payloadRoot = Path.Combine(scratch.Root, "payload");
        PreviewPayloadFixture.CreatePayload(payloadRoot);
        var before = PackageFileDigests(payloadRoot);
        using var reader = PreviewPayloadFixture.OpenPayload(payloadRoot);

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, Path.Combine(scratch.Root, "package"), PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(before, PackageFileDigests(payloadRoot));
    }

    [Fact]
    public void SkirmishSlots_WritePlayableSidesAndPlayerStartsAtDeclaredPositions()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");
        var slots = new[]
        {
            new GeneratedMapSkirmishSlot("FactionAmerica", 100d, 100d),
            new GeneratedMapSkirmishSlot("FactionGLA", 300d, 300d),
        };

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap(), slots);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var diagnosticCodes = result.Diagnostics.Select(diagnostic => diagnostic.Split(':')[0]).ToArray();
        Assert.Contains(GeneratedMapPackageDiagnosticCodes.SkirmishSynthesized, diagnosticCodes);
        Assert.Contains(GeneratedMapPackageDiagnosticCodes.HeightQuantizationSummary, diagnosticCodes);

        var model = new PackageReader(target).Read();

        // The retained SidesList section carries the playable sides; its parsed content is
        // asserted against the backend readers in the Simulation test project, which owns
        // the internal section reader.
        var sidesSection = Assert.Single(model.RetainedSections, section => section.SectionName == "SidesList");
        Assert.Equal(3, (int)sidesSection.SectionVersion);

        // One player start PlacedObject per slot: the starred waypoint type name, the declared
        // map-local position, and the properties the runtime waypoint scanner reads.
        var objects = model.Objects!.Objects;
        var start1 = Assert.Single(objects, placed => placed.StableId == "Player_1_Start");
        var start2 = Assert.Single(objects, placed => placed.StableId == "Player_2_Start");

        foreach (var (placed, slot, waypointId) in new[] { (start1, slots[0], 1), (start2, slots[1], 2) })
        {
            Assert.Equal("*Waypoints/Waypoint", placed.Type.Name);
            Assert.Equal(slot.X, placed.X.Value);
            Assert.Equal(slot.Y, placed.Y.Value);
            Assert.Equal(0d, placed.OrientationRadians);
            Assert.Equal(waypointId, Assert.IsType<MapPropertyValue.IntegerValue>(placed.LegacyProperties["waypointID"]).Value);
            Assert.Equal(placed.StableId, Assert.IsType<MapPropertyValue.AsciiStringValue>(placed.LegacyProperties["waypointName"]).Value);
        }

        // Z is sampled from the written quantized terrain at the slot's lattice point.
        var expectedZ1 = PreviewPayloadFixture.ResidentGridHeight(PreviewPayloadFixture.GlobalHeight(10, 10));
        var expectedZ2 = PreviewPayloadFixture.ResidentGridHeight(PreviewPayloadFixture.GlobalHeight(30, 30));
        Assert.Equal(expectedZ1, start1.Z.Value);
        Assert.Equal(expectedZ2, start2.Z.Value);
    }

    [Fact]
    public void SkirmishSlots_AreByteIdenticalAcrossIndependentRuns()
    {
        using var firstScratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(firstScratch.Root);
        using var secondScratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(secondScratch.Root);
        var slots = new[]
        {
            new GeneratedMapSkirmishSlot("FactionAmerica", 100d, 100d),
            new GeneratedMapSkirmishSlot("FactionGLA", 300d, 300d),
        };

        using (var firstReader = PreviewPayloadFixture.OpenPayload(firstScratch.Root))
        {
            var firstResult = GeneratedMapPackageConverter.Convert(
                firstReader, MapName, Path.Combine(firstScratch.Root, "package"), PreviewPayloadFixture.DefaultTextureMap(), slots);
            Assert.True(firstResult.Success, string.Join("; ", firstResult.Diagnostics));
        }

        using (var secondReader = PreviewPayloadFixture.OpenPayload(secondScratch.Root))
        {
            var secondResult = GeneratedMapPackageConverter.Convert(
                secondReader, MapName, Path.Combine(secondScratch.Root, "package"), PreviewPayloadFixture.DefaultTextureMap(), slots);
            Assert.True(secondResult.Success, string.Join("; ", secondResult.Diagnostics));
        }

        Assert.Equal(PackageFileDigests(Path.Combine(firstScratch.Root, "package")), PackageFileDigests(Path.Combine(secondScratch.Root, "package")));
    }

    [Fact]
    public void WithoutSkirmishSlots_WritesNoSidesListAndNoStartWaypoints()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));

        var model = new PackageReader(target).Read();
        Assert.DoesNotContain(model.RetainedSections, section => section.SectionName == "SidesList");
        Assert.DoesNotContain(model.Objects?.Objects ?? [], placed => placed.StableId.StartsWith("Player_", StringComparison.Ordinal));
    }

    [Fact]
    public void SkirmishSlotBeyondTheMapExtent_FailsBeforePublishing()
    {
        // The 2x2-chunk fixture extent is 2560 game units; slot 2 lies outside it.
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");
        var slots = new[]
        {
            new GeneratedMapSkirmishSlot("FactionAmerica", 100d, 100d),
            new GeneratedMapSkirmishSlot("FactionGLA", 5000d, 500d),
        };

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap(), slots);

        Assert.False(result.Success);
        Assert.StartsWith(
            GeneratedMapPackageDiagnosticCodes.SkirmishConfiguration + ": Skirmish slot 2 declares the start position (5000, 500), which is outside the map extent",
            Assert.Single(result.Diagnostics),
            StringComparison.Ordinal);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void SkirmishSlotWithUnknownFaction_FailsBeforePublishing()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(
            reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap(), [new GeneratedMapSkirmishSlot("FactionMars", 100d, 100d)]);

        Assert.False(result.Success);
        Assert.StartsWith(
            GeneratedMapPackageDiagnosticCodes.SkirmishConfiguration + ": Skirmish slot 1 declares faction 'FactionMars', which is not a known player template",
            Assert.Single(result.Diagnostics),
            StringComparison.Ordinal);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void SkirmishSlotWithDuplicatePosition_FailsBeforePublishing()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");
        var slots = new[]
        {
            new GeneratedMapSkirmishSlot("FactionAmerica", 100d, 100d),
            new GeneratedMapSkirmishSlot("FactionGLA", 100d, 100d),
        };

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap(), slots);

        Assert.False(result.Success);
        Assert.StartsWith(
            GeneratedMapPackageDiagnosticCodes.SkirmishConfiguration + ": Skirmish slot 2 repeats the start position (100, 100) of an earlier slot",
            Assert.Single(result.Diagnostics),
            StringComparison.Ordinal);
        Assert.False(Directory.Exists(target));
    }

    private static byte NoncontiguousMaterial(long x, long y) => (x + (3 * y)) % 3 == 0 ? (byte)0 : (byte)2;

    private static IEnumerable<string> NonHeightDiagnostics(GeneratedMapPackageResult result)
    {
        return result.Diagnostics.Where(diagnostic => !diagnostic.StartsWith(HeightQuantizationSummaryPrefix, StringComparison.Ordinal));
    }

    private static IEnumerable<(int ChunkX, int ChunkY)> ChunkCoordinates(int chunksX, int chunksY)
    {
        return Enumerable.Range(0, chunksY).SelectMany(chunkY => Enumerable.Range(0, chunksX).Select(chunkX => (chunkX, chunkY)));
    }

    /// <summary>One "relative path sha256" line per file under the package root, ordered by path for stable comparison.</summary>
    private static IReadOnlyList<string> PackageFileDigests(string packageRoot)
    {
        return Directory
            .EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories)
            .Select(file => (Path: Path.GetRelativePath(packageRoot, file), Digest: Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant()))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .Select(entry => $"{entry.Path} {entry.Digest}")
            .ToArray();
    }
}
