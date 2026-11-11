#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Data.Map;
using Panopticon.Mathematics;
using Panopticon.Terrain;
using Panopticon.Terrain.Cdlod;
using Xunit;

namespace Panopticon.Simulation.Tests.Terrain;

public sealed class TerrainStreamingTests
{
    private sealed class FakePatch
    {
        public CdlodSelectedNode Selected { get; }

        public int Band { get; }

        public FakePatch(CdlodSelectedNode selected, int band)
        {
            Selected = selected;
            Band = band;
        }
    }

    private static HeightMap CreateSyntheticHeightMap(int width, int height, Func<int, int, ushort>? elevationFunc = null)
    {
        var elevations = new ushort[width + 1, height + 1];
        for (var y = 0; y <= height; y++)
        {
            for (var x = 0; x <= width; x++)
            {
                elevations[x, y] = elevationFunc != null
                    ? elevationFunc(x, y)
                    : (ushort)((x * 17 + y * 31 + (x ^ y) * 7) % 2000 + 100);
            }
        }

        var data = HeightMapData.Create(0, elevations);
        return new HeightMap(data);
    }

    [Fact]
    public void SharedEdgesAreCrackFreeForAllFourEdges()
    {
        var map = CreateSyntheticHeightMap(257, 257);
        const float coarseMorph = 0.75f;
        const float fineMorph = 0.25f;

        // --- North shared edge ---
        // Fine node at (0, 1, 0) spans X:[0..16], Y:[16..32]. North edge is Y=32.
        // Coarse node at (0, 1, 1) spans X:[0..32], Y:[32..64]. South edge is Y=32.
        var fineNorth = new CdlodNode(0, 1, 0);
        var coarseNorth = new CdlodNode(0, 1, 1);

        var fineNorthGeom = TerrainPatchGeometry.Build(
            map,
            fineNorth,
            fineMorph,
            CdlodEdges.North,
            edge => edge == CdlodEdges.North ? coarseMorph : null);

        var coarseNorthGeom = TerrainPatchGeometry.Build(
            map,
            coarseNorth,
            coarseMorph,
            CdlodEdges.None);

        for (var j = 0; j <= 8; j++)
        {
            var pFineEven = fineNorthGeom.Positions[16 * 17 + 2 * j];
            var pCoarse = coarseNorthGeom.Positions[0 * 17 + j];
            Assert.Equal(pCoarse, pFineEven);

            if (j < 8)
            {
                var pFineOdd = fineNorthGeom.Positions[16 * 17 + 2 * j + 1];
                var pCoarseNext = coarseNorthGeom.Positions[0 * 17 + j + 1];
                var expectedMid = Vector3.Lerp(pCoarse, pCoarseNext, 0.5f);
                Assert.Equal(expectedMid, pFineOdd);
            }
        }

        // --- South shared edge ---
        // Fine node at (0, 2, 0) spans X:[0..16], Y:[32..48]. South edge is Y=32.
        // Coarse node at (0, 0, 1) spans X:[0..32], Y:[0..32]. North edge is Y=32.
        var fineSouth = new CdlodNode(0, 2, 0);
        var coarseSouth = new CdlodNode(0, 0, 1);

        var fineSouthGeom = TerrainPatchGeometry.Build(
            map,
            fineSouth,
            fineMorph,
            CdlodEdges.South,
            edge => edge == CdlodEdges.South ? coarseMorph : null);

        var coarseSouthGeom = TerrainPatchGeometry.Build(
            map,
            coarseSouth,
            coarseMorph,
            CdlodEdges.None);

        for (var j = 0; j <= 8; j++)
        {
            var pFineEven = fineSouthGeom.Positions[0 * 17 + 2 * j];
            var pCoarse = coarseSouthGeom.Positions[16 * 17 + j];
            Assert.Equal(pCoarse, pFineEven);

            if (j < 8)
            {
                var pFineOdd = fineSouthGeom.Positions[0 * 17 + 2 * j + 1];
                var pCoarseNext = coarseSouthGeom.Positions[16 * 17 + j + 1];
                var expectedMid = Vector3.Lerp(pCoarse, pCoarseNext, 0.5f);
                Assert.Equal(expectedMid, pFineOdd);
            }
        }

        // --- East shared edge ---
        // Fine node at (1, 0, 0) spans X:[16..32], Y:[0..16]. East edge is X=32.
        // Coarse node at (1, 0, 1) spans X:[32..64], Y:[0..32]. West edge is X=32.
        var fineEast = new CdlodNode(1, 0, 0);
        var coarseEast = new CdlodNode(1, 0, 1);

        var fineEastGeom = TerrainPatchGeometry.Build(
            map,
            fineEast,
            fineMorph,
            CdlodEdges.East,
            edge => edge == CdlodEdges.East ? coarseMorph : null);

        var coarseEastGeom = TerrainPatchGeometry.Build(
            map,
            coarseEast,
            coarseMorph,
            CdlodEdges.None);

        for (var j = 0; j <= 8; j++)
        {
            var pFineEven = fineEastGeom.Positions[(2 * j) * 17 + 16];
            var pCoarse = coarseEastGeom.Positions[j * 17 + 0];
            Assert.Equal(pCoarse, pFineEven);

            if (j < 8)
            {
                var pFineOdd = fineEastGeom.Positions[(2 * j + 1) * 17 + 16];
                var pCoarseNext = coarseEastGeom.Positions[(j + 1) * 17 + 0];
                var expectedMid = Vector3.Lerp(pCoarse, pCoarseNext, 0.5f);
                Assert.Equal(expectedMid, pFineOdd);
            }
        }

        // --- West shared edge ---
        // Fine node at (2, 0, 0) spans X:[32..48], Y:[0..16]. West edge is X=32.
        // Coarse node at (0, 0, 1) spans X:[0..32], Y:[0..32]. East edge is X=32.
        var fineWest = new CdlodNode(2, 0, 0);
        var coarseWest = new CdlodNode(0, 0, 1);

        var fineWestGeom = TerrainPatchGeometry.Build(
            map,
            fineWest,
            fineMorph,
            CdlodEdges.West,
            edge => edge == CdlodEdges.West ? coarseMorph : null);

        var coarseWestGeom = TerrainPatchGeometry.Build(
            map,
            coarseWest,
            coarseMorph,
            CdlodEdges.None);

        for (var j = 0; j <= 8; j++)
        {
            var pFineEven = fineWestGeom.Positions[(2 * j) * 17 + 0];
            var pCoarse = coarseWestGeom.Positions[j * 17 + 16];
            Assert.Equal(pCoarse, pFineEven);

            if (j < 8)
            {
                var pFineOdd = fineWestGeom.Positions[(2 * j + 1) * 17 + 0];
                var pCoarseNext = coarseWestGeom.Positions[(j + 1) * 17 + 16];
                var expectedMid = Vector3.Lerp(pCoarse, pCoarseNext, 0.5f);
                Assert.Equal(expectedMid, pFineOdd);
            }
        }
    }

