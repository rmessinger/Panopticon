using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Content.Importing.Maps;
using Panopticon.Terrain;
using Xunit;

namespace Panopticon.Tests.Terrain;

public sealed class RuntimeTerrainChunkStoreTests
{
    private const int Stride = MapChunkGeometry.IntervalsPerChunkAxis;

    private const int LatticeCountX = (2 * Stride) + 37;

    private const int LatticeCountY = Stride + 53;

    private static readonly ChunkKey KeyA = new(0, 0);

    private static readonly ChunkKey KeyB = new(1, 0);

    private static readonly ChunkKey KeyC = new(2, 0);

    [Fact]
    public void GetChunkLoadsOnDemandAndCachesResidentChunk()
    {
        var source = new CountingChunkSource(BuildLayout(withBlend: false));
        var store = new RuntimeTerrainChunkStore(source, 6);

        Assert.Equal(0, store.ResidentCount);
        Assert.Empty(source.LoadCounts);

        var first = store.GetChunk(KeyB);
        var second = store.GetChunk(KeyB);

        Assert.Same(first, second);
        Assert.Equal(1, source.LoadCounts[KeyB]);
        Assert.Equal(1, store.ResidentCount);
        Assert.True(store.IsResident(KeyB));
    }

    [Fact]
    public void CapEvictionRemovesLeastRecentlyUsedChunk()
    {
        var store = new RuntimeTerrainChunkStore(new CountingChunkSource(BuildLayout(withBlend: false)), 2);

        store.GetChunk(KeyA);
        store.GetChunk(KeyB);
        store.GetChunk(KeyA);
        store.GetChunk(KeyC);

        Assert.Equal(new[] { KeyA, KeyC }, store.ResidentKeys);
        Assert.False(store.IsResident(KeyB));
    }

    [Fact]
    public void DirtyChunkIsNeverEvictedAndLoadFailsAtCapacity()
    {
        var store = new RuntimeTerrainChunkStore(new CountingChunkSource(BuildLayout(withBlend: false)), 1);

        store.GetChunk(KeyA);
        store.SetHeight(5, 5, 9.0f);
        var before = store.ExportChunk(KeyA).Heights.Samples.ToArray();

        Assert.Throws<TerrainChunkStoreCapacityException>(() => store.GetChunk(KeyB));

        Assert.Equal(new[] { KeyA }, store.ResidentKeys);
        Assert.Equal(new[] { KeyA }, store.DirtyKeys);
        Assert.False(store.IsResident(KeyB));
        Assert.Equal(before, store.ExportChunk(KeyA).Heights.Samples.ToArray());
        Assert.Equal(9.0f, store.GetHeight(5, 5));
    }

    [Fact]
    public void EvictionSkipsDirtyChunkAndRemovesCleanLeastRecentlyUsed()
    {
        var store = new RuntimeTerrainChunkStore(new CountingChunkSource(BuildLayout(withBlend: false)), 2);

        store.GetChunk(KeyA);
        store.SetHeight(5, 5, 9.0f);
        store.GetChunk(KeyB);
        store.GetChunk(KeyC);

        Assert.Equal(new[] { KeyA, KeyC }, store.ResidentKeys);
        Assert.Equal(new[] { KeyA }, store.DirtyKeys);
        Assert.False(store.IsResident(KeyB));
    }

    [Fact]
    public void LoadRegionFailureLeavesResidencyUnchanged()
    {
        var source = new CountingChunkSource(BuildLayout(withBlend: false));
        source.FailingKeys.Add(KeyC);
        var store = new RuntimeTerrainChunkStore(source, 6);
        store.GetChunk(KeyA);

        Assert.Throws<TerrainChunkLoadException>(() => store.LoadRegion(0, 0, 2, 0));

        Assert.Equal(new[] { KeyA }, store.ResidentKeys);
        Assert.Equal(1, store.ResidentCount);
        Assert.False(store.IsResident(KeyB));
    }

    [Fact]
    public void LoadRegionReturnsInclusiveRectangleInRowMajorOrder()
    {
        var source = new CountingChunkSource(BuildLayout(withBlend: false));
        var store = new RuntimeTerrainChunkStore(source, 6);
        var preloaded = store.GetChunk(KeyB);

        var region = store.LoadRegion(0, 0, 2, 1);

        var expectedKeys = new ChunkKey[]
        {
            new(0, 0), new(1, 0), new(2, 0), new(0, 1), new(1, 1), new(2, 1),
        };
        Assert.Equal(expectedKeys, region.Select(chunk => chunk.Key));
        Assert.Same(preloaded, region[1]);
        Assert.Equal(1, source.LoadCounts[KeyB]);
        Assert.Equal(6, store.ResidentCount);
    }

