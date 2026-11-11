#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Panopticon.Content.Importing.Maps;
using Panopticon.Content.Importing.MapSources;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.Data.Map;
using Panopticon.Terrain;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Terrain;

public sealed class LargeMapRoundTripTests
{
    private const int LatticeWidth = 350;

    private const int LatticeHeight = 210;

    private const int ChunksX = 3;

    private const int ChunksY = 2;

    private const int Stride = MapChunkGeometry.IntervalsPerChunkAxis;

    private const int ResidentCap = 4;

    private const float ElevationScale = 0.625f;

    private readonly ITestOutputHelper _output;

    public LargeMapRoundTripTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void WrittenPackageReadsBackThroughBoundedStoreAndAgreesWithResidentMaterialization()
    {
        WithTempRoot(root =>
        {
            var model = BuildModel();
            var packageRoot = WritePackage(root, model);

            AssertChunksEqual(model, new PackageReader(packageRoot).Read());

            var materialized = RuntimeBridge.MaterializeHeightMapData(model);
            AssertMaterializedMatchesElevationFunction(materialized);
            Assert.True(RuntimeBridge.TryGetMaskFeed(model, out var feed));

            var source = new PackageChunkSource(packageRoot);
            var store = new RuntimeTerrainChunkStore(source, ResidentCap);
            Assert.Equal(ChunksX, source.ChunksX);
            Assert.Equal(ChunksY, source.ChunksY);
            Assert.Equal(LatticeWidth, source.LatticeWidth);
            Assert.Equal(LatticeHeight, source.LatticeHeight);

            Assert.Throws<TerrainChunkStoreCapacityException>(() => store.LoadRegion(0, 0, ChunksX - 1, ChunksY - 1));
            Assert.Equal(0, store.ResidentCount);

            var pages = new (int MinX, int MinY, int MaxX, int MaxY)[]
            {
                (0, 0, 1, 1),
                (2, 0, 2, 1),
                (0, 0, 2, 0),
                (0, 1, 2, 1),
            };
            var covered = new bool[LatticeWidth, LatticeHeight];

            foreach (var page in pages)
            {
                var region = store.LoadRegion(page.MinX, page.MinY, page.MaxX, page.MaxY);
                var expectedKeys = new List<ChunkKey>();

                for (var cy = page.MinY; cy <= page.MaxY; cy++)
                {
                    for (var cx = page.MinX; cx <= page.MaxX; cx++)
                    {
                        expectedKeys.Add(new ChunkKey(cx, cy));
                    }
                }

                Assert.Equal(expectedKeys, region.Select(chunk => chunk.Key));

                foreach (var chunk in region)
                {
                    VerifyStoredSamples(store, chunk.Key, materialized);
                    VerifyOwnedPoints(store, chunk.Key, materialized, feed, covered);
                }

                Assert.True(store.ResidentCount <= ResidentCap, $"Resident count {store.ResidentCount} exceeds cap {ResidentCap}.");
                _output.WriteLine($"Page ({page.MinX},{page.MinY})-({page.MaxX},{page.MaxY}): resident {store.ResidentCount} of cap {ResidentCap}.");
            }

            for (var y = 0; y < LatticeHeight; y++)
            {
                for (var x = 0; x < LatticeWidth; x++)
                {
                    Assert.True(covered[x, y], $"Lattice point ({x}, {y}) was never compared.");
                }
            }
        });
    }

