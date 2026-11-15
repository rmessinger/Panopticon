using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Geospatial;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Interchange;
using Xunit;
using GeneratedFeature = Panopticon.Geospatial.Features.GeneratedFeature;

namespace Panopticon.Geospatial.Preview.Tests;

public sealed class GeneratedBuildingBuilderTests
{
    private const string MapName = "building-map";

    private const double OriginX = 500000d;

    private const double OriginY = 6000000d;

    [Fact]
    public void SedanShapedTwoEmissionPartYieldsOneBuildingWithOnePart()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var attributes = BuildingAttributes(hauteur: 12d, usage: "residential");
        var polygon = GameSquare(100d, 100d, 160d, 160d);
        var features = new[]
        {
            BuildingRegion(GeneratedFeatureKind.Passability, "BATIMENT-0", "sedan-proof-buildings/BATIMENT-0#0", polygon, attributes, ruleIndex: 0),
            BuildingRegion(GeneratedFeatureKind.Buildability, "BATIMENT-0", "sedan-proof-buildings/BATIMENT-0#0", polygon, attributes, ruleIndex: 1),
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var building = Assert.Single(result.Model!.Buildings!.Buildings);
        Assert.Equal("vectors", building.SourceId);
        Assert.Equal("BATIMENT-0", building.FeatureId);
        Assert.Equal(12d, building.HeightMeters);
        Assert.Equal("SourceAttribute", building.HeightSource);
        Assert.Equal("Gabled", building.RoofRule);
        Assert.Null(building.RoofFallbackReason);
        Assert.InRange(building.MaterialVariant, 0, 3);

        var part = Assert.Single(building.Parts);
        Assert.Equal("BATIMENT-0/sedan-proof-buildings/BATIMENT-0#0", part.PartStableId);

        // The two mask emissions still land in the vector layer; provenance keeps one entry.
        var model = new PackageReader(target).Read();
        Assert.Equal(2, model.VectorLayers!.Features.Count);
        Assert.Equal("BATIMENT-0/sedan-proof-buildings/BATIMENT-0#0", Assert.Single(model.Provenance.GeneratedFeatures).StableId);

        Assert.Equal(
            "GEO-PKG-013: Generated 1 buildings from 1 source parts (1 source heights, 0 usage-median fallbacks, 0 global-median fallbacks; roofs: 1 gabled, 0 flat; triangles: 14; mesh chunks: 1).",
            Assert.Single(result.Diagnostics, diagnostic => diagnostic.StartsWith("GEO-PKG-013:", StringComparison.Ordinal)));
    }

