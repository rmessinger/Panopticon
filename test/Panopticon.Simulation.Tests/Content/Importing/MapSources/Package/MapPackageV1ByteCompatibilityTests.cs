#nullable enable

// V1 byte-compatibility baseline for the WP1020 group-3 vector-layer writer. The fixtures below
// were produced by the accepted group-2 writer so any change to v1 package bytes fails here
// instead of silently re-baselining the format.
//
// Fixture provenance:
//   Source commit: 93821be3cc06937a6d47996ada3cb1c6b52e214b (group-2 writer, detached
//     throwaway worktree /home/rmessinger/dev/Panopticon-r3-baseline-v1).
//   Generation command (run twice, once per capture directory):
//     R3_CAPTURE_DIR=/tmp/r3-v1-baseline-capture/run1 dotnet test test/Panopticon.Simulation.Tests --filter BaselineCapture
//     R3_CAPTURE_DIR=/tmp/r3-v1-baseline-capture/run2 dotnet test test/Panopticon.Simulation.Tests --filter BaselineCapture --no-build
//   Double-capture determinism: run1 and run2 are byte-identical (manifest.json and tree.txt
//     compare equal with cmp; full package trees identical with diff -r).
//   Input model: single-chunk 129x129 vector-free interchange with non-constant heights,
//     materials and masks layers, a 3-entry material palette, one placed object, one waypoint,
//     provenance with one source record and one generated feature, border 0, and one retained
//     section. Built by BuildBaselineModel below, byte-identical to the capture test's builder
//     except for the MapInterchange constructor call (the baseline commit has no vectorLayers
//     parameter; here it is passed explicitly as null).
//   Fixture manifest SHA-256: 3ca94c9660bd204f0c50ef34c428a9ed41a2c8bf410503cc8b4296359d0d61e0
//   Fixture tree list SHA-256: 6e0adf96ac06aed21a8311413be725393e4ad7232346aa7f519dea1957778e9f

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

/// <summary>
/// Proves the current writer emits byte-identical v1 packages to the accepted group-2 writer
/// for vector-free models, against fixtures captured from commit 93821be3c.
/// </summary>
public sealed class MapPackageV1ByteCompatibilityTests
{
    [Fact]
    public void VectorFreeImport_ManifestBytesMatchGroup2Baseline()
    {
        using var scratch = new ScratchRoot();
        var packageDir = Path.Combine(scratch.Root, "v1-baseline");
        var writeResult = LegacyMapPackageImporter.Import(BuildBaselineModel(), packageDir);

        Assert.True(writeResult.Success, string.Join("; ", writeResult.Diagnostics));

        var actualBytes = File.ReadAllBytes(Path.Combine(packageDir, MapPackageFormat.ManifestFileName));
        var expectedBytes = File.ReadAllBytes(FixturePath("V1BaselineManifest.json"));

        Assert.True(
            actualBytes.AsSpan().SequenceEqual(expectedBytes),
            $"manifest.json bytes differ from the group-2 baseline: actual sha256 {Sha256Hex(actualBytes)} " +
            $"({actualBytes.Length} bytes), expected sha256 {Sha256Hex(expectedBytes)} ({expectedBytes.Length} bytes).");

        var text = System.Text.Encoding.UTF8.GetString(actualBytes);
        Assert.DoesNotContain("vectorLayer", text, StringComparison.Ordinal);
        Assert.Contains("\"packageVersion\": 1", text, StringComparison.Ordinal);

        var (manifest, diagnostics) = MapPackageManifestJson.ParseAndValidate(text);
        Assert.Empty(diagnostics);
        Assert.Equal(MapPackageFormat.CurrentPackageVersion, manifest!.PackageVersion);
        Assert.Null(manifest.VectorLayer);
    }

    [Fact]
    public void VectorFreeImport_FileTreeMatchesGroup2Baseline()
    {
        using var scratch = new ScratchRoot();
        var packageDir = Path.Combine(scratch.Root, "v1-baseline-tree");
        var writeResult = LegacyMapPackageImporter.Import(BuildBaselineModel(), packageDir);

        Assert.True(writeResult.Success, string.Join("; ", writeResult.Diagnostics));

        var actualTree = FileTreeDigests(packageDir);
        var expectedTree = NormalizeLines(File.ReadAllText(FixturePath("V1BaselineTree.txt")));

        Assert.Equal(expectedTree, actualTree);
    }

    [Fact]
    public void FailControl_VectorPresentManifestBytesDifferFromBaseline()
    {
        var baselineBytes = File.ReadAllBytes(FixturePath("V1BaselineManifest.json"));
        var (manifest, diagnostics) = MapPackageManifestJson.Parse(System.Text.Encoding.UTF8.GetString(baselineBytes));

        Assert.Empty(diagnostics);
        Assert.NotNull(manifest);

        var vectorEntry = new MapPackageVectorLayerEntry(
            MapPackageFormat.VectorLayerPath,
            MapPackageFormat.EncodingJson,
            128,
            Sha256Hex(new byte[] { 0x11, 0x22, 0x33 }),
            1);
        var vectorVariant = manifest! with { PackageVersion = MapPackageFormat.VectorPackageVersion, VectorLayer = vectorEntry };
        var vectorBytes = System.Text.Encoding.UTF8.GetBytes(MapPackageManifestJson.Serialize(vectorVariant));

        // Fail-control: the byte assertion must have teeth. A manifest with a vector layer
        // present must NOT compare equal to the v1 baseline bytes.
        Assert.NotEqual(baselineBytes, vectorBytes);
    }

    [Fact]
    public void ManifestWithVersionTwoAndNoVectorLayer_StillParsesValidates()
    {
        var baselineText = File.ReadAllText(FixturePath("V1BaselineManifest.json"));
        var lenientText = baselineText.Replace(
            $"\"packageVersion\": {MapPackageFormat.CurrentPackageVersion}",
            $"\"packageVersion\": {MapPackageFormat.VectorPackageVersion}");

        Assert.NotEqual(baselineText, lenientText);

        var (manifest, diagnostics) = MapPackageManifestJson.ParseAndValidate(lenientText);

        Assert.Empty(diagnostics);
        Assert.NotNull(manifest);
        Assert.Equal(MapPackageFormat.VectorPackageVersion, manifest!.PackageVersion);
        Assert.Null(manifest.VectorLayer);
    }

    // Byte-identical to BuildBaselineModel in the baseline capture test
    // (test/Panopticon.Simulation.Tests/Content/Importing/MapSources/Package/BaselineCaptureTests.cs
    // at commit 93821be3cc06937a6d47996ada3cb1c6b52e214b), except for the MapInterchange
    // constructor call: vectorLayers is passed explicitly as null here.
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
            vectorLayers: null);
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
