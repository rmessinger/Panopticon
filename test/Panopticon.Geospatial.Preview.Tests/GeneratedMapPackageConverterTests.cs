using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Grid;
using Panopticon.Geospatial.Payload;
using Panopticon.Geospatial.Payload.Models;
using Xunit;

namespace Panopticon.Geospatial.Preview.Tests;

public sealed class GeneratedMapPackageConverterTests
{
    private const string MapName = "fixture-map";

    [Fact]
    public void GeneratedMapPackageWritesFixturePayloadThatReloadsIntact()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(scratch.Root);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Empty(result.Diagnostics);
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

            Assert.Equal(PreviewPayloadFixture.TerrainFromLattice(chunkX, chunkY), chunk.Heights.Samples.ToArray());
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
    public void GeneratedMapPackageHeightsAgreeWithResidentMaterialization()
    {
        using var unrepresentableScratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(unrepresentableScratch.Root);
        using var unrepresentableReader = PreviewPayloadFixture.OpenPayload(unrepresentableScratch.Root);
        var unrepresentableTarget = Path.Combine(unrepresentableScratch.Root, "package");
        Assert.True(GeneratedMapPackageConverter.Convert(unrepresentableReader, MapName, unrepresentableTarget).Success);
        var unrepresentableModel = new PackageReader(unrepresentableTarget).Read();

        // The fixture's 0.5 game unit Y term is not a multiple of the 0.0390625 quantum, so the bridge rejects it.
        var exception = Assert.Throws<MapImportException>(() => RuntimeBridge.MaterializeHeightMapData(unrepresentableModel));
        Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Code == MapInterchangeDiagnostic.HeightRepresentable);

        using var representableScratch = new PreviewPayloadFixture.ScratchRoot();
        Func<long, long, float> heightOverride = (latticeX, latticeY) => 1000f + (1.25f * latticeX);
        PreviewPayloadFixture.CreatePayload(representableScratch.Root, heightOverride: heightOverride);
        using var representableReader = PreviewPayloadFixture.OpenPayload(representableScratch.Root);
        var representableTarget = Path.Combine(representableScratch.Root, "package");
        Assert.True(GeneratedMapPackageConverter.Convert(representableReader, MapName, representableTarget).Success);
        var representableModel = new PackageReader(representableTarget).Read();

        var expectedKeys = ChunkCoordinates(representableReader.Metadata.Grid.ChunksX, representableReader.Metadata.Grid.ChunksY).ToArray();

        for (var index = 0; index < expectedKeys.Length; index++)
        {
            var (chunkX, chunkY) = expectedKeys[index];

            Assert.Equal(
                PreviewPayloadFixture.TerrainFromLattice(chunkX, chunkY, heightOverride),
                representableModel.Layout.Chunks[index].Heights.Samples.ToArray());
        }

        var heights = RuntimeBridge.MaterializeHeightMapData(representableModel);

        for (var latticeY = 0; latticeY < representableModel.Layout.LatticeHeight; latticeY++)
        {
            for (var latticeX = 0; latticeX < representableModel.Layout.LatticeWidth; latticeX++)
            {
                var resident = heights.Elevations[latticeX, latticeY] * heights.VerticalScale;
                Assert.Equal(heightOverride(latticeX, latticeY), resident);
            }
        }
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

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target);

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
        Assert.Contains(result.Limitations, limitation => limitation.Contains("provenance attribute text", StringComparison.Ordinal));
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

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target);

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

    private static IEnumerable<(int ChunkX, int ChunkY)> ChunkCoordinates(int chunksX, int chunksY)
    {
        return Enumerable.Range(0, chunksY).SelectMany(chunkY => Enumerable.Range(0, chunksX).Select(chunkX => (chunkX, chunkY)));
    }
}
