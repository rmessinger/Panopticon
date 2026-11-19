#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Content.Importing.MapSources.Package;
using Xunit;

namespace Panopticon.Tests.Content.Importing.MapSources.Package;

public sealed class MapPackageVectorLayerTests
{
    [Fact]
    public void InterchangeWithVectorLayer_WritesVersionTwoAndReloadsFeaturesExactly()
    {
        using var scratch = new ScratchRoot();
        var packageDir = Path.Combine(scratch.Root, "vector-v2");
        var original = BuildModel(BuildVectorLayers());

        var write = LegacyMapPackageImporter.Import(original, packageDir);

        Assert.True(write.Success, string.Join("; ", write.Diagnostics));
        var manifest = ReadManifest(packageDir);
        Assert.Equal(MapPackageFormat.VectorPackageVersion, manifest.PackageVersion);
        Assert.NotNull(manifest.VectorLayer);
        Assert.Equal(MapPackageFormat.VectorLayerPath, manifest.VectorLayer!.Path);
        Assert.Equal(MapPackageFormat.EncodingJson, manifest.VectorLayer.Encoding);
        Assert.Equal(original.VectorLayers!.Features.Count, manifest.VectorLayer.FeatureCount);

        var reloaded = new PackageReader(packageDir).Read();

        Assert.NotNull(reloaded.VectorLayers);
        AssertVectorLayersEqual(original.VectorLayers!, reloaded.VectorLayers!);
    }