    [Fact]
    public void SelectionRestrictedToAtMostOneLodDifference()
    {
        var map = CreateSyntheticHeightMap(257, 257);
        var provider = new CachedHeightRangeProvider(map);
        var quadtree = new CdlodQuadtree(257, 257, provider, 256, 1.0f);

        var view = Matrix4x4.CreateLookAt(new Vector3(1280f, 1280f, 400f), new Vector3(1280f, 1280f, 0f), Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, 1f, 1f, 5000f);
        var camera = new CdlodCamera(new Vector3(1280f, 1280f, 400f), new BoundingFrustum(view * proj), 1f);

        var selection = quadtree.Select(camera);
        Assert.NotEmpty(selection.Nodes);

        foreach (var selected in selection.Nodes)
        {
            var extentA = selected.Node.ClippedExtent(257, 257);
            foreach (var other in selection.Nodes)
            {
                if (selected.Node == other.Node)
                {
                    continue;
                }

                var extentB = other.Node.ClippedExtent(257, 257);
                var touches = extentA.Left <= extentB.Right && extentB.Left <= extentA.Right &&
                              extentA.Top <= extentB.Bottom && extentB.Top <= extentA.Bottom;

                if (touches)
                {
                    Assert.True(
                        Math.Abs(selected.Node.Level - other.Node.Level) <= 1,
                        $"Adjacent nodes {selected.Node} and {other.Node} differ by more than 1 LOD level.");
                }
            }
        }
    }