    [Fact]
    public void ConvertedBuildingsReloadFromPackageAsVersionThree()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var features = new[]
        {
            BuildingRegion(
                GeneratedFeatureKind.Annotation,
                "BATIMENT-1",
                "sedan-proof-buildings/BATIMENT-1#0",
                GameSquare(100d, 100d, 180d, 160d),
                BuildingAttributes(hauteur: 9.5d, usage: "residential")),
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var original = result.Model!.Buildings!;
        Assert.Single(original.Buildings);
        Assert.True(File.Exists(Path.Combine(target, MapPackageFormat.BuildingsMetadataPath)));

        var (manifest, manifestDiagnostics) = MapPackageManifestJson.ParseAndValidate(
            File.ReadAllText(Path.Combine(target, MapPackageFormat.ManifestFileName)));
        Assert.Empty(manifestDiagnostics);
        Assert.Equal(MapPackageFormat.BuildingPackageVersion, manifest!.PackageVersion);
        Assert.NotNull(manifest.Buildings);

        var reloaded = new PackageReader(target).Read().Buildings!;
        var expected = original.Buildings[0];
        var actual = reloaded.Buildings[0];
        Assert.Equal(expected.SourceId, actual.SourceId);
        Assert.Equal(expected.FeatureId, actual.FeatureId);
        Assert.Equal(expected.HeightMeters, actual.HeightMeters);
        Assert.Equal(expected.HeightSource, actual.HeightSource);
        Assert.Equal(expected.HeightGameUnits, actual.HeightGameUnits);
        Assert.Equal(expected.RoofRule, actual.RoofRule);
        Assert.Equal(expected.RoofFallbackReason, actual.RoofFallbackReason);
        Assert.Equal(expected.MaterialVariant, actual.MaterialVariant);
        Assert.Equal(expected.SeatMethod, actual.SeatMethod);
        Assert.Equal(expected.SupportMinZ, actual.SupportMinZ);
        Assert.Equal(expected.SupportMaxZ, actual.SupportMaxZ);
        Assert.Equal(expected.SinkGameUnits, actual.SinkGameUnits);
        Assert.Equal(expected.BaseZ, actual.BaseZ);
        Assert.Equal(expected.EaveZ, actual.EaveZ);

        Assert.Equal(expected.Parts.Count, actual.Parts.Count);
        Assert.Equal(expected.Parts[0].PartStableId, actual.Parts[0].PartStableId);
        Assert.Equal(expected.Parts[0].ChunkX, actual.Parts[0].ChunkX);
        Assert.Equal(expected.Parts[0].ChunkY, actual.Parts[0].ChunkY);

        Assert.Equal(original.MeshChunks.Count, reloaded.MeshChunks.Count);
        for (var index = 0; index < original.MeshChunks.Count; index++)
        {
            Assert.Equal(original.MeshChunks[index].ChunkX, reloaded.MeshChunks[index].ChunkX);
            Assert.Equal(original.MeshChunks[index].ChunkY, reloaded.MeshChunks[index].ChunkY);
            Assert.Equal(original.MeshChunks[index].Segments.Count, reloaded.MeshChunks[index].Segments.Count);

            for (var segment = 0; segment < original.MeshChunks[index].Segments.Count; segment++)
            {
                Assert.Equal(original.MeshChunks[index].Segments[segment].MaterialVariant, reloaded.MeshChunks[index].Segments[segment].MaterialVariant);
                Assert.Equal(original.MeshChunks[index].Segments[segment].Vertices.Count, reloaded.MeshChunks[index].Segments[segment].Vertices.Count);
                Assert.Equal(original.MeshChunks[index].Segments[segment].Indices.Count, reloaded.MeshChunks[index].Segments[segment].Indices.Count);
            }
        }
    }

    [Fact]
    public void ConcaveAndHoledFootprintsStayFlatWithRecordedReasons()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var attributes = BuildingAttributes(hauteur: 10d, usage: "residential");
        var features = new[]
        {
            BuildingRegion(GeneratedFeatureKind.Annotation, "bat-concave", "c#0", GameLShape(100d, 100d), attributes),
            BuildingRegion(GeneratedFeatureKind.Annotation, "bat-holed", "c#0", GameHoledSquare(300d, 100d), attributes),
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var buildings = result.Model!.Buildings!.Buildings;
        Assert.Equal(2, buildings.Count);
        Assert.Equal("Flat", buildings[0].RoofRule);
        Assert.Equal("concave", buildings[0].RoofFallbackReason);
        Assert.Equal("Flat", buildings[1].RoofRule);
        Assert.Equal("holes", buildings[1].RoofFallbackReason);

        var wallVariants = buildings.Select(building => building.MaterialVariant).ToArray();
        var segmentVariants = result.Model.Buildings.MeshChunks
            .SelectMany(chunk => chunk.Segments)
            .Select(segment => segment.MaterialVariant)
            .ToArray();

        Assert.Contains(segmentVariants, variant => variant < 4);
        Assert.Contains(segmentVariants, variant => variant >= 4);
        Assert.All(wallVariants, variant => Assert.Contains(segmentVariants, candidate => candidate == variant));
        Assert.All(wallVariants, variant => Assert.Contains(segmentVariants, candidate => candidate == variant + 4));
    }

