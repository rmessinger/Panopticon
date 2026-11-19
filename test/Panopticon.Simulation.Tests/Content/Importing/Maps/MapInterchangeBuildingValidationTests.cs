using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Panopticon.Content.Importing.Maps;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Maps;

/// <summary>
/// WP1021 group 4: model-level validation of the generated building layer (BuildingLayerSet) on
/// MapInterchange. The synthetic single-chunk model is pure-unit and boots no game files.
/// </summary>
public sealed class MapInterchangeBuildingValidationTests
{
    private const int MaxMeshVerticesPerSegment = 65535;

    [Fact]
    public void ValidBuildingLayerPasses()
    {
        var model = WithLayer(BuildValidModel(), BuildLayer());

        Assert.Empty(MapInterchangeValidator.Validate(model));
    }

    [Fact]
    public void NullBuildingLayerIsSkipped()
    {
        Assert.Null(BuildValidModel().Buildings);
        Assert.Empty(MapInterchangeValidator.Validate(BuildValidModel()));
    }

    [Fact]
    public void NullBuildingEntryFails()
    {
        var layer = BuildLayer([null!]);

        AssertOnlyCode(WithLayer(BuildValidModel(), layer), MapInterchangeDiagnostic.NullComponent);
    }

    [Fact]
    public void NullPartEntryFails()
    {
        var building = BuildBuilding(parts: [null!]);

        AssertOnlyCode(WithBuilding(building), MapInterchangeDiagnostic.NullComponent);
    }

    [Fact]
    public void NullMeshChunkEntryFails()
    {
        var layer = BuildLayer(meshChunks: [null!]);

        AssertOnlyCode(WithLayer(BuildValidModel(), layer), MapInterchangeDiagnostic.NullComponent);
    }

    [Fact]
    public void NullSegmentEntryFails()
    {
        var chunk = BuildMeshChunk(segments: [null!]);

        AssertOnlyCode(WithMeshChunks(chunk), MapInterchangeDiagnostic.NullComponent);
    }

    [Fact]
    public void DuplicateBuildingFails()
    {
        var first = BuildBuilding("osm-way-1", "feature-1");
        var second = BuildBuilding("osm-way-1", "feature-1");

        AssertOnlyCode(WithBuildings(first, second), MapInterchangeDiagnostic.BuildingDuplicate);
    }

    [Fact]
    public void DuplicatePartStableIdFails()
    {
        var first = BuildBuilding("osm-way-1", "feature-1");
        var second = BuildBuilding("osm-way-2", "feature-2");

        AssertOnlyCode(WithBuildings(first, second), MapInterchangeDiagnostic.BuildingDuplicate);
    }

    [Fact]
    public void DuplicateMeshChunkFails()
    {
        AssertOnlyCode(WithMeshChunks(BuildMeshChunk(0, 0), BuildMeshChunk(0, 0)), MapInterchangeDiagnostic.BuildingDuplicate);
    }

