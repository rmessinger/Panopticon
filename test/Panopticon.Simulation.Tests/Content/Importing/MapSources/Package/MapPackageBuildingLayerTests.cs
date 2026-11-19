#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Content.Importing.MapSources.Package;
using Xunit;

namespace Panopticon.Tests.Content.Importing.MapSources.Package;

public sealed class MapPackageBuildingLayerTests
{
    private const string BuildingsPath = "buildings/buildings.json";

    private const string MeshPath = "buildings/chunk-0-0.bin";

    [Fact]
    public void BuildingLayer_WritesVersionThreeAndReloadsWithIdenticalBytes()
    {
        using var scratch = new ScratchRoot();
        var firstDir = Path.Combine(scratch.Root, "first");
        var write = LegacyMapPackageImporter.Import(BuildModel(BuildBuildingLayer()), firstDir);

        Assert.True(write.Success, string.Join("; ", write.Diagnostics));

        var manifest = ReadManifest(firstDir);
        Assert.Equal(MapPackageFormat.BuildingPackageVersion, manifest.PackageVersion);
        Assert.NotNull(manifest.Buildings);
        Assert.Equal(BuildingsPath, manifest.Buildings!.Path);
        Assert.Equal(MapPackageFormat.EncodingJson, manifest.Buildings.Encoding);
        Assert.Equal(1, manifest.Buildings.BuildingCount);
        Assert.Single(manifest.Buildings.MeshChunks);

        var mesh = manifest.Buildings.MeshChunks[0];
        Assert.Equal(MeshPath, mesh.Path);
        Assert.Equal(MapPackageFormat.EncodingPbm1, mesh.Encoding);
        Assert.Equal(1, mesh.SegmentCount);
        Assert.Equal(4, mesh.VertexCount);
        Assert.Equal(6, mesh.IndexCount);

        var tree = FileTreeDigests(firstDir);
        Assert.Contains(tree, entry => entry.StartsWith(BuildingsPath + " ", StringComparison.Ordinal));
        Assert.Contains(tree, entry => entry.StartsWith(MeshPath + " ", StringComparison.Ordinal));

        var reloaded = new PackageReader(firstDir).Read();

        Assert.NotNull(reloaded.Buildings);
        AssertBuildingLayersEqual(BuildBuildingLayer(), reloaded.Buildings!);

        var secondDir = Path.Combine(scratch.Root, "second");
        Assert.True(LegacyMapPackageImporter.Import(reloaded, secondDir).Success);
        Assert.Equal(File.ReadAllBytes(Path.Combine(firstDir, BuildingsPath)), File.ReadAllBytes(Path.Combine(secondDir, BuildingsPath)));
        Assert.Equal(File.ReadAllBytes(Path.Combine(firstDir, MeshPath)), File.ReadAllBytes(Path.Combine(secondDir, MeshPath)));
    }

    [Fact]
    public void EmptyBuildingLayer_OmitsBuildingsMemberAndKeepsVersionOne()
    {
        using var scratch = new ScratchRoot();
        var packageDir = Path.Combine(scratch.Root, "empty-layer");

        Assert.True(LegacyMapPackageImporter.Import(BuildModel(new BuildingLayerSet([], [])), packageDir).Success);

        var text = File.ReadAllText(Path.Combine(packageDir, MapPackageFormat.ManifestFileName));
        Assert.Contains("\"packageVersion\": 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"buildings\"", text, StringComparison.Ordinal);
        Assert.Null(new PackageReader(packageDir).Read().Buildings);
    }

    [Fact]
    public void ZeroSegmentMeshChunk_FailsWriteWithBuildingDiagnostic()
    {
        using var scratch = new ScratchRoot();
        var layer = new BuildingLayerSet(BuildBuildingLayer().Buildings, [new BuildingMeshChunk(0, 0, [])]);

        var write = LegacyMapPackageImporter.Import(BuildModel(layer), Path.Combine(scratch.Root, "zero-segment"));

        Assert.False(write.Success);
        Assert.Contains(write.Diagnostics, diagnostic => diagnostic.Code == MapPackageDiagnostic.BuildingLayer);
    }

