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
/// Tamper-rejection tests for the H3 map package chunk layer (WI-H3-02): flipped bytes
/// and truncated files on disk must be rejected as typed diagnostics that return no
/// data, and digest verification must run before decoding.
/// </summary>
public sealed class MapPackageChunkTamperTests
{
    [Fact]
    public void FlippedHeightByteIsRejectedWithoutData()
    {
        var (root, target, heightsEntry, _) = WriteSingleLayerPackage();

        try
        {
            var filePath = Path.Combine(target, "layers", "heights_0_0.bin");
            var bytes = File.ReadAllBytes(filePath);
            bytes[64] ^= 0xFF;
            File.WriteAllBytes(filePath, bytes);

            var read = MapPackageChunkFileReader.ReadHeights(target, heightsEntry);

            Assert.False(read.Success);
            Assert.Null(read.Payload);
            Assert.Contains(read.Diagnostics, d => d.Code == MapPackageChunkDiagnostic.DigestMismatch);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TruncatedMaterialsFileIsRejectedWithoutData()
    {
        var (root, target, _, _) = WriteSingleLayerPackage();

        try
        {
            var entry = new MapPackageLayerEntry(
                0,
                0,
                MapPackageFormat.LayerKindMaterials,
                "layers/materials_0_0.bin",
                MapPackageFormat.EncodingUint8,
                129,
                129,
                129 * 129,
                ValidSha256);
            var filePath = Path.Combine(target, "layers", "materials_0_0.bin");
            var bytes = File.ReadAllBytes(filePath);
            File.WriteAllBytes(filePath, bytes[..^32]);

            var read = MapPackageChunkFileReader.ReadMaterials(target, entry);

            Assert.False(read.Success);
            Assert.Null(read.Payload);
            Assert.Contains(read.Diagnostics, d => d.Code == MapPackageChunkDiagnostic.LengthMismatch);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SameLengthTamperIsRejectedByDigestBeforeDecode()
    {
        var (root, target, heightsEntry, _) = WriteSingleLayerPackage();

        try
        {
            // Same length, different content: only the digest check can catch this, and
            // the reader must reject before decoding anything.
            var filePath = Path.Combine(target, "layers", "heights_0_0.bin");
            var bytes = File.ReadAllBytes(filePath);
            Assert.Equal(heightsEntry.ByteLength, bytes.Length);
            bytes[^1] ^= 0x01;
            File.WriteAllBytes(filePath, bytes);

            var read = MapPackageChunkFileReader.ReadHeights(target, heightsEntry);

            Assert.False(read.Success);
            Assert.Null(read.Payload);

            var diagnostic = Assert.Single(read.Diagnostics);
            Assert.Equal(MapPackageChunkDiagnostic.DigestMismatch, diagnostic.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TamperedRetainedSectionIsRejectedWithoutData()
    {
        var (root, target, _, retainedEntry) = WriteSingleLayerPackage();

        try
        {
            var filePath = Path.Combine(target, "retained", "AssetList.bin");
            var bytes = File.ReadAllBytes(filePath);
            bytes[0] ^= 0x80;
            File.WriteAllBytes(filePath, bytes);

            var read = MapPackageChunkFileReader.ReadRetained(target, retainedEntry);

            Assert.False(read.Success);
            Assert.Null(read.Payload);
            Assert.Contains(read.Diagnostics, d => d.Code == MapPackageChunkDiagnostic.DigestMismatch);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ManifestDigestMismatchIsReportedAsDigestInvalid()
    {
        var (root, target, heightsEntry, _) = WriteSingleLayerPackage();

        try
        {
            var declared = heightsEntry with { Sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdeg" };
            var read = MapPackageChunkFileReader.ReadHeights(target, declared);

            Assert.False(read.Success);
            Assert.Null(read.Payload);
            Assert.Contains(read.Diagnostics, d => d.Code == MapPackageDiagnostic.DigestInvalid);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static readonly string ValidSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    /// <summary>Writes one valid package carrying heights, materials, masks, imagery, and one retained section.</summary>
    private static (string Root, string Target, MapPackageLayerEntry HeightsEntry, MapPackageRetainedEntry RetainedEntry) WriteSingleLayerPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "panopticon-h3-02-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "package");

        var heights = new float[131 * 131];

        for (var i = 0; i < heights.Length; i++)
        {
            heights[i] = i * 0.25f;
        }

        var heightsEncoded = MapPackageChunkCodec.EncodeHeights(heights);
        var materialsEncoded = PatternedBytes(129 * 129);
        var masksEncoded = PatternedBytes(129 * 129);
        var imageryEncoded = PatternedBytes(32 * 32 * 4);
        var retainedEncoded = PatternedBytes(160);

        var layers = new[]
        {
            new MapPackageLayerEntry(0, 0, MapPackageFormat.LayerKindHeights, "layers/heights_0_0.bin", MapPackageFormat.EncodingFloat32Le, 131, 131, heightsEncoded.Length, Sha256Hex(heightsEncoded)),
            new MapPackageLayerEntry(0, 0, MapPackageFormat.LayerKindMaterials, "layers/materials_0_0.bin", MapPackageFormat.EncodingUint8, 129, 129, materialsEncoded.Length, Sha256Hex(materialsEncoded)),
            new MapPackageLayerEntry(0, 0, MapPackageFormat.LayerKindMasks, "layers/masks_0_0.bin", MapPackageFormat.EncodingUint8, 129, 129, masksEncoded.Length, Sha256Hex(masksEncoded)),
            new MapPackageLayerEntry(0, 0, MapPackageFormat.LayerKindImagery, "layers/imagery_0_0.raw", MapPackageFormat.EncodingRgba8, 32, 32, imageryEncoded.Length, Sha256Hex(imageryEncoded)),
        };
        var retained = new[]
        {
            new MapPackageRetainedEntry("AssetList", 1, "retained/AssetList.bin", retainedEncoded.Length, Sha256Hex(retainedEncoded)),
        };
        var manifest = BuildManifest(layers, retained);
        var payloads = new FixedPayloads(heights, materialsEncoded, masksEncoded, imageryEncoded, retainedEncoded);
        var write = MapPackageDirectoryWriter.Write(target, manifest, payloads);

        Assert.True(write.Success, string.Join("; ", write.Diagnostics.Select(d => d.ToString())));

        return (root, target, layers[0], retained[0]);
    }

    private static byte[] PatternedBytes(int length)
    {
        var data = new byte[length];

        for (var i = 0; i < length; i++)
        {
            data[i] = (byte)(i % 251);
        }

        return data;
    }

    private static MapPackageManifest BuildManifest(MapPackageLayerEntry[] layers, MapPackageRetainedEntry[] retained)
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
            retained);
    }

    private static string Sha256Hex(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }

    private sealed class FixedPayloads : IMapPackagePayloadProvider
    {
        private readonly float[] _heights;

        private readonly byte[] _materials;

        private readonly byte[] _masks;

        private readonly byte[] _imagery;

        private readonly byte[] _retained;

        public FixedPayloads(float[] heights, byte[] materials, byte[] masks, byte[] imagery, byte[] retained)
        {
            _heights = heights;
            _materials = materials;
            _masks = masks;
            _imagery = imagery;
            _retained = retained;
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
            return _imagery;
        }

        public byte[] GetRetainedSection(MapPackageRetainedEntry entry)
        {
            return _retained;
        }
    }
}