    [Fact]
    public void MorphZeroEqualsRawAndMorphOneDisplacesOddInteriorVerticesFully()
    {
        var map = CreateSyntheticHeightMap(257, 257);
        var node = new CdlodNode(1, 1, 1);

        var geomZero = TerrainPatchGeometry.Build(map, node, 0f, CdlodEdges.None);
        var geomOne = TerrainPatchGeometry.Build(map, node, 1f, CdlodEdges.None);

        for (var k = 0; k < 17; k++)
        {
            for (var i = 0; i < 17; i++)
            {
                var idx = k * 17 + i;
                var raw = TerrainPatchGeometry.GetRawPosition(map, node, i, k);
                Assert.Equal(raw, geomZero.Positions[idx]);

                var isInterior = i > 0 && i < 16 && k > 0 && k < 16;
                if (!isInterior)
                {
                    continue;
                }

                var isOddX = i % 2 != 0;
                var isOddY = k % 2 != 0;

                if (!isOddX && !isOddY)
                {
                    Assert.Equal(raw, geomOne.Positions[idx]);
                }
                else if (isOddX && !isOddY)
                {
                    var p0 = TerrainPatchGeometry.GetRawPosition(map, node, i - 1, k);
                    var p1 = TerrainPatchGeometry.GetRawPosition(map, node, i + 1, k);
                    var expected = Vector3.Lerp(p0, p1, 0.5f);
                    Assert.Equal(expected, geomOne.Positions[idx]);
                }
                else if (!isOddX && isOddY)
                {
                    var p0 = TerrainPatchGeometry.GetRawPosition(map, node, i, k - 1);
                    var p1 = TerrainPatchGeometry.GetRawPosition(map, node, i, k + 1);
                    var expected = Vector3.Lerp(p0, p1, 0.5f);
                    Assert.Equal(expected, geomOne.Positions[idx]);
                }
                else
                {
                    var c00 = TerrainPatchGeometry.GetRawPosition(map, node, i - 1, k - 1);
                    var c10 = TerrainPatchGeometry.GetRawPosition(map, node, i + 1, k - 1);
                    var c01 = TerrainPatchGeometry.GetRawPosition(map, node, i - 1, k + 1);
                    var c11 = TerrainPatchGeometry.GetRawPosition(map, node, i + 1, k + 1);
                    var expected = (c00 + c10 + c01 + c11) * 0.25f;
                    Assert.Equal(expected, geomOne.Positions[idx]);
                }
            }
        }
    }

    [Fact]
    public void NormalsAreIdenticalAcrossLevelsAtSameLatticeSample()
    {
        var map = CreateSyntheticHeightMap(257, 257);
        var fineNode = new CdlodNode(0, 0, 0);
        var coarseNode = new CdlodNode(0, 0, 1);

        var fineGeom = TerrainPatchGeometry.Build(map, fineNode, 0f, CdlodEdges.None);
        var coarseGeom = TerrainPatchGeometry.Build(map, coarseNode, 0f, CdlodEdges.None);

        // Even indices of fine node (2*j, 2*k) correspond to coarse node index (j, k)
        for (var k = 0; k <= 8; k++)
        {
            for (var i = 0; i <= 8; i++)
            {
                var fineIdx = (2 * k) * 17 + (2 * i);
                var coarseIdx = k * 17 + i;

                var fineCoords = TerrainPatchGeometry.GetSampleCoords(map, fineNode, 2 * i, 2 * k);
                var coarseCoords = TerrainPatchGeometry.GetSampleCoords(map, coarseNode, i, k);

                Assert.Equal(fineCoords, coarseCoords);
                Assert.Equal(fineGeom.Normals[fineIdx], coarseGeom.Normals[coarseIdx]);
                Assert.Equal(map.Normals[fineCoords.X, fineCoords.Y], fineGeom.Normals[fineIdx]);
            }
        }
    }

    [Fact]
    public void PartialClippedEdgeNodeRepeatsLastSampleGivingZeroAreaTriangles()
    {
        // 25x25 heightmap: valid lattice indices are 0..24
        var map = CreateSyntheticHeightMap(25, 25);
        var node = new CdlodNode(1, 1, 0); // Origin at (16, 16), interval 16 reaches 32

        var geom = TerrainPatchGeometry.Build(map, node, 0f, CdlodEdges.None);
        Assert.Equal(289, geom.Positions.Length);
        Assert.Equal(1536, geom.Indices.Length);
        Assert.Equal(512, geom.Triangles.Length);

        // Samples past index 8 (16 + 8 = 24) must repeat coordinate 24
        var expectedLastPos = map.GetPosition(24, 24);
        for (var k = 8; k < 17; k++)
        {
            for (var i = 8; i < 17; i++)
            {
                var idx = k * 17 + i;
                Assert.Equal(expectedLastPos, geom.Positions[idx]);
            }
        }

        // Check that quads completely within the clipped region produce zero-area triangles
        var zeroAreaFound = false;
        foreach (var tri in geom.Triangles)
        {
            var area = Vector3.Cross(tri.V1 - tri.V0, tri.V2 - tri.V0).Length() * 0.5f;
            if (area == 0f)
            {
                zeroAreaFound = true;
                break;
            }
        }

        Assert.True(zeroAreaFound, "Expected zero-area triangles in clipped edge region.");
    }