    [Fact]
    public void SampleIndexingResolvesInteriorSharedEdgeCornerAndPartialEdge()
    {
        var store = new RuntimeTerrainChunkStore(new CountingChunkSource(BuildLayout(withBlend: false)), 6);
        store.LoadRegion(0, 0, 2, 1);

        Assert.Equal(HeightAt(200, 50), store.GetHeight(200, 50));

        Assert.Equal(HeightAt(128, 50), store.GetHeight(128, 50));
        Assert.Equal(HeightAt(128, 50), store.GetStoredHeight(new ChunkKey(0, 0), 129, 51));
        Assert.Equal(HeightAt(128, 50), store.GetStoredHeight(new ChunkKey(1, 0), 1, 51));

        Assert.Equal(HeightAt(128, 128), store.GetHeight(128, 128));
        Assert.Equal(HeightAt(128, 128), store.GetStoredHeight(new ChunkKey(0, 0), 129, 129));
        Assert.Equal(HeightAt(128, 128), store.GetStoredHeight(new ChunkKey(1, 0), 1, 129));
        Assert.Equal(HeightAt(128, 128), store.GetStoredHeight(new ChunkKey(0, 1), 129, 1));
        Assert.Equal(HeightAt(128, 128), store.GetStoredHeight(new ChunkKey(1, 1), 1, 1));

        Assert.Equal(HeightAt(LatticeCountX - 1, LatticeCountY - 1), store.GetHeight(LatticeCountX - 1, LatticeCountY - 1));
        Assert.Equal(37, store.GetChunk(new ChunkKey(2, 1)).CoreWidth);
        Assert.Equal(53, store.GetChunk(new ChunkKey(2, 1)).CoreHeight);
        Assert.Equal(
            HeightAt(LatticeCountX - 1, LatticeCountY - 1),
            store.GetStoredHeight(new ChunkKey(2, 1), 37, 53));

        Assert.Equal(HeightAt(129, 50), store.GetStoredHeight(new ChunkKey(0, 0), 130, 51));
        Assert.Equal(HeightAt(127, 50), store.GetStoredHeight(new ChunkKey(1, 0), 0, 51));
    }

    [Fact]
    public void MaterialAndMaskLookupReturnCorePatterns()
    {
        var store = new RuntimeTerrainChunkStore(new CountingChunkSource(BuildLayout(withBlend: true)), 6);

        foreach (var (x, y) in new[] { (0, 0), (200, 50), (128, 50), (128, 128), (LatticeCountX - 1, LatticeCountY - 1) })
        {
            Assert.True(store.TryGetMaterial(x, y, out var material));
            Assert.Equal(MaterialAt(x, y), material);
            Assert.True(store.TryGetMask(x, y, out var mask));
            Assert.Equal(MaskAt(x, y), mask);
        }
    }

    [Fact]
    public void MaterialAndMaskLookupReturnFalseOnHeightsOnlyLayout()
    {
        var store = new RuntimeTerrainChunkStore(new CountingChunkSource(BuildLayout(withBlend: false)), 6);

        Assert.False(store.TryGetMaterial(200, 50, out var material));
        Assert.Equal(0, material);
        Assert.False(store.TryGetMask(200, 50, out var mask));
        Assert.Equal(0, mask);
    }

    [Fact]
    public void SetHeightUpdatesOwnerAndResidentSeamCopiesAndExportReflectsEdit()
    {
        var store = new RuntimeTerrainChunkStore(new CountingChunkSource(BuildLayout(withBlend: false)), 6);
        store.LoadRegion(0, 0, 2, 1);
        Assert.Empty(store.DirtyKeys);

        store.SetHeight(128, 50, 777.5f);

        Assert.Equal(777.5f, store.GetHeight(128, 50));
        Assert.Equal(777.5f, store.GetStoredHeight(KeyA, 129, 51));
        Assert.Equal(777.5f, store.GetStoredHeight(KeyB, 1, 51));
        Assert.Equal(HeightAt(129, 50), store.GetHeight(129, 50));

        var exported = store.ExportChunk(KeyB);
        Assert.Equal(777.5f, exported.Heights.Samples[(51 * MapChunkGeometry.StoredPointsPerAxis) + 1]);

        Assert.True(store.IsDirty(KeyB));
        Assert.Equal(new[] { KeyA, KeyB }, store.DirtyKeys);

        store.MarkClean(KeyB);

        Assert.False(store.IsDirty(KeyB));
        Assert.Equal(new[] { KeyA }, store.DirtyKeys);
    }

    [Fact]
    public void LoadingNeighbourAdoptsEditedApronAndMarksItDirty()
    {
        var store = new RuntimeTerrainChunkStore(new CountingChunkSource(BuildLayout(withBlend: false)), 2);

        store.GetChunk(KeyA);
        store.SetHeight(127, 50, 4242.0f);
        Assert.False(store.IsResident(KeyB));

        var loaded = store.GetChunk(KeyB);

        Assert.Equal(4242.0f, store.GetStoredHeight(KeyB, 0, 51));
        Assert.Equal(4242.0f, store.GetHeight(127, 50));
        Assert.True(loaded.IsDirty);
        Assert.True(store.IsDirty(KeyB));
        Assert.Equal(new[] { KeyA, KeyB }, store.DirtyKeys);
    }