    [Fact]
    public void NonPositiveHeightMetersFails()
    {
        AssertOnlyCode(WithBuilding(BuildBuilding(heightMeters: 0)), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void NonFiniteHeightMetersFails()
    {
        AssertOnlyCode(WithBuilding(BuildBuilding(heightMeters: double.NaN)), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void NonPositiveHeightGameUnitsFails()
    {
        AssertOnlyCode(WithBuilding(BuildBuilding(heightGameUnits: 0)), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void UnknownHeightSourceFails()
    {
        AssertOnlyCode(WithBuilding(BuildBuilding(heightSource: "Guessed")), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void UnknownRoofRuleFails()
    {
        AssertOnlyCode(WithBuilding(BuildBuilding(roofRule: "Domed")), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void NegativeBuildingMaterialVariantFails()
    {
        AssertOnlyCode(WithBuilding(BuildBuilding(materialVariant: -1)), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void InvertedSupportRangeFails()
    {
        AssertOnlyCode(WithBuilding(BuildBuilding(supportMinZ: 5.0, supportMaxZ: 1.0)), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void NegativeSinkGameUnitsFails()
    {
        AssertOnlyCode(WithBuilding(BuildBuilding(sinkGameUnits: -0.5)), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void EaveNotAboveBaseFails()
    {
        AssertOnlyCode(WithBuilding(BuildBuilding(baseZ: 3.0, eaveZ: 3.0)), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void BuildingWithoutPartsFails()
    {
        AssertOnlyCode(WithBuilding(BuildBuilding(parts: [])), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void PointFootprintFails()
    {
        var part = new BuildingPart("part-1", new VectorPointGeometry(new VectorPosition(5, 5, 0)), 0, 0);

        AssertOnlyCode(WithPart(part), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void BrokenFootprintRingClosureFails()
    {
        var ring = new VectorRing(
        [
            new VectorPosition(0, 0, 0),
            new VectorPosition(10, 0, 0),
            new VectorPosition(10, 8, 0),
            new VectorPosition(0, 8, 1),
        ]);
        var part = new BuildingPart("part-1", new VectorPolygonGeometry(ring, []), 0, 0);

        AssertOnlyCode(WithPart(part), MapInterchangeDiagnostic.VectorLayer);
    }

    [Fact]
    public void NegativePartChunkFails()
    {
        AssertOnlyCode(WithPart(BuildPart(chunkX: -1)), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void PartWithoutMeshChunkFails()
    {
        AssertOnlyCode(WithPart(BuildPart(chunkX: 3, chunkY: 2)), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void NegativeMeshChunkCoordinateFails()
    {
        AssertOnlyCode(WithMeshChunks(BuildMeshChunk(-1, 0)), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void NegativeSegmentMaterialVariantFails()
    {
        AssertOnlyCode(WithSegment(BuildSegment(materialVariant: -1)), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void EmptySegmentVerticesFails()
    {
        AssertOnlyCode(WithSegment(BuildSegment(vertices: [])), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void OversizedSegmentVertexCountFails()
    {
        var vertices = new BuildingVertex[MaxMeshVerticesPerSegment + 1];
        double maxX = 0;
        double maxY = 0;

        for (var index = 0; index < vertices.Length; index++)
        {
            var x = index % 257;
            var y = index / 257;
            vertices[index] = Vertex(x, y, 0);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        var segment = BuildSegment(vertices: vertices, maxX: maxX, maxY: maxY);

        AssertOnlyCode(WithSegment(segment), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void NonTriangleIndexCountFails()
    {
        AssertOnlyCode(WithSegment(BuildSegment(indices: [0u, 1])), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void SegmentIndexOutOfRangeFails()
    {
        AssertOnlyCode(WithSegment(BuildSegment(indices: [0u, 1, 7])), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void NonFiniteVertexPositionFails()
    {
        var vertices = new List<BuildingVertex>
        {
            Vertex(0, 0, 0),
            Vertex(10, 0, double.NaN),
            Vertex(10, 8, 5),
        };

        AssertOnlyCode(WithSegment(BuildSegment(vertices: vertices)), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void NonFiniteVertexNormalFails()
    {
        var vertices = new List<BuildingVertex>
        {
            Vertex(0, 0, 0),
            new(10, 0, 0, double.NaN, 0, 1),
            Vertex(10, 8, 5),
        };

        AssertOnlyCode(WithSegment(BuildSegment(vertices: vertices)), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void ZeroLengthNormalFails()
    {
        var vertices = new List<BuildingVertex>
        {
            Vertex(0, 0, 0),
            new(10, 0, 0, 0, 0, 0),
            Vertex(10, 8, 5),
        };

        AssertOnlyCode(WithSegment(BuildSegment(vertices: vertices)), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void SegmentBoundsMismatchFails()
    {
        AssertOnlyCode(WithSegment(BuildSegment(maxX: 11)), MapInterchangeDiagnostic.BuildingLayer);
    }

    [Fact]
    public void BuildingLayerConstructionDefensivelyCopiesCallerOwnedState()
    {
        var attributes = new Dictionary<string, string> { ["building"] = "yes" };
        var buildings = new List<GeneratedBuilding>
        {
            new(
                "osm-way-1",
                "feature-1",
                attributes,
                12.5,
                "SourceAttribute",
                41.7,
                "Flat",
                null,
                0,
                "FootprintMedian",
                0.0,
                5.0,
                0.5,
                0.0,
                12.5,
                [BuildPart()]),
        };
        var meshChunks = new List<BuildingMeshChunk> { BuildMeshChunk() };
        var layer = new BuildingLayerSet(buildings, meshChunks);
        var model = WithLayer(BuildValidModel(), layer);

        Assert.Empty(MapInterchangeValidator.Validate(model));

        buildings.Clear();
        meshChunks.Clear();
        attributes["building"] = "mutated";

        Assert.Empty(MapInterchangeValidator.Validate(model));
        Assert.Equal(1, model.Buildings!.Buildings.Count);
        Assert.Equal(1, model.Buildings.MeshChunks.Count);
        Assert.Equal("yes", model.Buildings.Buildings[0].Attributes["building"]);
    }

    private static MapInterchange WithLayer(MapInterchange model, BuildingLayerSet layer) => model with { Buildings = layer };

    private static MapInterchange WithBuildings(params GeneratedBuilding[] buildings) => WithLayer(BuildValidModel(), BuildLayer(buildings));

    private static MapInterchange WithBuilding(GeneratedBuilding building) => WithBuildings(building);

    private static MapInterchange WithPart(BuildingPart part) => WithBuilding(BuildBuilding(parts: [part]));

    private static MapInterchange WithMeshChunks(params BuildingMeshChunk[] chunks) => WithLayer(BuildValidModel(), BuildLayer(meshChunks: chunks));

    private static MapInterchange WithSegment(BuildingMeshSegment segment) => WithMeshChunks(BuildMeshChunk(segments: [segment]));

    private static void AssertOnlyCode(MapInterchange model, string expected)
    {
        Assert.Equal([expected], Codes(model));
    }

    private static string[] Codes(MapInterchange model)
    {
        return MapInterchangeValidator.Validate(model).Select(d => d.Code).Distinct().ToArray();
    }

    private static BuildingLayerSet BuildLayer(IReadOnlyList<GeneratedBuilding>? buildings = null, IReadOnlyList<BuildingMeshChunk>? meshChunks = null) =>
        new(buildings ?? [BuildBuilding()], meshChunks ?? [BuildMeshChunk()]);

    private static GeneratedBuilding BuildBuilding(
        string sourceId = "osm-way-1",
        string featureId = "feature-1",
        double heightMeters = 12.5,
        string heightSource = "SourceAttribute",
        double heightGameUnits = 41.7,
        string roofRule = "Flat",
        int materialVariant = 0,
        double supportMinZ = 0.0,
        double supportMaxZ = 5.0,
        double sinkGameUnits = 0.5,
        double baseZ = 0.0,
        double eaveZ = 12.5,
        IReadOnlyList<BuildingPart>? parts = null) => new(
        sourceId,
        featureId,
        new Dictionary<string, string> { ["building"] = "yes" },
        heightMeters,
        heightSource,
        heightGameUnits,
        roofRule,
        null,
        materialVariant,
        "FootprintMedian",
        supportMinZ,
        supportMaxZ,
        sinkGameUnits,
        baseZ,
        eaveZ,
        parts ?? [BuildPart()]);

    private static BuildingPart BuildPart(string partStableId = "part-1", int chunkX = 0, int chunkY = 0) =>
        new(partStableId, BuildFootprint(), chunkX, chunkY);

    private static VectorPolygonGeometry BuildFootprint()
    {
        var ring = new VectorRing(
        [
            new VectorPosition(0, 0, 0),
            new VectorPosition(10, 0, 0),
            new VectorPosition(10, 8, 0),
            new VectorPosition(0, 8, 0),
            new VectorPosition(0, 0, 0),
        ]);

        return new VectorPolygonGeometry(ring, []);
    }

    private static BuildingVertex Vertex(double x, double y, double z) => new(x, y, z, 0, 0, 1);

    private static BuildingMeshSegment BuildSegment(
        int materialVariant = 0,
        IReadOnlyList<BuildingVertex>? vertices = null,
        IReadOnlyList<uint>? indices = null,
        double minX = 0,
        double minY = 0,
        double minZ = 0,
        double maxX = 10,
        double maxY = 8,
        double maxZ = 5) => new(
        materialVariant,
        vertices ?? [Vertex(0, 0, 0), Vertex(10, 0, 0), Vertex(10, 8, 5)],
        indices ?? [0u, 1, 2],
        minX,
        minY,
        minZ,
        maxX,
        maxY,
        maxZ);

    private static BuildingMeshChunk BuildMeshChunk(int chunkX = 0, int chunkY = 0, IReadOnlyList<BuildingMeshSegment>? segments = null) =>
        new(chunkX, chunkY, segments ?? [BuildSegment()]);

    private const int LatticePoints = 2;

    private static MapInterchange BuildValidModel()
    {
        var layout = new TerrainLayout(
            new MapQuantity(10, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
            LatticePoints,
            LatticePoints,
            1,
            1,
            0,
            new GeospatialTransform(null, null, null, 1.0, null, null, null),
            MapNodataPolicy.Fail,
            3,
            (uint)(LatticePoints * LatticePoints),
            [new MapTerrainBorder(0, 0, (uint)(LatticePoints - 1), (uint)(LatticePoints - 1))],
            [BuildChunk()]);

        var blend = new BlendModel(
        [
            new MaterialPaletteEntry(0, "grass", new AssetReference("terrain", "grass")),
            new MaterialPaletteEntry(1, "rock", new AssetReference("terrain", "rock")),
        ]);

        var objects = new PlacedObjectSet(
        [
            new PlacedObject(
                "object-1",
                new MapQuantity(0, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
                new MapQuantity(0, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
                new MapQuantity(0, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalZ),
                0.0,
                new AssetReference("units", "infantry"),
                0u,
                new Dictionary<string, MapPropertyValue>()),
        ]);

        var waypoints = new WaypointSet(
        [
            new Waypoint(
                "waypoint-1",
                "start",
                new MapQuantity(0, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
                new MapQuantity(1, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
                new MapQuantity(0, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalZ)),
        ]);

        var provenance = new Provenance(
            null,
            [],
            [new GeneratedFeature("feature-1", "lake", new Dictionary<string, string>())]);

        var report = new ImportReport([], [], 0, 0, []);

        var bytes = new byte[] { 1, 2, 3, 4 };

        return new MapInterchange(
            MapInterchange.CurrentSchemaVersion,
            new MapIdentity("synthetic", MapSourceKind.Generated, null),
            layout,
            blend,
            objects,
            waypoints,
            provenance,
            report,
            [new RetainedSection("GlobalVersion", 8, bytes, Sha256Hex(bytes))]);
    }

    private static Chunk BuildChunk()
    {
        // 2 x 2 lattice: one chunk with a 2 x 2 core and 4 x 4 stored samples.
        return new Chunk(
            new ChunkKey(0, 0),
            new HeightLayer(new float[16]),
            new MaterialLayer(new byte[4]),
            new MaskLayer(new byte[4]),
            null);
    }

    private static string Sha256Hex(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