    [Fact]
    public void OutOfRangeAccessFailsExplicitlyWithoutChangingResidency()
    {
        WithTempRoot(root =>
        {
            var packageRoot = WritePackage(root, BuildModel());
            var store = new RuntimeTerrainChunkStore(new PackageChunkSource(packageRoot), ResidentCap);

            Assert.Throws<ArgumentOutOfRangeException>(() => store.GetHeight(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => store.GetHeight(LatticeWidth, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => store.GetHeight(0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => store.GetHeight(0, LatticeHeight));
            Assert.Throws<TerrainChunkLoadException>(() => store.GetChunk(new ChunkKey(ChunksX, 0)));
            Assert.Throws<TerrainChunkLoadException>(() => store.GetChunk(new ChunkKey(-1, 0)));
            Assert.Equal(0, store.ResidentCount);
        });
    }

    [Fact]
    public void CorruptedLayerDigestFailsLoadWithoutMakingChunkResident()
    {
        WithTempRoot(root =>
        {
            var packageRoot = WritePackage(root, BuildModel());
            var heightsPath = Path.Combine(packageRoot, "layers", "heights_1_0.bin");
            var bytes = File.ReadAllBytes(heightsPath);
            bytes[^1] ^= 0x01;
            File.WriteAllBytes(heightsPath, bytes);

            var store = new RuntimeTerrainChunkStore(new PackageChunkSource(packageRoot), ResidentCap);
            var exception = Assert.Throws<TerrainChunkLoadException>(() => store.GetChunk(new ChunkKey(1, 0)));

            Assert.Contains("heights_1_0.bin", exception.Message);
            Assert.False(store.IsResident(new ChunkKey(1, 0)));
            Assert.Equal(0, store.ResidentCount);
            Assert.Equal(new ChunkKey(0, 0), store.GetChunk(new ChunkKey(0, 0)).Key);
        });
    }

    [Fact]
    public void MissingManifestFailsAtSourceConstruction()
    {
        WithTempRoot(root =>
        {
            var packageRoot = Path.Combine(root, "no-manifest");
            Directory.CreateDirectory(packageRoot);

            Assert.Throws<TerrainChunkLoadException>(() => new PackageChunkSource(packageRoot));
        });
    }

    [Fact]
    public void EvictionKeepsDirtyChunksAndEditsDoNotPersistAcrossReload()
    {
        WithTempRoot(root =>
        {
            var model = BuildModel();
            var packageRoot = WritePackage(root, model);
            var materialized = RuntimeBridge.MaterializeHeightMapData(model);
            var sourceValue = Bits(materialized.Elevations[5, 5] * ElevationScale);
            var keyA = new ChunkKey(0, 0);

            var store = new RuntimeTerrainChunkStore(new PackageChunkSource(packageRoot), 2);
            store.GetChunk(keyA);
            store.SetHeight(5, 5, 9.0f);

            Assert.True(store.IsDirty(keyA));
            Assert.Equal(new[] { keyA }, store.DirtyKeys);
            Assert.Throws<TerrainChunkStoreCapacityException>(() => store.Evict(keyA));
            Assert.True(store.IsResident(keyA));

            // The store has no flush path, so clearing the dirty flag discards the edit.
            store.MarkClean(keyA);
            Assert.True(store.Evict(keyA));
            Assert.False(store.IsResident(keyA));

            Assert.Equal(sourceValue, Bits(store.GetHeight(5, 5)));
            Assert.False(store.IsDirty(keyA));

            var blocking = new RuntimeTerrainChunkStore(new PackageChunkSource(packageRoot), 2);
            blocking.SetHeight(5, 5, 9.0f);
            blocking.SetHeight(200, 50, 7.0f);
            var dirtyKeys = new[] { new ChunkKey(0, 0), new ChunkKey(1, 0) };

            Assert.Equal(dirtyKeys, blocking.DirtyKeys);
            Assert.Throws<TerrainChunkStoreCapacityException>(() => blocking.GetHeight(300, 50));
            Assert.Equal(dirtyKeys, blocking.ResidentKeys);
            Assert.Equal(dirtyKeys, blocking.DirtyKeys);
            Assert.False(blocking.IsResident(new ChunkKey(2, 0)));
            Assert.Equal(Bits(9.0f), Bits(blocking.GetHeight(5, 5)));
        });
    }

    private static void WithTempRoot(Action<string> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "panopticon-b4a-6-" + Guid.NewGuid().ToString("N"));

        try
        {
            body(root);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string WritePackage(string root, MapInterchange model)
    {
        var packageRoot = Path.Combine(root, "package");
        var write = LegacyMapPackageImporter.Write(model, packageRoot);

        Assert.True(write.Success, string.Join("; ", write.Diagnostics));
        Assert.Empty(write.Diagnostics);

        return packageRoot;
    }

    private static MapInterchange BuildModel()
    {
        var chunks = new List<Chunk>(ChunksX * ChunksY);

        for (var cy = 0; cy < ChunksY; cy++)
        {
            for (var cx = 0; cx < ChunksX; cx++)
            {
                var coreWidth = CoreExtent(LatticeWidth, cx);
                var coreHeight = CoreExtent(LatticeHeight, cy);
                var storedWidth = coreWidth + (2 * MapChunkGeometry.ApronSamplesPerSide);
                var storedHeight = coreHeight + (2 * MapChunkGeometry.ApronSamplesPerSide);
                var heights = new float[storedWidth * storedHeight];

                for (var sy = 0; sy < storedHeight; sy++)
                {
                    for (var sx = 0; sx < storedWidth; sx++)
                    {
                        var (x, y) = StoredToLattice(cx, cy, sx, sy);
                        heights[(sy * storedWidth) + sx] = Elevation(x, y) * ElevationScale;
                    }
                }

                var materials = new byte[coreWidth * coreHeight];
                var masks = new byte[coreWidth * coreHeight];

                for (var j = 0; j < coreHeight; j++)
                {
                    for (var i = 0; i < coreWidth; i++)
                    {
                        materials[(j * coreWidth) + i] = MaterialFor(i, j);
                        masks[(j * coreWidth) + i] = MaskFor(i, j, coreWidth);
                    }
                }

                chunks.Add(new Chunk(
                    new ChunkKey(cx, cy),
                    new HeightLayer(heights),
                    new MaterialLayer(materials),
                    new MaskLayer(masks),
                    null));
            }
        }

        var layout = new TerrainLayout(
            new MapQuantity(10, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
            LatticeWidth,
            LatticeHeight,
            ChunksX,
            ChunksY,
            0,
            null!,
            MapNodataPolicy.Fail,
            3,
            LatticeWidth * LatticeHeight,
            [new MapTerrainBorder(0, 0, LatticeWidth - 1, LatticeHeight - 1)],
            chunks);

        var blend = new BlendModel(
        [
            new MaterialPaletteEntry(0, "terrain-0", new AssetReference("terrain", "terrain-0")),
            new MaterialPaletteEntry(1, "terrain-1", new AssetReference("terrain", "terrain-1")),
            new MaterialPaletteEntry(2, "terrain-2", new AssetReference("terrain", "terrain-2")),
            new MaterialPaletteEntry(3, "terrain-3", new AssetReference("terrain", "terrain-3")),
            new MaterialPaletteEntry(4, "terrain-4", new AssetReference("terrain", "terrain-4")),
        ]);

        var retainedBytes = new byte[] { 1, 2, 3, 4 };

        return new MapInterchange(
            MapInterchange.CurrentSchemaVersion,
            new MapIdentity("LargeMapRoundTrip", MapSourceKind.Generated, null),
            layout,
            blend,
            null,
            null,
            new Provenance(null, [], []),
            new ImportReport([], [], 0, 0, []),
            [new RetainedSection("GlobalVersion", 8, retainedBytes, Sha256Hex(retainedBytes))]);
    }

    private static void AssertChunksEqual(MapInterchange expected, MapInterchange actual)
    {
        Assert.Equal(expected.Layout.Chunks.Count, actual.Layout.Chunks.Count);

        for (var index = 0; index < expected.Layout.Chunks.Count; index++)
        {
            var want = expected.Layout.Chunks[index];
            var got = actual.Layout.Chunks[index];

            Assert.Equal(want.Key, got.Key);
            Assert.Equal(want.Heights.Samples.Count, got.Heights.Samples.Count);

            for (var sample = 0; sample < want.Heights.Samples.Count; sample++)
            {
                Assert.Equal(Bits(want.Heights.Samples[sample]), Bits(got.Heights.Samples[sample]));
            }

            Assert.Equal(want.Materials!.Indices, got.Materials!.Indices);
            Assert.Equal(want.Masks!.Flags, got.Masks!.Flags);
        }
    }

    private static void AssertMaterializedMatchesElevationFunction(HeightMapData materialized)
    {
        for (var y = 0; y < LatticeHeight; y++)
        {
            for (var x = 0; x < LatticeWidth; x++)
            {
                Assert.Equal((ushort)Elevation(x, y), materialized.Elevations[x, y]);
            }
        }
    }

    private static void VerifyOwnedPoints(
        RuntimeTerrainChunkStore store,
        ChunkKey key,
        HeightMapData materialized,
        bool[,] feed,
        bool[,] covered)
    {
        var xStart = key.Cx * Stride;
        var yStart = key.Cy * Stride;
        var xEnd = key.Cx == ChunksX - 1 ? LatticeWidth - 1 : xStart + Stride - 1;
        var yEnd = key.Cy == ChunksY - 1 ? LatticeHeight - 1 : yStart + Stride - 1;
        var coreWidth = CoreExtent(LatticeWidth, key.Cx);

        for (var y = yStart; y <= yEnd; y++)
        {
            for (var x = xStart; x <= xEnd; x++)
            {
                var i = x - xStart;
                var j = y - yStart;

                Assert.Equal(Bits(materialized.Elevations[x, y] * ElevationScale), Bits(store.GetHeight(x, y)));
                Assert.True(store.TryGetMaterial(x, y, out var material));
                Assert.Equal(MaterialFor(i, j), material);
                Assert.True(store.TryGetMask(x, y, out var mask));
                Assert.Equal(MaskFor(i, j, coreWidth), mask);
                Assert.Equal(feed[x, y], (mask & (byte)MapMaskFlags.Impassable) != 0);

                covered[x, y] = true;
            }
        }
    }

    private static void VerifyStoredSamples(RuntimeTerrainChunkStore store, ChunkKey key, HeightMapData materialized)
    {
        var storedWidth = CoreExtent(LatticeWidth, key.Cx) + (2 * MapChunkGeometry.ApronSamplesPerSide);
        var storedHeight = CoreExtent(LatticeHeight, key.Cy) + (2 * MapChunkGeometry.ApronSamplesPerSide);

        for (var sy = 0; sy < storedHeight; sy++)
        {
            for (var sx = 0; sx < storedWidth; sx++)
            {
                var (x, y) = StoredToLattice(key.Cx, key.Cy, sx, sy);
                Assert.Equal(Bits(materialized.Elevations[x, y] * ElevationScale), Bits(store.GetStoredHeight(key, sx, sy)));
            }
        }
    }

    private static (int X, int Y) StoredToLattice(int cx, int cy, int sx, int sy) =>
        (
            Math.Clamp((cx * Stride) - MapChunkGeometry.ApronSamplesPerSide + sx, 0, LatticeWidth - 1),
            Math.Clamp((cy * Stride) - MapChunkGeometry.ApronSamplesPerSide + sy, 0, LatticeHeight - 1)
        );

    private static int CoreExtent(int latticeCount, int chunkIndex) =>
        Math.Min(MapChunkGeometry.CorePointsPerAxis, latticeCount - (chunkIndex * Stride));

    private static int Elevation(int x, int y) => (((x * 7) + (y * 13)) % 97) + (x / 64);

    private static byte MaterialFor(int i, int j) => (byte)((i + j) % 5);

    private static byte MaskFor(int i, int j, int coreWidth) => (byte)(((j * coreWidth) + i) % 4);

    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
