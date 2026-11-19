#nullable enable

// V2 byte-compatibility baseline for the building-layer writer. The fixtures below were produced
// by the accepted group-3 writer so any change to v2 package bytes fails here.
//
// Fixture provenance:
//   Source commit: fa7c71099 (group-3 vector-layer writer, detached throwaway worktree
//     /home/rmessinger/dev/Panopticon-r3-baseline-v2, removed after capture).
//   Generation: a temporary BaselineCaptureV2Tests (not committed) wrote the package through
//     LegacyMapPackageImporter.Import with R3_CAPTURE_DIR set, once per run directory:
//     R3_CAPTURE_DIR=~/log/r3-g4-v2-baseline/run1 dotnet test test/Panopticon.Simulation.Tests --filter BaselineCaptureV2Tests
//     R3_CAPTURE_DIR=~/log/r3-g4-v2-baseline/run2 dotnet test test/Panopticon.Simulation.Tests --filter BaselineCaptureV2Tests --no-build
//   Double-capture determinism: run1 and run2 manifest.json and tree.txt compare equal with cmp,
//     and the package trees are identical with diff -r.
//   Input model: the v1 byte-compatibility baseline model (single-chunk 129x129, heights, materials,
//     masks, one placed object, one waypoint, provenance, one retained section) with the four-feature
//     vector layer from BuildVectorLayers below.
//   Fixture manifest SHA-256: d9062a5c49da1db028f20bd4b687a6b219117c3b49311ab145090bb16380c31d
//   Fixture tree list SHA-256: f68e45cf26fa03b51ef8de89038cd204feb0540bb0b8f0588678c5fee6ecc61d

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Content.Importing.MapSources.Package;
using Xunit;

namespace Panopticon.Tests.Content.Importing.MapSources.Package;