    [Fact]
    public void MeshByteFlip_FailsDigestAndYieldsNoModel()
    {
        using var scratch = new ScratchRoot();
        var packageDir = WriteBuildingPackage(scratch, "mesh-flip");
        var meshFile = Path.Combine(packageDir, MeshPath);
        var bytes = File.ReadAllBytes(meshFile);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(meshFile, bytes);

        var exception = Assert.Throws<MapImportException>(() => new PackageReader(packageDir).Read());

        Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Message.Contains(MeshPath, StringComparison.Ordinal));
    }

    [Fact]
    public void MetadataWithUnknownMember_FailsWithoutModel()
    {
        using var scratch = new ScratchRoot();
        var packageDir = WriteBuildingPackage(scratch, "metadata-unknown");
        var metadataFile = Path.Combine(packageDir, BuildingsPath);
        var document = JsonNode.Parse(File.ReadAllBytes(metadataFile))!.AsObject();
        document["unexpected"] = JsonValue.Create(true);
        var metadataBytes = Encoding.UTF8.GetBytes(document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n");
        File.WriteAllBytes(metadataFile, metadataBytes);

        var manifestPath = Path.Combine(packageDir, MapPackageFormat.ManifestFileName);
        var root = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        root["buildings"]!["byteLength"] = JsonValue.Create(metadataBytes.Length);
        root["buildings"]!["sha256"] = JsonValue.Create(Sha256Hex(metadataBytes));
        File.WriteAllText(manifestPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exception = Assert.Throws<MapImportException>(() => new PackageReader(packageDir).Read());

        Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Code == MapPackageDiagnostic.BuildingLayer);
    }

    [Fact]
    public void BuildingLayerUnderVersionTwo_FailsManifestValidation()
    {
        using var scratch = new ScratchRoot();
        var packageDir = WriteBuildingPackage(scratch, "v2-with-buildings");
        var manifestPath = Path.Combine(packageDir, MapPackageFormat.ManifestFileName);
        var root = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        root["packageVersion"] = JsonValue.Create(MapPackageFormat.VectorPackageVersion);
        File.WriteAllText(manifestPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exception = Assert.Throws<MapImportException>(() => new PackageReader(packageDir).Read());

        Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Message.Contains("carries a building layer", StringComparison.Ordinal));
    }

    [Fact]
    public void MeshDecode_RejectsBadMagicTruncationAndOutOfRangeIndex()
    {
        var bytes = MapPackageBuildingCodec.EncodeMesh(BuildBuildingLayer().MeshChunks[0]);

        var badMagic = (byte[])bytes.Clone();
        badMagic[0] = (byte)'X';
        Assert.Throws<FormatException>(() => MapPackageBuildingCodec.DecodeMesh(badMagic, 0, 0));

        Assert.Throws<FormatException>(() => MapPackageBuildingCodec.DecodeMesh(bytes[..^1], 0, 0));

        var outOfRange = (byte[])bytes.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(outOfRange.AsSpan(outOfRange.Length - 4), 99u);
        Assert.Throws<FormatException>(() => MapPackageBuildingCodec.DecodeMesh(outOfRange, 0, 0));
    }

    private static string WriteBuildingPackage(ScratchRoot scratch, string name)
    {
        var packageDir = Path.Combine(scratch.Root, name);
        var write = LegacyMapPackageImporter.Import(BuildModel(BuildBuildingLayer()), packageDir);
        Assert.True(write.Success, string.Join("; ", write.Diagnostics));

        return packageDir;
    }

    private static BuildingLayerSet BuildBuildingLayer()
    {
        var footprint = new VectorPolygonGeometry(
            new VectorRing(
            [
                new VectorPosition(0d, 0d, null),
                new VectorPosition(4d, 0d, null),
                new VectorPosition(4d, 4d, null),
                new VectorPosition(0d, 4d, null),
                new VectorPosition(0d, 0d, null),
            ]),
            []);

        var building = new GeneratedBuilding(
            "osm-way-1",
            "bldg-0",
            new Dictionary<string, string> { ["building"] = "yes" },
            12d,
            "SourceAttribute",
            12d,
            "Flat",
            null,
            1,
            "FootprintMedian",
            0d,
            2d,
            0.5d,
            0d,
            12d,
            [new BuildingPart("part-0", footprint, 0, 0)]);

        var segment = new BuildingMeshSegment(
            1,
            [
                new BuildingVertex(0d, 0d, 0d, 0d, 0d, 1d),
                new BuildingVertex(4d, 0d, 0d, 0d, 0d, 1d),
                new BuildingVertex(4d, 4d, 0d, 0d, 0d, 1d),
                new BuildingVertex(0d, 4d, 0d, 0d, 0d, 1d),
            ],
            [0u, 1u, 2u, 0u, 2u, 3u],
            0d,
            0d,
            0d,
            4d,
            4d,
            0d);

        return new BuildingLayerSet([building], [new BuildingMeshChunk(0, 0, [segment])]);
    }

    private static MapInterchange BuildModel(BuildingLayerSet? buildings)
    {
        const int latticePoints = 129;
        const int storedExtent = latticePoints + 2;

        var heights = new float[storedExtent * storedExtent];

        for (var i = 0; i < heights.Length; i++)
        {
            heights[i] = 25.0f + (i * 0.125f);
        }

        var chunk = new Chunk(new ChunkKey(0, 0), new HeightLayer(heights), null, null, null);

        var layout = new TerrainLayout(
            new MapQuantity(10, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
            latticePoints,
            latticePoints,
            1,
            1,
            6,
            null!,
            MapNodataPolicy.Fail,
            3,
            latticePoints * latticePoints,
            [new MapTerrainBorder(0, 0, latticePoints - 1, latticePoints - 1)],
            [chunk]);

        return new MapInterchange(
            MapInterchange.CurrentSchemaVersion,
            new MapIdentity("BuildingLayerMap", MapSourceKind.Generated, null),
            layout,
            new BlendModel([]),
            null,
            null,
            new Provenance(null, [], []),
            new ImportReport([], [], 0, 0, []),
            [],
            buildings: buildings);
    }

    private static MapPackageManifest ReadManifest(string packageDir)
    {
        var (manifest, diagnostics) = MapPackageManifestJson.ParseAndValidate(File.ReadAllText(Path.Combine(packageDir, MapPackageFormat.ManifestFileName)));
        Assert.Empty(diagnostics);

        return manifest!;
    }

    private static void AssertBuildingLayersEqual(BuildingLayerSet expected, BuildingLayerSet actual)
    {
        Assert.Equal(expected.Buildings.Count, actual.Buildings.Count);

        for (var index = 0; index < expected.Buildings.Count; index++)
        {
            var left = expected.Buildings[index];
            var right = actual.Buildings[index];

            Assert.Equal(left.SourceId, right.SourceId);
            Assert.Equal(left.FeatureId, right.FeatureId);
            Assert.Equal(AttributeText(left.Attributes), AttributeText(right.Attributes));
            Assert.Equal(Bits(left.HeightMeters), Bits(right.HeightMeters));
            Assert.Equal(Bits(left.HeightGameUnits), Bits(right.HeightGameUnits));
            Assert.Equal(left.HeightSource, right.HeightSource);
            Assert.Equal(left.RoofRule, right.RoofRule);
            Assert.Equal(left.RoofFallbackReason, right.RoofFallbackReason);
            Assert.Equal(left.MaterialVariant, right.MaterialVariant);
            Assert.Equal(left.SeatMethod, right.SeatMethod);
            Assert.Equal(Bits(left.SupportMinZ), Bits(right.SupportMinZ));
            Assert.Equal(Bits(left.SupportMaxZ), Bits(right.SupportMaxZ));
            Assert.Equal(Bits(left.SinkGameUnits), Bits(right.SinkGameUnits));
            Assert.Equal(Bits(left.BaseZ), Bits(right.BaseZ));
            Assert.Equal(Bits(left.EaveZ), Bits(right.EaveZ));
            Assert.Equal(left.Parts.Count, right.Parts.Count);

            for (var part = 0; part < left.Parts.Count; part++)
            {
                Assert.Equal(left.Parts[part].PartStableId, right.Parts[part].PartStableId);
                Assert.Equal(left.Parts[part].ChunkX, right.Parts[part].ChunkX);
                Assert.Equal(left.Parts[part].ChunkY, right.Parts[part].ChunkY);
                Assert.Equal(GeometryText(left.Parts[part].Footprint), GeometryText(right.Parts[part].Footprint));
            }
        }

        Assert.Equal(expected.MeshChunks.Count, actual.MeshChunks.Count);

        for (var chunkIndex = 0; chunkIndex < expected.MeshChunks.Count; chunkIndex++)
        {
            var left = expected.MeshChunks[chunkIndex];
            var right = actual.MeshChunks[chunkIndex];

            Assert.Equal(left.ChunkX, right.ChunkX);
            Assert.Equal(left.ChunkY, right.ChunkY);
            Assert.Equal(left.Segments.Count, right.Segments.Count);

            for (var segmentIndex = 0; segmentIndex < left.Segments.Count; segmentIndex++)
            {
                var leftSegment = left.Segments[segmentIndex];
                var rightSegment = right.Segments[segmentIndex];

                Assert.Equal(leftSegment.MaterialVariant, rightSegment.MaterialVariant);
                Assert.Equal(string.Join(",", leftSegment.Indices), string.Join(",", rightSegment.Indices));
                Assert.Equal(
                    string.Join(";", leftSegment.Vertices.Select(VertexText)),
                    string.Join(";", rightSegment.Vertices.Select(VertexText)));
                Assert.Equal(
                    string.Join(" ", new[] { leftSegment.MinX, leftSegment.MinY, leftSegment.MinZ, leftSegment.MaxX, leftSegment.MaxY, leftSegment.MaxZ }.Select(Bits)),
                    string.Join(" ", new[] { rightSegment.MinX, rightSegment.MinY, rightSegment.MinZ, rightSegment.MaxX, rightSegment.MaxY, rightSegment.MaxZ }.Select(Bits)));
            }
        }
    }

    private static string AttributeText(IReadOnlyDictionary<string, string> attributes) =>
        string.Join(";", attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value));

    private static string VertexText(BuildingVertex vertex) =>
        string.Join(" ", new[] { vertex.X, vertex.Y, vertex.Z, vertex.NormalX, vertex.NormalY, vertex.NormalZ }.Select(Bits));

    private static string Bits(double value) => BitConverter.DoubleToInt64Bits(value).ToString("x16", CultureInfo.InvariantCulture);

    private static string GeometryText(VectorGeometry geometry) => geometry switch
    {
        VectorPointGeometry point => "Point(" + PositionText(point.Position) + ")",
        VectorLineStringGeometry line => "LineString(" + string.Join(", ", line.Positions.Select(PositionText)) + ")",
        VectorPolygonGeometry polygon => "Polygon(" + string.Join(", ", new[] { polygon.OuterRing }.Concat(polygon.InnerRings).Select(ring => "(" + string.Join(", ", ring.Positions.Select(PositionText)) + ")")) + ")",
        VectorMultiGeometry multi => "Multi(" + string.Join(", ", multi.Geometries.Select(GeometryText)) + ")",
        _ => throw new InvalidOperationException($"Unexpected vector geometry '{geometry.GetType().Name}'."),
    };

    private static string PositionText(VectorPosition position) =>
        Bits(position.X) + " " + Bits(position.Y) + " " + (position.AltitudeMeters is { } altitude ? Bits(altitude) : "none");

    private static IReadOnlyList<string> FileTreeDigests(string packageRoot)
    {
        return Directory
            .EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories)
            .Select(file => (Path: Path.GetRelativePath(packageRoot, file).Replace('\\', '/'), Digest: Sha256Hex(File.ReadAllBytes(file))))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .Select(entry => $"{entry.Path} {entry.Digest}")
            .ToArray();
    }

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class ScratchRoot : IDisposable
    {
        public ScratchRoot()
        {
            Root = Path.Combine(Path.GetTempPath(), "PanopticonTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