    [Fact]
    public void MissingHauteurFallsBackToUsageThenGlobalMedian()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var features = new[]
        {
            BuildingRegion(GeneratedFeatureKind.Annotation, "bld-a", "a#0", GameSquare(100d, 100d, 160d, 160d), BuildingAttributes(hauteur: 10d, usage: "a")),
            BuildingRegion(GeneratedFeatureKind.Annotation, "bld-b", "b#0", GameSquare(200d, 100d, 260d, 160d), BuildingAttributes(hauteur: 20d, usage: "a")),
            BuildingRegion(GeneratedFeatureKind.Annotation, "bld-c", "c#0", GameSquare(300d, 100d, 360d, 160d), BuildingAttributes(hauteur: null, usage: "a")),
            BuildingRegion(GeneratedFeatureKind.Annotation, "bld-d", "d#0", GameSquare(400d, 100d, 460d, 160d), BuildingAttributes(hauteur: null, usage: "b")),
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var byFeature = result.Model!.Buildings!.Buildings.ToDictionary(building => building.FeatureId);

        Assert.Equal(10d, byFeature["bld-a"].HeightMeters);
        Assert.Equal("SourceAttribute", byFeature["bld-a"].HeightSource);
        Assert.Equal(20d, byFeature["bld-b"].HeightMeters);
        Assert.Equal("SourceAttribute", byFeature["bld-b"].HeightSource);
        Assert.Equal(15d, byFeature["bld-c"].HeightMeters);
        Assert.Equal("UsageMedian", byFeature["bld-c"].HeightSource);
        Assert.Equal(15d, byFeature["bld-d"].HeightMeters);
        Assert.Equal("GlobalMedian", byFeature["bld-d"].HeightSource);

        Assert.Equal(
            "GEO-PKG-013: Generated 4 buildings from 4 source parts (2 source heights, 1 usage-median fallbacks, 1 global-median fallbacks; roofs: 4 gabled, 0 flat; triangles: 56; mesh chunks: 1).",
            Assert.Single(result.Diagnostics, diagnostic => diagnostic.StartsWith("GEO-PKG-013:", StringComparison.Ordinal)));
    }

    [Fact]
    public void UnresolvableHeightFailsWithoutPackage()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var features = new[]
        {
            BuildingRegion(
                GeneratedFeatureKind.Annotation,
                "solo-0",
                "solo#0",
                GameSquare(100d, 100d, 160d, 160d),
                BuildingAttributes(hauteur: null, usage: null)),
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.StartsWith(GeneratedMapPackageDiagnosticCodes.BuildingHeightUnresolved + ":", StringComparison.Ordinal)
                && diagnostic.Contains("vectors/solo-0", StringComparison.Ordinal));
        Assert.Null(result.Model);
        Assert.Null(result.PackageRoot);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void ConflictingSamePartGeometryFailsWithoutPackage()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var attributes = BuildingAttributes(hauteur: 12d, usage: "residential");
        var features = new[]
        {
            BuildingRegion(GeneratedFeatureKind.Passability, "conflict-0", "c#0", GameSquare(100d, 100d, 160d, 160d), attributes, ruleIndex: 0),
            BuildingRegion(GeneratedFeatureKind.Buildability, "conflict-0", "c#0", GameSquare(200d, 100d, 260d, 160d), attributes, ruleIndex: 1),
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.StartsWith(GeneratedMapPackageDiagnosticCodes.BuildingConflict + ":", StringComparison.Ordinal)
                && diagnostic.Contains("conflict-0/c#0", StringComparison.Ordinal));
        Assert.Null(result.Model);
        Assert.Null(result.PackageRoot);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void SlopedTerrainSeatsBuildingBetweenSupportSamples()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var features = new[]
        {
            BuildingRegion(
                GeneratedFeatureKind.Annotation,
                "seat-0",
                "seat#0",
                GameSquare(100d, 100d, 200d, 200d),
                BuildingAttributes(hauteur: 12d, usage: "residential")),
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var building = Assert.Single(result.Model!.Buildings!.Buildings);

        // Support set = lattice points (9..21)^2 (footprint box expanded by one sample);
        // the fixture terrain is strictly increasing, so the extremes sit at the corners.
        // Seating reads the quantized terrain the converter wrote, not the raw payload values.
        Assert.Equal(PreviewPayloadFixture.ResidentGridHeight(PreviewPayloadFixture.GlobalHeight(9, 9)), building.SupportMinZ);
        Assert.Equal(PreviewPayloadFixture.ResidentGridHeight(PreviewPayloadFixture.GlobalHeight(21, 21)), building.SupportMaxZ);
        Assert.NotEqual(PreviewPayloadFixture.GlobalHeight(9, 9), building.SupportMinZ);
        Assert.Equal(building.SupportMinZ - 1d, building.BaseZ);
        Assert.Equal(building.SupportMaxZ + building.HeightGameUnits, building.EaveZ);
        Assert.Equal(1d, building.SinkGameUnits);
        Assert.Equal("ConservativeSkirtCellRange", building.SeatMethod);

        for (var gy = 10; gy <= 20; gy++)
        {
            for (var gx = 10; gx <= 20; gx++)
            {
                var sample = PreviewPayloadFixture.ResidentGridHeight(PreviewPayloadFixture.GlobalHeight(gx, gy));
                Assert.True(building.BaseZ <= sample, $"BaseZ {building.BaseZ} exceeds sample ({gx}, {gy}) = {sample}");
                Assert.True(building.EaveZ >= sample + 12d, $"EaveZ {building.EaveZ} is below sample ({gx}, {gy}) + height = {sample + 12d}");
            }
        }
    }

