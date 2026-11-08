#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources.Package;
using Xunit;

namespace Panopticon.Tests.Content.Importing.MapSources.Package;

/// <summary>
/// Atomic-write tests for the H3 map package chunk layer (WI-H3-02): an overwrite
/// replaces the package exactly and leaves no staging or backup residue, and a failure
/// during staging leaves the previous package intact with no residue.
/// </summary>
public sealed class MapPackageChunkAtomicWriteTests
{
    [Fact]
    public void OverwriteReplacesPackageExactlyAndLeavesNoResidue()
    {
        var root = NewTempRoot();

        try
        {
            var target = Path.Combine(root, "package");

            var first = BuildPackage(1.0f);
            var firstWrite = MapPackageDirectoryWriter.Write(target, first.Manifest, first.Payloads);
            Assert.True(firstWrite.Success, Describe(firstWrite.Diagnostics));

            var second = BuildPackage(2.5f);
            var secondWrite = MapPackageDirectoryWriter.Write(target, second.Manifest, second.Payloads);
            Assert.True(secondWrite.Success, Describe(secondWrite.Diagnostics));

            // The final content is exactly the second package.
            var heightsPath = Path.Combine(target, "layers", "heights_0_0.bin");
            Assert.Equal(second.HeightsEncoded, File.ReadAllBytes(heightsPath));

            var manifestJson = File.ReadAllText(Path.Combine(target, "manifest.json"));
            Assert.Contains(second.HeightsDigest, manifestJson);
            Assert.DoesNotContain(first.HeightsDigest, manifestJson);

            // No staging or backup residue remains in the parent directory.
            ResidueAssertion(root, target);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProviderFailureMidStagingLeavesOriginalIntactAndNoResidue()
    {
        var root = NewTempRoot();

        try
        {
            var target = Path.Combine(root, "package");

            var original = BuildPackage(1.0f);
            var originalWrite = MapPackageDirectoryWriter.Write(target, original.Manifest, original.Payloads);
            Assert.True(originalWrite.Success, Describe(originalWrite.Diagnostics));

            var heightsPath = Path.Combine(target, "layers", "heights_0_0.bin");
            var originalBytes = File.ReadAllBytes(heightsPath);
            var originalManifest = File.ReadAllText(Path.Combine(target, "manifest.json"));

            // The replacement provider throws on its second payload request, aborting the
            // staging step mid-write.
            var replacement = BuildPackage(7.5f);
            var failingPayloads = new ThrowingMaterialsPayloads(replacement.Payloads);
            var failedWrite = MapPackageDirectoryWriter.Write(target, replacement.Manifest, failingPayloads);

            Assert.False(failedWrite.Success);
            Assert.Contains(failedWrite.Diagnostics, d => d.Code == MapPackageChunkDiagnostic.PayloadProviderFailed);

            // The previous package survives unchanged.
            Assert.Equal(originalBytes, File.ReadAllBytes(heightsPath));
            Assert.Equal(originalManifest, File.ReadAllText(Path.Combine(target, "manifest.json")));

            // No staging or backup residue remains in the parent directory.
            ResidueAssertion(root, target);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void ResidueAssertion(string root, string target)
    {
        var entries = Directory.GetFileSystemEntries(root);
        var entry = Assert.Single(entries);
        Assert.Equal(Path.GetFullPath(target), Path.GetFullPath(entry));
    }

    private static (MapPackageManifest Manifest, FixedPayloads Payloads, byte[] HeightsEncoded, string HeightsDigest) BuildPackage(float heightScale)
    {
        var heights = new float[131 * 131];

        for (var i = 0; i < heights.Length; i++)
        {
            heights[i] = i * heightScale;
        }

        var heightsEncoded = MapPackageChunkCodec.EncodeHeights(heights);
        var heightsDigest = Sha256Hex(heightsEncoded);
        var materials = PatternedBytes(129 * 129, 17);
        var masks = PatternedBytes(129 * 129, 23);

        var materialsEncoded = MapPackageChunkCodec.EncodeMaterials(materials);
        var masksEncoded = MapPackageChunkCodec.EncodeMasks(masks);

        var layers = new[]
        {
            new MapPackageLayerEntry(0, 0, MapPackageFormat.LayerKindHeights, "layers/heights_0_0.bin", MapPackageFormat.EncodingFloat32Le, 131, 131, heightsEncoded.Length, heightsDigest),
            new MapPackageLayerEntry(0, 0, MapPackageFormat.LayerKindMaterials, "layers/materials_0_0.bin", MapPackageFormat.EncodingUint8, 129, 129, materialsEncoded.Length, Sha256Hex(materialsEncoded)),
            new MapPackageLayerEntry(0, 0, MapPackageFormat.LayerKindMasks, "layers/masks_0_0.bin", MapPackageFormat.EncodingUint8, 129, 129, masksEncoded.Length, Sha256Hex(masksEncoded)),
        };
        var manifest = BuildManifest(layers);
        var payloads = new FixedPayloads(heights, materials, masks);

        return (manifest, payloads, heightsEncoded, heightsDigest);
    }

    private static MapPackageManifest BuildManifest(MapPackageLayerEntry[] layers)
    {
        return new MapPackageManifest(
            MapPackageFormat.PackageFormatName,
            MapPackageFormat.CurrentPackageVersion,
            MapInterchange.CurrentSchemaVersion,
            new MapPackageIdentity("TestMap", "LegacyMap", 8),
            new MapPackageTerrainLayout(
                new MapPackageQuantity(10.0, "GameUnit", "MapLocalXy"),
                129,
                129,
                1,
                1,
                0,
                null,
                "Fail",
                1,
                16641,
                []),
            new MapPackageBlendModel(
            [
                new MapPackageMaterialPaletteEntry(0, "grass", new MapPackageAssetReference("terrain", "grass")),
            ]),
            [],
            [],
            new MapPackageProvenance("1.0", [], []),
            layers,
            []);
    }

    private static byte[] PatternedBytes(int length, int offset)
    {
        var data = new byte[length];

        for (var i = 0; i < length; i++)
        {
            data[i] = (byte)((i + offset) % 251);
        }

        return data;
    }

    private static string Sha256Hex(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }

    private static string Describe(System.Collections.Generic.IReadOnlyList<MapPackageDiagnostic> diagnostics)
    {
        return string.Join("; ", diagnostics.Select(d => d.ToString()));
    }

    private static string NewTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "panopticon-h3-02-" + Guid.NewGuid().ToString("N"));
    }

    private sealed class FixedPayloads : IMapPackagePayloadProvider
    {
        private readonly float[] _heights;

        private readonly byte[] _materials;

        private readonly byte[] _masks;

        public FixedPayloads(float[] heights, byte[] materials, byte[] masks)
        {
            _heights = heights;
            _materials = materials;
            _masks = masks;
        }

        public float[] GetHeights(MapPackageLayerEntry entry)
        {
            return _heights;
        }

        public byte[] GetMaterials(MapPackageLayerEntry entry)
        {
            return _materials;
        }

        public byte[] GetMasks(MapPackageLayerEntry entry)
        {
            return _masks;
        }

        public byte[] GetImagery(MapPackageLayerEntry entry)
        {
            throw new InvalidOperationException("This package carries no imagery.");
        }

        public byte[] GetRetainedSection(MapPackageRetainedEntry entry)
        {
            throw new InvalidOperationException("This package carries no retained sections.");
        }
    }

    private sealed class ThrowingMaterialsPayloads : IMapPackagePayloadProvider
    {
        private readonly IMapPackagePayloadProvider _inner;

        public ThrowingMaterialsPayloads(IMapPackagePayloadProvider inner)
        {
            _inner = inner;
        }

        public float[] GetHeights(MapPackageLayerEntry entry)
        {
            return _inner.GetHeights(entry);
        }

        public byte[] GetMaterials(MapPackageLayerEntry entry)
        {
            throw new InvalidOperationException("provider boom");
        }

        public byte[] GetMasks(MapPackageLayerEntry entry)
        {
            return _inner.GetMasks(entry);
        }

        public byte[] GetImagery(MapPackageLayerEntry entry)
        {
            return _inner.GetImagery(entry);
        }

        public byte[] GetRetainedSection(MapPackageRetainedEntry entry)
        {
            return _inner.GetRetainedSection(entry);
        }
    }
}
