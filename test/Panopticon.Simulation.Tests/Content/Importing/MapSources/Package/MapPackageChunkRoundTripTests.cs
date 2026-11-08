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
/// Round-trip tests for the H3 map package chunk layer (WI-H3-02): packages written by
/// <see cref="MapPackageDirectoryWriter"/> must read back byte-identically through the
/// raw file bytes and the typed payloads of <see cref="MapPackageChunkFileReader"/>,
/// including exact float32 bit patterns for heights.
/// </summary>
public sealed class MapPackageChunkRoundTripTests
{
    [Fact]
    public void HeightsRoundTripPreservesExactBitPatterns()
    {
        var root = NewTempRoot();

        try
        {
            var target = Path.Combine(root, "package");
            var heights = HeightsWithSpecialBitPatterns();
            var heightsEncoded = MapPackageChunkCodec.EncodeHeights(heights);
            var entry = HeightsEntry(heightsEncoded);
            var manifest = BuildManifest([entry], []);
            var write = MapPackageDirectoryWriter.Write(target, manifest, new FixedPayloads(heights, [], [], [], []));

            Assert.True(write.Success, Describe(write.Diagnostics));

            var rawOnDisk = File.ReadAllBytes(Path.Combine(target, "layers", "heights_0_0.bin"));
            Assert.Equal(heightsEncoded, rawOnDisk);

            var read = MapPackageChunkFileReader.ReadHeights(target, entry);
            Assert.True(read.Success, Describe(read.Diagnostics));
            Assert.NotNull(read.Payload);
            Assert.Equal(heights.Length, read.Payload.Length);

            for (var i = 0; i < heights.Length; i++)
            {
                Assert.Equal(
                    BitConverter.SingleToUInt32Bits(heights[i]),
                    BitConverter.SingleToUInt32Bits(read.Payload[i]));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RawLayersRoundTripByteIdentically()
    {
        var root = NewTempRoot();

        try
        {
            var target = Path.Combine(root, "package");
            var heights = PatternedFloats();
            var heightsEncoded = MapPackageChunkCodec.EncodeHeights(heights);
            var materials = PatternedBytes(129 * 129);
            var masks = PatternedBytes(129 * 129);
            var imagery = PatternedBytes(32 * 32 * 4);
            var retained = PatternedBytes(160);

            var materialsEncoded = MapPackageChunkCodec.EncodeMaterials(materials);
            var masksEncoded = MapPackageChunkCodec.EncodeMasks(masks);
            var imageryEncoded = MapPackageChunkCodec.EncodeImagery(imagery);
            var retainedEncoded = MapPackageChunkCodec.EncodeRetained(retained);

            var layers = new[]
            {
                HeightsEntry(heightsEncoded),
                MaterialsEntry(materialsEncoded),
                MasksEntry(masksEncoded),
                ImageryEntry(imageryEncoded),
            };
            var retainedEntries = new[] { RetainedEntry(retainedEncoded) };
            var manifest = BuildManifest(layers, retainedEntries);
            var payloads = new FixedPayloads(heights, materials, masks, imagery, retained);
            var write = MapPackageDirectoryWriter.Write(target, manifest, payloads);

            Assert.True(write.Success, Describe(write.Diagnostics));

            Assert.Equal(heightsEncoded, File.ReadAllBytes(Path.Combine(target, "layers", "heights_0_0.bin")));
            Assert.Equal(materialsEncoded, File.ReadAllBytes(Path.Combine(target, "layers", "materials_0_0.bin")));
            Assert.Equal(masksEncoded, File.ReadAllBytes(Path.Combine(target, "layers", "masks_0_0.bin")));
            Assert.Equal(imageryEncoded, File.ReadAllBytes(Path.Combine(target, "layers", "imagery_0_0.raw")));
            Assert.Equal(retainedEncoded, File.ReadAllBytes(Path.Combine(target, "retained", "AssetList.bin")));

            var readHeights = MapPackageChunkFileReader.ReadHeights(target, layers[0]);
            Assert.True(readHeights.Success, Describe(readHeights.Diagnostics));

            for (var i = 0; i < heights.Length; i++)
            {
                Assert.Equal(
                    BitConverter.SingleToUInt32Bits(heights[i]),
                    BitConverter.SingleToUInt32Bits(readHeights.Payload[i]));
            }

            var readMaterials = MapPackageChunkFileReader.ReadMaterials(target, layers[1]);
            Assert.True(readMaterials.Success, Describe(readMaterials.Diagnostics));
            Assert.Equal(materials, readMaterials.Payload);

            var readMasks = MapPackageChunkFileReader.ReadMasks(target, layers[2]);
            Assert.True(readMasks.Success, Describe(readMasks.Diagnostics));
            Assert.Equal(masks, readMasks.Payload);

            var readImagery = MapPackageChunkFileReader.ReadImagery(target, layers[3]);
            Assert.True(readImagery.Success, Describe(readImagery.Diagnostics));
            Assert.Equal(imagery, readImagery.Payload);

            var readRetained = MapPackageChunkFileReader.ReadRetained(target, retainedEntries[0]);
            Assert.True(readRetained.Success, Describe(readRetained.Diagnostics));
            Assert.Equal(retained, readRetained.Payload);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WriterCreatesMissingParentDirectories()
    {
        var root = NewTempRoot();

        try
        {
            var target = Path.Combine(root, "deep", "nested", "package");
            var heights = PatternedFloats();
            var heightsEncoded = MapPackageChunkCodec.EncodeHeights(heights);
            var entry = HeightsEntry(heightsEncoded);
            var manifest = BuildManifest([entry], []);
            var write = MapPackageDirectoryWriter.Write(target, manifest, new FixedPayloads(heights, [], [], [], []));

            Assert.True(write.Success, Describe(write.Diagnostics));
            Assert.True(Directory.Exists(target));
            Assert.True(MapPackageChunkFileReader.ReadHeights(target, entry).Success);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static float[] HeightsWithSpecialBitPatterns()
    {
        var samples = new float[131 * 131];

        samples[0] = -0.0f;
        samples[1] = BitConverter.UInt32BitsToSingle(0x00000001u);
        samples[2] = BitConverter.UInt32BitsToSingle(0x007FFFFFu);
        samples[3] = 1.5e38f;
        samples[4] = -273.15f;
        samples[5] = float.MaxValue;
        samples[6] = float.MinValue;
        samples[7] = float.Epsilon;
        samples[8] = 0.0f;
        samples[9] = 123.456f;

        var random = new Random(20261108);

        for (var i = 10; i < samples.Length; i++)
        {
            samples[i] = (float)((random.NextDouble() * 2000.0) - 1000.0);
        }

        return samples;
    }

    private static float[] PatternedFloats()
    {
        var samples = new float[131 * 131];

        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = i * 0.5f;
        }

        return samples;
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

    private static MapPackageLayerEntry HeightsEntry(byte[] encoded)
    {
        return new MapPackageLayerEntry(
            0,
            0,
            MapPackageFormat.LayerKindHeights,
            "layers/heights_0_0.bin",
            MapPackageFormat.EncodingFloat32Le,
            131,
            131,
            encoded.Length,
            Sha256Hex(encoded));
    }

    private static MapPackageLayerEntry MaterialsEntry(byte[] encoded)
    {
        return new MapPackageLayerEntry(
            0,
            0,
            MapPackageFormat.LayerKindMaterials,
            "layers/materials_0_0.bin",
            MapPackageFormat.EncodingUint8,
            129,
            129,
            encoded.Length,
            Sha256Hex(encoded));
    }

    private static MapPackageLayerEntry MasksEntry(byte[] encoded)
    {
        return new MapPackageLayerEntry(
            0,
            0,
            MapPackageFormat.LayerKindMasks,
            "layers/masks_0_0.bin",
            MapPackageFormat.EncodingUint8,
            129,
            129,
            encoded.Length,
            Sha256Hex(encoded));
    }

    private static MapPackageLayerEntry ImageryEntry(byte[] encoded)
    {
        return new MapPackageLayerEntry(
            0,
            0,
            MapPackageFormat.LayerKindImagery,
            "layers/imagery_0_0.raw",
            MapPackageFormat.EncodingRgba8,
            32,
            32,
            encoded.Length,
            Sha256Hex(encoded));
    }

    private static MapPackageRetainedEntry RetainedEntry(byte[] encoded)
    {
        return new MapPackageRetainedEntry(
            "AssetList",
            1,
            "retained/AssetList.bin",
            encoded.Length,
            Sha256Hex(encoded));
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