    [Fact]
    public void SquareWallsFaceAwayFromFootprint()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var features = new[]
        {
            BuildingRegion(GeneratedFeatureKind.Annotation, "wall-square", "wall#0", GameSquare(100d, 100d, 200d, 200d), BuildingAttributes(hauteur: 12d, usage: "residential")),
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var building = Assert.Single(result.Model!.Buildings!.Buildings);
        var wallVertices = result.Model.Buildings!.MeshChunks
            .SelectMany(chunk => chunk.Segments)
            .Where(segment => segment.MaterialVariant < 4)
            .SelectMany(segment => segment.Indices.Select(index => segment.Vertices[(int)index]))
            .ToArray();
        var centreX = (wallVertices.Min(vertex => vertex.X) + wallVertices.Max(vertex => vertex.X)) / 2d;
        var centreY = (wallVertices.Min(vertex => vertex.Y) + wallVertices.Max(vertex => vertex.Y)) / 2d;
        const double tolerance = 1e-9;

        var walls = wallVertices
            .Chunk(3)
            .Where(triangle => triangle.All(vertex => vertex.Z >= building.BaseZ - tolerance && vertex.Z <= building.EaveZ + tolerance)
                && triangle.Any(vertex => vertex.Z <= building.BaseZ + tolerance))
            .ToArray();

        Assert.Equal(8, walls.Length);
        foreach (var triangle in walls)
        {
            var a = triangle[0];
            var b = triangle[1];
            var c = triangle[2];
            var ux = b.X - a.X;
            var uy = b.Y - a.Y;
            var uz = b.Z - a.Z;
            var vx = c.X - a.X;
            var vy = c.Y - a.Y;
            var vz = c.Z - a.Z;
            var normalX = (uy * vz) - (uz * vy);
            var normalY = (uz * vx) - (ux * vz);
            var outwardX = ((a.X + b.X + c.X) / 3d) - centreX;
            var outwardY = ((a.Y + b.Y + c.Y) / 3d) - centreY;

            Assert.True((normalX * outwardX) + (normalY * outwardY) > 0d, $"Wall triangle at ({a.X}, {a.Y}) winds inward.");
            Assert.True((a.NormalX * normalX) + (a.NormalY * normalY) > 0d, $"Wall normal at ({a.X}, {a.Y}) points inward.");
            Assert.Equal(0d, a.NormalZ);
        }
    }