public sealed class MapPackageV2ByteCompatibilityTests
{
    [Fact]
    public void VectorPresentImport_ManifestBytesMatchGroup3Baseline()
    {
        using var scratch = new ScratchRoot();
        var packageDir = Path.Combine(scratch.Root, "v2-baseline");
        var writeResult = LegacyMapPackageImporter.Import(BuildBaselineModel(), packageDir);

        Assert.True(writeResult.Success, string.Join("; ", writeResult.Diagnostics));

        var actualBytes = File.ReadAllBytes(Path.Combine(packageDir, MapPackageFormat.ManifestFileName));
        var expectedBytes = File.ReadAllBytes(FixturePath("V2BaselineManifest.json"));

        Assert.True(
            actualBytes.AsSpan().SequenceEqual(expectedBytes),
            $"manifest.json bytes differ from the group-3 baseline: actual sha256 {Sha256Hex(actualBytes)} " +
            $"({actualBytes.Length} bytes), expected sha256 {Sha256Hex(expectedBytes)} ({expectedBytes.Length} bytes).");

        var text = System.Text.Encoding.UTF8.GetString(actualBytes);
        Assert.Contains("\"packageVersion\": 2", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"buildings\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void VectorPresentImport_FileTreeMatchesGroup3Baseline()
    {
        using var scratch = new ScratchRoot();
        var packageDir = Path.Combine(scratch.Root, "v2-baseline-tree");
        var writeResult = LegacyMapPackageImporter.Import(BuildBaselineModel(), packageDir);

        Assert.True(writeResult.Success, string.Join("; ", writeResult.Diagnostics));

        var actualTree = FileTreeDigests(packageDir);
        var expectedTree = NormalizeLines(File.ReadAllText(FixturePath("V2BaselineTree.txt")));

        Assert.Equal(expectedTree, actualTree);
    }

    private static MapInterchange BuildBaselineModel()
    {
        const int latticePoints = 129;
        const int coreExtent = latticePoints;
        const int storedExtent = coreExtent + 2;

        var heights = new float[storedExtent * storedExtent];

        for (var i = 0; i < heights.Length; i++)
        {
            heights[i] = 12.5f + ((i % 17) * 0.5f) + ((i / 17) * 0.125f);
        }

        var materials = new byte[coreExtent * coreExtent];

        for (var i = 0; i < materials.Length; i++)
        {
            materials[i] = (byte)((i * 7) % 3);
        }

        var masks = new byte[coreExtent * coreExtent];

        for (var i = 0; i < masks.Length; i++)
        {
            masks[i] = (byte)(i % 4);
        }

        var chunk = new Chunk(
            new ChunkKey(0, 0),
            new HeightLayer(heights),
            new MaterialLayer(materials),
            new MaskLayer(masks),
            null);

        var layout = new TerrainLayout(
            new MapQuantity(10, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
            latticePoints,
            latticePoints,
            1,
            1,
            0,
            null!,
            MapNodataPolicy.Fail,
            3,
            latticePoints * latticePoints,
            [new MapTerrainBorder(0, 0, latticePoints - 1, latticePoints - 1)],
            [chunk]);

        var blend = new BlendModel([
            new MaterialPaletteEntry(0, "grass", new AssetReference("terrain", "grass")),
            new MaterialPaletteEntry(1, "dirt", new AssetReference("terrain", "dirt")),
            new MaterialPaletteEntry(2, "rock", new AssetReference("terrain", "rock")),
        ]);

        var objects = new PlacedObjectSet([
            new PlacedObject(
                "r3-obj-0",
                new MapQuantity(10, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineXy),
                new MapQuantity(20, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineXy),
                new MapQuantity(0, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineZ),
                0.0,
                new AssetReference("legacy", "AmericaVehicleCrusader"),
                0u,
                new Dictionary<string, MapPropertyValue> { ["team"] = new MapPropertyValue.AsciiStringValue("america") }),
        ]);

        var waypoints = new WaypointSet([
            new Waypoint(
                "r3-wp-0",
                "R3Start",
                new MapQuantity(15, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineXy),
                new MapQuantity(25, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineXy),
                new MapQuantity(0, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineZ)),
        ]);

        var provenance = new Provenance(
            "8",
            [
                new SourceRecord(
                    "legacy-map",
                    ["3f0a0e8db1e2c9de59a2f2b40dbe5c78a5b1e9f0d4c8a3b2e1f0d9c8b7a6f5e4"],
                    new MapQuantity(0, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineXy),
                    new MapQuantity(0, MapQuantityUnit.GameUnit, MapQuantityFrame.LegacyEngineXy)),
            ],
            [
                new GeneratedFeature(
                    "r3-gf-0",
                    "RoadCenterLine",
                    new Dictionary<string, string> { ["lanes"] = "2" }),
            ]);

        var report = new ImportReport([], [], 0, 0, []);
        var retainedBytes = new byte[] { 0x5A, 0xA5, 0x01, 0xFE };
        var retained = new RetainedSection("GlobalVersion", 8, retainedBytes, Sha256Hex(retainedBytes));

        return new MapInterchange(
            MapInterchange.CurrentSchemaVersion,
            new MapIdentity("R3V1ByteBaseline", MapSourceKind.LegacyMap, 8),
            layout,
            blend,
            objects,
            waypoints,
            provenance,
            report,
            [retained],
            vectorLayers: BuildVectorLayers());
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

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Content", "Importing", "MapSources", "Package", "Fixtures", fileName);

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static IReadOnlyList<string> FileTreeDigests(string packageRoot)
    {
        return Directory
            .EnumerateFiles(packageRoot, "*", SearchOption.AllDirectories)
            .Select(file => (Path: Path.GetRelativePath(packageRoot, file).Replace('\\', '/'), Digest: Sha256Hex(File.ReadAllBytes(file))))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .Select(entry => $"{entry.Path} {entry.Digest}")
            .ToArray();
    }

    private static string[] NormalizeLines(string text) =>
        text.Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