    [Fact]
    public void LoadingNeighbourAdoptsEditedSharedCoreEdgeAndMarksItDirty()
    {
        var store = new RuntimeTerrainChunkStore(new CountingChunkSource(BuildLayout(withBlend: false)), 2);

        store.GetChunk(KeyB);
        store.SetHeight(128, 50, 777.5f);
        Assert.False(store.IsResident(KeyA));

        store.GetChunk(KeyA);

        Assert.Equal(777.5f, store.GetStoredHeight(KeyA, 129, 51));
        Assert.Equal(777.5f, store.GetHeight(128, 50));
        Assert.True(store.IsDirty(KeyA));
    }

    private static float HeightAt(int latticeX, int latticeY) => latticeX + (latticeY * 10000f);

    private static byte MaterialAt(int latticeX, int latticeY) => (byte)(((latticeX * 7) + (latticeY * 13)) % 251);

    private static byte MaskAt(int latticeX, int latticeY) => (byte)((latticeX + latticeY) & 3);

    private static int CoreExtent(int latticeCount, int chunkIndex) =>
        Math.Min(MapChunkGeometry.CorePointsPerAxis, latticeCount - (chunkIndex * Stride));

    private static TerrainLayout BuildLayout(bool withBlend)
    {
        var chunks = new List<Chunk>();

        for (var cy = 0; cy < 2; cy++)
        {
            for (var cx = 0; cx < 3; cx++)
            {
                var coreWidth = CoreExtent(LatticeCountX, cx);
                var coreHeight = CoreExtent(LatticeCountY, cy);
                var storedWidth = coreWidth + (2 * MapChunkGeometry.ApronSamplesPerSide);
                var storedHeight = coreHeight + (2 * MapChunkGeometry.ApronSamplesPerSide);
                var heights = new float[storedWidth * storedHeight];

                for (var sy = 0; sy < storedHeight; sy++)
                {
                    for (var sx = 0; sx < storedWidth; sx++)
                    {
                        var latticeX = Math.Clamp((cx * Stride) - 1 + sx, 0, LatticeCountX - 1);
                        var latticeY = Math.Clamp((cy * Stride) - 1 + sy, 0, LatticeCountY - 1);
                        heights[(sy * storedWidth) + sx] = HeightAt(latticeX, latticeY);
                    }
                }

                byte[]? materials = null;
                byte[]? masks = null;

                if (withBlend)
                {
                    materials = new byte[coreWidth * coreHeight];
                    masks = new byte[coreWidth * coreHeight];

                    for (var j = 0; j < coreHeight; j++)
                    {
                        for (var i = 0; i < coreWidth; i++)
                        {
                            var latticeX = (cx * Stride) + i;
                            var latticeY = (cy * Stride) + j;
                            materials[(j * coreWidth) + i] = MaterialAt(latticeX, latticeY);
                            masks[(j * coreWidth) + i] = MaskAt(latticeX, latticeY);
                        }
                    }
                }

                chunks.Add(new Chunk(
                    new ChunkKey(cx, cy),
                    new HeightLayer(heights),
                    materials is null ? null : new MaterialLayer(materials),
                    masks is null ? null : new MaskLayer(masks),
                    null));
            }
        }

        return new TerrainLayout(
            new MapQuantity(10, MapQuantityUnit.GameUnit, MapQuantityFrame.MapLocalXy),
            LatticeCountX,
            LatticeCountY,
            3,
            2,
            0,
            new GeospatialTransform(null, null, null, 1.0, null, null, null),
            MapNodataPolicy.Fail,
            3,
            (uint)(LatticeCountX * LatticeCountY),
            [new MapTerrainBorder(0, 0, (uint)(LatticeCountX - 1), (uint)(LatticeCountY - 1))],
            chunks);
    }

    private sealed class CountingChunkSource : ITerrainChunkSource
    {
        private readonly InterchangeChunkSource _inner;

        public CountingChunkSource(TerrainLayout layout)
        {
            _inner = new InterchangeChunkSource(layout);
        }

        public Dictionary<ChunkKey, int> LoadCounts { get; } = new();

        public HashSet<ChunkKey> FailingKeys { get; } = new();

        public int ChunksX => _inner.ChunksX;

        public int ChunksY => _inner.ChunksY;

        public int LatticeWidth => _inner.LatticeWidth;

        public int LatticeHeight => _inner.LatticeHeight;

        public TerrainChunkData LoadChunk(ChunkKey key)
        {
            LoadCounts[key] = LoadCounts.GetValueOrDefault(key) + 1;

            if (FailingKeys.Contains(key))
            {
                throw new TerrainChunkLoadException($"Chunk ({key.Cx}, {key.Cy}) failed by test double.");
            }

            return _inner.LoadChunk(key);
        }
    }
}