    [Fact]
    public void CrossChunkFootprintAssignsPartsToExpectedChunks()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var attributes = BuildingAttributes(hauteur: 12d, usage: "residential");
        var features = new[]
        {
            BuildingRegion(GeneratedFeatureKind.Annotation, "span-0", "sedan-proof-buildings/span-0#0", GameSquare(1200d, 100d, 1260d, 160d), attributes),
            BuildingRegion(GeneratedFeatureKind.Annotation, "span-0", "sedan-proof-buildings/span-0#1", GameSquare(1300d, 100d, 1360d, 160d), attributes),
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var building = Assert.Single(result.Model!.Buildings!.Buildings);
        Assert.Equal(2, building.Parts.Count);

        Assert.Equal("span-0/sedan-proof-buildings/span-0#0", building.Parts[0].PartStableId);
        Assert.Equal(0, building.Parts[0].ChunkX);
        Assert.Equal(0, building.Parts[0].ChunkY);
        Assert.Equal("span-0/sedan-proof-buildings/span-0#1", building.Parts[1].PartStableId);
        Assert.Equal(1, building.Parts[1].ChunkX);
        Assert.Equal(0, building.Parts[1].ChunkY);

        var meshChunks = result.Model.Buildings!.MeshChunks;
        Assert.Equal(new[] { (0, 0), (1, 0) }, meshChunks.Select(chunk => (chunk.ChunkX, chunk.ChunkY)).ToArray());

        // One square part per chunk: 8 wall + 2 gable-end triangles in the wall variant
        // (30 vertices) and 4 roof-slope triangles in the roof variant (12 vertices) —
        // each part is emitted exactly once.
        foreach (var chunk in meshChunks)
        {
            Assert.Equal(2, chunk.Segments.Count);
            Assert.True(chunk.Segments[0].MaterialVariant < 4);
            Assert.Equal(30, chunk.Segments[0].Vertices.Count);
            Assert.Equal(30, chunk.Segments[0].Indices.Count);
            Assert.Equal(chunk.Segments[0].MaterialVariant + 4, chunk.Segments[1].MaterialVariant);
            Assert.Equal(12, chunk.Segments[1].Vertices.Count);
            Assert.Equal(12, chunk.Segments[1].Indices.Count);
        }
    }

    [Fact]
    public void BuildingConversionsProduceByteIdenticalPackages()
    {
        List<GeneratedFeature> Features() => new[]
        {
            BuildingRegion(
                GeneratedFeatureKind.Annotation,
                "bat-gabled",
                "g#0",
                GameSquare(100d, 100d, 160d, 160d),
                BuildingAttributes(hauteur: 12d, usage: "residential")),
            BuildingRegion(
                GeneratedFeatureKind.Annotation,
                "bat-flat",
                "f#0",
                GameLShape(300d, 100d),
                BuildingAttributes(hauteur: 8d, usage: "industrial")),
        }.ToList();

        using var firstScratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(firstScratch.Root, features: Features());
        var firstTarget = Path.Combine(firstScratch.Root, "package");

        using var secondScratch = new PreviewPayloadFixture.ScratchRoot();
        PreviewPayloadFixture.CreatePayload(secondScratch.Root, features: Features());
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

        Assert.Contains(firstTree, entry => entry.Contains("buildings/buildings.json", StringComparison.Ordinal));
        Assert.Equal(firstTree, secondTree);
    }

    [Fact]
    public void MultiPartSourceFeatureYieldsOneBuildingWithTwoParts()
    {
        using var scratch = new PreviewPayloadFixture.ScratchRoot();
        var attributes = BuildingAttributes(hauteur: 12d, usage: "residential");
        var features = new[]
        {
            BuildingRegion(GeneratedFeatureKind.Annotation, "multi-0", "multi-0#0", GameSquare(100d, 100d, 160d, 160d), attributes),
            BuildingRegion(GeneratedFeatureKind.Annotation, "multi-0", "multi-0#1", GameSquare(200d, 100d, 260d, 160d), attributes),
        }.ToList();
        PreviewPayloadFixture.CreatePayload(scratch.Root, features: features);
        using var reader = PreviewPayloadFixture.OpenPayload(scratch.Root);
        var target = Path.Combine(scratch.Root, "package");

        var result = GeneratedMapPackageConverter.Convert(reader, MapName, target, PreviewPayloadFixture.DefaultTextureMap());

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var building = Assert.Single(result.Model!.Buildings!.Buildings);
        Assert.Equal("vectors", building.SourceId);
        Assert.Equal("multi-0", building.FeatureId);
        Assert.Equal(2, building.Parts.Count);
        Assert.Equal("multi-0/multi-0#0", building.Parts[0].PartStableId);
        Assert.Equal("multi-0/multi-0#1", building.Parts[1].PartStableId);
        Assert.All(building.Parts, part => Assert.Equal((0, 0), (part.ChunkX, part.ChunkY)));
    }