    [Fact]
    public void StreamerManagesEnterStayExitAndRebuildsOnNeighbourBandChange()
    {
        var created = new List<FakePatch>();
        var released = new List<FakePatch>();

        var streamer = new TerrainPatchStreamer<FakePatch>(
            (selected, band) =>
            {
                var patch = new FakePatch(selected, band);
                created.Add(patch);
                return patch;
            },
            patch =>
            {
                released.Add(patch);
            });

        var nodeA = new CdlodNode(0, 0, 0);
        var nodeB = new CdlodNode(0, 1, 0);
        var coarseNorth = new CdlodNode(0, 1, 1);

        // Selection 1: Node A and Node B enter
        var sel1 = new CdlodSelection(new[]
        {
            new CdlodSelectedNode(nodeA, 0.1f, CdlodEdges.None),
            new CdlodSelectedNode(nodeB, 0.2f, CdlodEdges.North),
            new CdlodSelectedNode(coarseNorth, 0.5f, CdlodEdges.None)
        });

        streamer.Update(sel1);
        Assert.Equal(3, streamer.ResidentCount);
        Assert.Equal(3, streamer.CreatedTotal);
        Assert.Equal(0, streamer.ReleasedTotal);
        Assert.Equal(3 * 9248L, streamer.ResidentVertexBytes);

        // Selection 2: Node A exits, Node B stays with same key and neighbour band
        var sel2 = new CdlodSelection(new[]
        {
            new CdlodSelectedNode(nodeB, 0.2f, CdlodEdges.North),
            new CdlodSelectedNode(coarseNorth, 0.5f, CdlodEdges.None)
        });

        streamer.Update(sel2);
        Assert.Equal(2, streamer.ResidentCount);
        Assert.Equal(3, streamer.CreatedTotal);
        Assert.Equal(1, streamer.ReleasedTotal);
        Assert.Equal(nodeA, released[0].Selected.Node);

        // Selection 3: Coarse neighbour morph band changes (from 0.5f [band 4] to 0.875f [band 7])
        // Node B must be rebuilt even though Node B's own morph did not change
        var sel3 = new CdlodSelection(new[]
        {
            new CdlodSelectedNode(nodeB, 0.2f, CdlodEdges.North),
            new CdlodSelectedNode(coarseNorth, 0.9f, CdlodEdges.None)
        });

        streamer.Update(sel3);
        Assert.Equal(2, streamer.ResidentCount);
        // Node B and coarseNorth both rebuilt: CreatedTotal goes from 3 to 5, ReleasedTotal from 1 to 3
        Assert.Equal(5, streamer.CreatedTotal);
        Assert.Equal(3, streamer.ReleasedTotal);

        // Dispose releases all resident patches
        streamer.Dispose();
        Assert.Equal(0, streamer.ResidentCount);
        Assert.Equal(5, streamer.ReleasedTotal);
    }

    [Fact]
    public void QuadtreeSelectionOverSpikeKeepsResidencyBounded()
    {
        const int size = 129;
        var map = CreateSyntheticHeightMap(size, size, (x, y) => (x == 64 && y == 64) ? (ushort)8000 : (ushort)100);
        var provider = new CachedHeightRangeProvider(map);
        const int capacity = 32;
        var quadtree = new CdlodQuadtree(size, size, provider, capacity, 1.0f);

        var view = Matrix4x4.CreateLookAt(new Vector3(640f, 640f, 300f), new Vector3(640f, 640f, 0f), Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, 1f, 1f, 5000f);
        var camera = new CdlodCamera(new Vector3(640f, 640f, 300f), new BoundingFrustum(view * proj), 1f);

        var selection = quadtree.Select(camera);

        using var streamer = new TerrainPatchStreamer<FakePatch>(
            (s, b) => new FakePatch(s, b),
            _ => { });

        streamer.Update(selection);

        Assert.True(streamer.ResidentCount <= capacity, $"ResidentCount {streamer.ResidentCount} exceeds capacity {capacity}");
        Assert.Equal(streamer.ResidentCount * 9248L, streamer.ResidentVertexBytes);
    }

    [Fact]
    public void InvalidationDropsOnlyIntersectingCachedRanges()
    {
        var map = CreateSyntheticHeightMap(257, 257);
        var provider = new CachedHeightRangeProvider(map);

        var nodeA = new CdlodNode(0, 0, 0); // (0, 0) to (16, 16)
        var nodeB = new CdlodNode(1, 0, 0); // (16, 0) to (32, 16)
        var nodeC = new CdlodNode(5, 5, 0); // (80, 80) to (96, 96)

        provider.GetHeightRange(nodeA);
        provider.GetHeightRange(nodeB);
        provider.GetHeightRange(nodeC);

        Assert.Equal(3, provider.CacheCount);

        // Invalidate a bounds intersecting only nodeA (X: 2..5, Y: 2..5)
        provider.Invalidate(new Rectangle(2, 2, 4, 4));

        Assert.Equal(2, provider.CacheCount);
        Assert.Equal(2, provider.CachedCount);

        // Invalidate everything via Clear
        provider.Clear();
        Assert.Equal(0, provider.CacheCount);
    }
}
