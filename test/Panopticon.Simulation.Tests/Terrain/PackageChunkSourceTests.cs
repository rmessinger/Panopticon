using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Terrain;
using Xunit;

namespace Panopticon.Tests.Terrain;

public sealed class PackageChunkSourceTests
{
    [Fact]
    public void WrittenPackageLoadsThroughStoreWithExactHeightBitPatterns()
    {
        var root = NewTempRoot();

        try
        {
            var target = Path.Combine(root, "package");
            var heights = HeightsWithSpecialBitPatterns();
            var materials = PatternedBytes(129 * 129, 3);
            var masks = PatternedBytes(129 * 129, 5);
            WritePackage(target, heights, materials, masks);

            var store = new RuntimeTerrainChunkStore(new PackageChunkSource(target), 1);
            var key = new ChunkKey(0, 0);

            Assert.Equal(Bits(heights[0]), Bits(store.GetStoredHeight(key, 0, 0)));
            Assert.False(store.IsDirty(key));

            for (var j = 0; j < 129; j++)
            {
                for (var i = 0; i < 129; i++)
                {
                    Assert.Equal(Bits(heights[((j + 1) * 131) + i + 1]), Bits(store.GetHeight(i, j)));
                    Assert.True(store.TryGetMaterial(i, j, out var material));
                    Assert.Equal(materials[(j * 129) + i], material);
                    Assert.True(store.TryGetMask(i, j, out var mask));
                    Assert.Equal(masks[(j * 129) + i], mask);
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TamperedHeightsFileFailsLoadWithTerrainChunkLoadException()
    {
        var root = NewTempRoot();

        try
        {
            var target = Path.Combine(root, "package");
            WritePackage(target, HeightsWithSpecialBitPatterns(), PatternedBytes(129 * 129, 3), PatternedBytes(129 * 129, 5));

            var heightsPath = Path.Combine(target, "layers", "heights_0_0.bin");
            var bytes = File.ReadAllBytes(heightsPath);
            bytes[^1] ^= 0x01;
            File.WriteAllBytes(heightsPath, bytes);

            var store = new RuntimeTerrainChunkStore(new PackageChunkSource(target), 1);
            var exception = Assert.Throws<TerrainChunkLoadException>(() => store.GetChunk(new ChunkKey(0, 0)));

            Assert.Contains("layers/heights_0_0.bin", exception.Message);
            Assert.Equal(0, store.ResidentCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MissingHeightsEntryInManifestFailsConstructionWithTerrainChunkLoadException()
    {
        var root = NewTempRoot();

        try
        {
            var target = Path.Combine(root, "package");
            var heights = HeightsWithSpecialBitPatterns();
            var materials = PatternedBytes(129 * 129, 3);
            WritePackage(target, heights, materials, PatternedBytes(129 * 129, 5));

            var manifestPath = Path.Combine(target, MapPackageFormat.ManifestFileName);
            var (manifest, diagnostics) = MapPackageManifestJson.ParseAndValidate(File.ReadAllText(manifestPath));
            Assert.Empty(diagnostics);
            var withoutHeights = manifest! with
            {
                Layers = manifest.Layers.Where(entry => entry.LayerKind != MapPackageFormat.LayerKindHeights).ToArray(),
            };
            File.WriteAllText(manifestPath, withoutHeights.ToJson());

            var exception = Assert.Throws<TerrainChunkLoadException>(() => new PackageChunkSource(target));

            Assert.Contains("is invalid", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WritePackage(string target, float[] heights, byte[] materials, byte[] masks)
    {
        var heightsEncoded = MapPackageChunkCodec.EncodeHeights(heights);
        var materialsEncoded = MapPackageChunkCodec.EncodeMaterials(materials);
        var masksEncoded = MapPackageChunkCodec.EncodeMasks(masks);
        var layers = new[]
        {
            HeightsEntry(heightsEncoded),
            MaterialsEntry(materialsEncoded),
            MasksEntry(masksEncoded),
        };
        var manifest = BuildManifest(layers);
        var write = MapPackageDirectoryWriter.Write(target, manifest, new FixedPayloads(heights, materials, masks));

        if (!write.Success)
        {
            throw new InvalidOperationException(string.Join("; ", write.Diagnostics.Select(d => d.ToString())));
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

        var random = new Random(20261111);

        for (var i = 10; i < samples.Length; i++)
        {
            samples[i] = (float)((random.NextDouble() * 2000.0) - 1000.0);
        }

        return samples;
    }

    private static byte[] PatternedBytes(int length, int seed)
    {
        var data = new byte[length];

        for (var i = 0; i < length; i++)
        {
            data[i] = (byte)((i + seed) % 251);
        }

        return data;
    }

    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

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

    private static string Sha256Hex(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }

    private static string NewTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "panopticon-b4a-5-" + Guid.NewGuid().ToString("N"));
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

        public float[] GetHeights(MapPackageLayerEntry entry) => _heights;

        public byte[] GetMaterials(MapPackageLayerEntry entry) => _materials;

        public byte[] GetMasks(MapPackageLayerEntry entry) => _masks;

        public byte[] GetImagery(MapPackageLayerEntry entry) => throw new NotSupportedException();

        public byte[] GetRetainedSection(MapPackageRetainedEntry entry) => throw new NotSupportedException();
    }
}