    [Fact]
    public void VectorFreeInterchange_SerializesVersionOneWithoutVectorLayerMember()
    {
        using var scratch = new ScratchRoot();
        var nullPackage = Path.Combine(scratch.Root, "vector-null");
        var emptyPackage = Path.Combine(scratch.Root, "vector-empty");

        Assert.True(LegacyMapPackageImporter.Import(BuildModel(null), nullPackage).Success);
        Assert.True(LegacyMapPackageImporter.Import(BuildModel(new VectorLayerSet([])), emptyPackage).Success);

        foreach (var packageDir in new[] { nullPackage, emptyPackage })
        {
            var text = File.ReadAllText(Path.Combine(packageDir, MapPackageFormat.ManifestFileName));
            Assert.Contains("\"packageVersion\": 1", text, StringComparison.Ordinal);
            Assert.DoesNotContain("vectorLayer", text, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(packageDir, MapPackageFormat.VectorLayerPath)));
            Assert.Null(new PackageReader(packageDir).Read().VectorLayers);
        }
    }

    [Fact]
    public void SidecarByteFlip_FailsDigestAndYieldsNoModel()
    {
        using var scratch = new ScratchRoot();
        var packageDir = WriteVectorPackage(scratch, "byte-flip");
        var sidecarPath = Path.Combine(packageDir, MapPackageFormat.VectorLayerPath);
        var bytes = File.ReadAllBytes(sidecarPath);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(sidecarPath, bytes);

        var exception = Assert.Throws<MapImportException>(() => new PackageReader(packageDir).Read());

        Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Message.Contains(MapPackageFormat.VectorLayerPath, StringComparison.Ordinal));
    }

    [Fact]
    public void FeatureCountMismatch_FailsWithoutModel()
    {
        using var scratch = new ScratchRoot();
        var packageDir = WriteVectorPackage(scratch, "count-mismatch");
        var manifestPath = Path.Combine(packageDir, MapPackageFormat.ManifestFileName);
        var root = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var count = root["vectorLayer"]!["featureCount"]!.GetValue<int>();
        root["vectorLayer"]!["featureCount"] = JsonValue.Create(count + 1);
        File.WriteAllText(manifestPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exception = Assert.Throws<MapImportException>(() => new PackageReader(packageDir).Read());

        Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Message.Contains("declares", StringComparison.Ordinal));
    }

    [Fact]
    public void VectorLayerUnderVersionOne_FailsManifestValidation()
    {
        using var scratch = new ScratchRoot();
        var packageDir = WriteVectorPackage(scratch, "v1-with-vector");
        var manifestPath = Path.Combine(packageDir, MapPackageFormat.ManifestFileName);
        var root = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        root["packageVersion"] = JsonValue.Create(MapPackageFormat.CurrentPackageVersion);
        File.WriteAllText(manifestPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exception = Assert.Throws<MapImportException>(() => new PackageReader(packageDir).Read());

        Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Message.Contains("carries a vector layer", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownSidecarMember_FailsWithoutModel()
    {
        using var scratch = new ScratchRoot();
        var packageDir = WriteVectorPackage(scratch, "unknown-member");
        var sidecarPath = Path.Combine(packageDir, MapPackageFormat.VectorLayerPath);
        var document = JsonNode.Parse(File.ReadAllBytes(sidecarPath))!.AsObject();
        document["unexpected"] = JsonValue.Create(true);
        var sidecarBytes = System.Text.Encoding.UTF8.GetBytes(document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n");
        File.WriteAllBytes(sidecarPath, sidecarBytes);

        var manifestPath = Path.Combine(packageDir, MapPackageFormat.ManifestFileName);
        var root = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        root["vectorLayer"]!["byteLength"] = JsonValue.Create(sidecarBytes.Length);
        root["vectorLayer"]!["sha256"] = JsonValue.Create(Sha256Hex(sidecarBytes));
        File.WriteAllText(manifestPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var exception = Assert.Throws<MapImportException>(() => new PackageReader(packageDir).Read());

        Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Code == MapPackageDiagnostic.VectorLayer);
    }

    [Fact]
    public void SameModelWrittenTwice_ProducesIdenticalFileTree()
    {
        using var scratch = new ScratchRoot();
        var firstDir = Path.Combine(scratch.Root, "first");
        var secondDir = Path.Combine(scratch.Root, "second");

        Assert.True(LegacyMapPackageImporter.Import(BuildModel(BuildVectorLayers()), firstDir).Success);
        Assert.True(LegacyMapPackageImporter.Import(BuildModel(BuildVectorLayers()), secondDir).Success);

        var firstTree = FileTreeDigests(firstDir);

        Assert.Contains(firstTree, entry => entry.StartsWith(MapPackageFormat.VectorLayerPath + " ", StringComparison.Ordinal));
        Assert.Equal(firstTree, FileTreeDigests(secondDir));
    }

    private static string WriteVectorPackage(ScratchRoot scratch, string name)
    {
        var packageDir = Path.Combine(scratch.Root, name);
        var write = LegacyMapPackageImporter.Import(BuildModel(BuildVectorLayers()), packageDir);
        Assert.True(write.Success, string.Join("; ", write.Diagnostics));

        return packageDir;
    }

    private static VectorLayerSet BuildVectorLayers()
    {
        var road = new VectorLineStringGeometry(
        [
            new VectorPosition(500000.5d, 6000000.25d, 12.5d),
            new VectorPosition(500010d, 6000010d, null),
            new VectorPosition(-0d, 1e-300d, -3d),
        ]);

        var zone = new VectorPolygonGeometry(
            new VectorRing(
            [
                new VectorPosition(500000d, 6000000d, 1d),
                new VectorPosition(500000d, 6000100d, null),
                new VectorPosition(500100d, 6000100d, 2.5d),
                new VectorPosition(500100d, 6000000d, null),
                new VectorPosition(500000d, 6000000d, 1d),
            ]),
            [
                new VectorRing(
                [
                    new VectorPosition(500020d, 6000020d, null),
                    new VectorPosition(500080d, 6000020d, null),
                    new VectorPosition(500080d, 6000080d, null),
                    new VectorPosition(500020d, 6000080d, null),
                    new VectorPosition(500020d, 6000020d, null),
                ]),
            ]);

        var multi = new VectorMultiGeometry(
        [
            new VectorPointGeometry(new VectorPosition(500300d, 6000300d, 9d)),
            new VectorPolygonGeometry(
                new VectorRing(
                [
                    new VectorPosition(500400d, 6000400d, null),
                    new VectorPosition(500420d, 6000400d, null),
                    new VectorPosition(500420d, 6000420d, null),
                    new VectorPosition(500400d, 6000400d, null),
                ]),
                []),
        ]);

        var point = new VectorPointGeometry(new VectorPosition(500512d, 6000512d, 4.25d));

        return new VectorLayerSet(
        [
            new VectorFeature(
                "road-0/vectors/road-0#0",
                "vectors",
                "Annotation",
                -1,
                new Dictionary<string, string> { ["width"] = "7.5", ["name"] = "Main Road" },
                null,
                null,
                null,
                road),
            new VectorFeature(
                "zone-0/vectors/zone-0#0",
                "vectors",
                "Passability",
                0,
                new Dictionary<string, string>(),
                false,
                null,
                null,
                zone),
            new VectorFeature(
                "multi-0/vectors/multi-0#0",
                "vectors",
                "Buildability",
                1,
                new Dictionary<string, string> { ["zone"] = "north" },
                null,
                true,
                null,
                multi),
            new VectorFeature(
                "tree-0/tree-0#0",
                "vectors",
                "ObjectCandidate",
                -1,
                new Dictionary<string, string>(),
                null,
                null,
                null,
                point),
        ]);
    }

    private static MapInterchange BuildModel(VectorLayerSet? vectorLayers)
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
            new MapIdentity("VectorLayerMap", MapSourceKind.Generated, null),
            layout,
            new BlendModel([]),
            null,
            null,
            new Provenance(null, [], []),
            new ImportReport([], [], 0, 0, []),
            [],
            vectorLayers: vectorLayers);
    }

    private static MapPackageManifest ReadManifest(string packageDir)
    {
        var (manifest, diagnostics) = MapPackageManifestJson.ParseAndValidate(File.ReadAllText(Path.Combine(packageDir, MapPackageFormat.ManifestFileName)));
        Assert.Empty(diagnostics);

        return manifest!;
    }

    private static void AssertVectorLayersEqual(VectorLayerSet expected, VectorLayerSet actual)
    {
        Assert.Equal(expected.Features.Count, actual.Features.Count);

        for (var index = 0; index < expected.Features.Count; index++)
        {
            var left = expected.Features[index];
            var right = actual.Features[index];

            Assert.Equal(left.SourceStableId, right.SourceStableId);
            Assert.Equal(left.SourceId, right.SourceId);
            Assert.Equal(left.Kind, right.Kind);
            Assert.Equal(left.RuleIndex, right.RuleIndex);
            Assert.Equal<bool?>(left.Passable, right.Passable);
            Assert.Equal<bool?>(left.Buildable, right.Buildable);
            Assert.Equal(left.Coverage, right.Coverage);
            Assert.Equal(AttributeText(left.Attributes), AttributeText(right.Attributes));
            Assert.Equal(GeometryText(left.Geometry), GeometryText(right.Geometry));
        }
    }

    private static string AttributeText(IReadOnlyDictionary<string, string> attributes) =>
        string.Join(";", attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value));

    private static string GeometryText(VectorGeometry geometry) => geometry switch
    {
        VectorPointGeometry point => "Point(" + PositionText(point.Position) + ")",
        VectorLineStringGeometry line => "LineString(" + string.Join(", ", line.Positions.Select(PositionText)) + ")",
        VectorPolygonGeometry polygon => "Polygon(" + string.Join(", ", new[] { polygon.OuterRing }.Concat(polygon.InnerRings).Select(ring => "(" + string.Join(", ", ring.Positions.Select(PositionText)) + ")")) + ")",
        VectorMultiGeometry multi => "Multi(" + string.Join(", ", multi.Geometries.Select(GeometryText)) + ")",
        _ => throw new InvalidOperationException($"Unexpected vector geometry '{geometry.GetType().Name}'."),
    };

    // Bit patterns keep the comparison exact, including negative zero and subnormals.
    private static string PositionText(VectorPosition position) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{BitConverter.DoubleToInt64Bits(position.X):x16} {BitConverter.DoubleToInt64Bits(position.Y):x16} {(position.AltitudeMeters is { } altitude ? BitConverter.DoubleToInt64Bits(altitude).ToString("x16", CultureInfo.InvariantCulture) : "none")}");

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