    private static GeoPolygon GameSquare(double x0, double y0, double x1, double y1) =>
        new(Ring((OriginX + x0, OriginY + y0), (OriginX + x1, OriginY + y0), (OriginX + x1, OriginY + y1), (OriginX + x0, OriginY + y1), (OriginX + x0, OriginY + y0)));

    private static GeoPolygon GameLShape(double offsetX, double offsetY) =>
        new(Ring(
            (OriginX + offsetX, OriginY + offsetY),
            (OriginX + offsetX + 100d, OriginY + offsetY),
            (OriginX + offsetX + 100d, OriginY + offsetY + 40d),
            (OriginX + offsetX + 60d, OriginY + offsetY + 40d),
            (OriginX + offsetX + 60d, OriginY + offsetY + 100d),
            (OriginX + offsetX, OriginY + offsetY + 100d),
            (OriginX + offsetX, OriginY + offsetY)));

    private static GeoPolygon GameHoledSquare(double offsetX, double offsetY)
    {
        var outer = Ring(
            (OriginX + offsetX, OriginY + offsetY),
            (OriginX + offsetX + 100d, OriginY + offsetY),
            (OriginX + offsetX + 100d, OriginY + offsetY + 100d),
            (OriginX + offsetX, OriginY + offsetY + 100d),
            (OriginX + offsetX, OriginY + offsetY));
        var hole = Ring(
            (OriginX + offsetX + 20d, OriginY + offsetY + 20d),
            (OriginX + offsetX + 80d, OriginY + offsetY + 20d),
            (OriginX + offsetX + 80d, OriginY + offsetY + 80d),
            (OriginX + offsetX + 20d, OriginY + offsetY + 80d),
            (OriginX + offsetX + 20d, OriginY + offsetY + 20d));

        return new GeoPolygon(outer, [hole]);
    }

    private static GeoRing Ring(params (double X, double Y)[] positions)
    {
        var ring = positions.Select(position => new GeoPosition(new Vector2d(position.X, position.Y))).ToList();
        ring.Add(ring[0]);

        return new GeoRing(ring);
    }

    private static IReadOnlyDictionary<string, string> BuildingAttributes(double? hauteur = 12d, string? usage = "residential", string cleabs = "123456")
    {
        var attributes = new Dictionary<string, string> { ["cleabs"] = cleabs };

        if (hauteur is { } height)
        {
            attributes["hauteur"] = height.ToString(CultureInfo.InvariantCulture);
        }

        if (usage is not null)
        {
            attributes["usage_1"] = usage;
        }

        return attributes;
    }

    private static GeneratedFeature BuildingRegion(
        GeneratedFeatureKind kind,
        string featureId,
        string partId,
        GeoPolygon polygon,
        IReadOnlyDictionary<string, string> attributes,
        int ruleIndex = 0) => kind switch
        {
            GeneratedFeatureKind.Passability => new GeneratedFeature(kind, polygon, "vectors", featureId, partId, ruleIndex, attributes, passable: false),
            GeneratedFeatureKind.Buildability => new GeneratedFeature(kind, polygon, "vectors", featureId, partId, ruleIndex, attributes, buildable: false),
            _ => new GeneratedFeature(kind, polygon, "vectors", featureId, partId, ruleIndex, attributes),
        };

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
