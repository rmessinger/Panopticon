using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Mathematics;
using Panopticon.Terrain.Cdlod;
using Xunit;

namespace Panopticon.Tests.Terrain;

public sealed class CdlodSelectionTests
{
    private const int SpikeWidth = 1025;
    private const int SpikeCenter = 512;
    private const float SpikeHeight = 500f;
    private const float Threshold = 2f;

    [Fact]
    public void IdenticalInputsProduceIdenticalSelections()
    {
        var provider = new SpikeProvider(SpikeWidth, SpikeWidth, SpikeCenter, SpikeCenter, SpikeHeight);
        var camera = SpikeCamera();

        var first = new CdlodQuadtree(SpikeWidth, SpikeWidth, provider, 4096, Threshold).Select(camera);
        var second = new CdlodQuadtree(SpikeWidth, SpikeWidth, provider, 4096, Threshold).Select(camera);

        Assert.Equal(first.Nodes, second.Nodes);
        Assert.True(IsSortedByLevelYX(first.Nodes));
    }

    [Fact]
    public void TopDownFlatLatticeSelectsRootOnlyWithExactCoverage()
    {
        const int width = 4109;
        var camera = Camera(new Vector3(20540f, 20540f, 500f), new Vector3(20540f, 20540f, 0f), Wide(), 1f);

        var selection = new CdlodQuadtree(width, width, new ConstantProvider(0f, 0f), 4096, Threshold).Select(camera);

        var node = Assert.Single(selection.Nodes).Node;
        Assert.Equal(9, node.Level);
        AssertExactCoverage(selection, width, width, 97);
    }

    [Fact]
    public void SpikeNearCameraProducesMixedLevelsWithLeafNearSpike()
    {
        var selection = SpikeSelection(4096);

        var levels = selection.Nodes.Select(n => n.Node.Level).ToList();
        Assert.Contains(levels, level => level == 0);
        Assert.True(levels.Max() > 0);

        var nearSpike = selection.Nodes.Where(n => n.Node.Level == 0 && Covers(n.Node.ClippedExtent(SpikeWidth, SpikeWidth), SpikeCenter, SpikeCenter, SpikeWidth, SpikeWidth));
        Assert.Single(nearSpike);
    }

    [Fact]
    public void EdgeAdjacentSelectedNodesDifferByAtMostOneLevel()
    {
        var selection = SpikeSelection(4096);

        foreach (var a in selection.Nodes)
        {
            foreach (var b in selection.Nodes)
            {
                if (Direction(a.Node, b.Node, SpikeWidth, SpikeWidth) != CdlodEdges.None)
                {
                    Assert.True(Math.Abs(a.Node.Level - b.Node.Level) <= 1);
                }
            }
        }
    }

    [Fact]
    public void CullingLeavesOnlyFrustumNodesAndCoversFrustumSamples()
    {
        const int width = 4109;
        var camera = Camera(new Vector3(500f, 500f, 2000f), new Vector3(500f, 500f, 0f), Perspective(0.2f), 1f);

        var selection = new CdlodQuadtree(width, width, new ConstantProvider(0f, 1000f), 4096, 0.1f).Select(camera);

        Assert.All(selection.Nodes, n =>
        {
            var extent = n.Node.ClippedExtent(width, width);
            Assert.True(extent.X < 2048 && extent.Y < 2048);
        });

        for (var y = 0; y < 200; y += 3)
        {
            for (var x = 0; x < 200; x += 3)
            {
                if (camera.Frustum.Contains(new Vector3(x * 10f, y * 10f, 0f)))
                {
                    Assert.Equal(1, selection.Nodes.Count(n => Covers(n.Node.ClippedExtent(width, width), x, y, width, width)));
                }
            }
        }
    }

    [Fact]
    public void RectangularLatticeCoversExactlyWithPartialEdgeNodes()
    {
        const int width = 1000;
        const int height = 300;
        var provider = new SpikeProvider(width, height, 990, 150, SpikeHeight);
        var camera = Camera(new Vector3(9900f, 1500f, 100f), new Vector3(9900f, 1500f, 0f), Wide(), 1f);

        var selection = new CdlodQuadtree(width, height, provider, 4096, Threshold).Select(camera);

        AssertExactCoverage(selection, width, height, 11);
        Assert.Contains(selection.Nodes, n => PartialExtent(n.Node, width, height));
    }

    [Fact]
    public void DegenerateLatticeAndCapacityThrow()
    {
        var provider = new ConstantProvider(0f, 0f);

        Assert.Throws<ArgumentOutOfRangeException>(() => new CdlodQuadtree(1, 100, provider, 10, Threshold));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CdlodQuadtree(100, 1, provider, 10, Threshold));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CdlodQuadtree(100, 100, provider, 0, Threshold));
    }

    [Fact]
    public void DegenerateConstructorArgumentsThrow()
    {
        var provider = new ConstantProvider(0f, 0f);

        Assert.Throws<ArgumentNullException>(() => new CdlodQuadtree(100, 100, null!, 10, Threshold));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CdlodQuadtree(100, 100, provider, 10, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CdlodQuadtree(100, 100, provider, 10, -1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CdlodQuadtree(100, 100, provider, 10, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CdlodQuadtree(100, 100, provider, 10, float.PositiveInfinity));
    }

    [Fact]
    public void DegenerateSelectArgumentsThrow()
    {
        var quadtree = new CdlodQuadtree(100, 100, new ConstantProvider(0f, 0f), 10, Threshold);
        var camera = SpikeCamera();

        Assert.Throws<ArgumentNullException>(() => quadtree.Select(new CdlodCamera(Vector3.Zero, null!, 1f)));
        Assert.Throws<ArgumentOutOfRangeException>(() => quadtree.Select(new CdlodCamera(camera.Position, camera.Frustum, 0f)));
        Assert.Throws<ArgumentOutOfRangeException>(() => quadtree.Select(new CdlodCamera(camera.Position, camera.Frustum, -1f)));
        Assert.Throws<ArgumentOutOfRangeException>(() => quadtree.Select(new CdlodCamera(camera.Position, camera.Frustum, float.NaN)));
        Assert.Throws<ArgumentOutOfRangeException>(() => quadtree.Select(new CdlodCamera(camera.Position, camera.Frustum, float.PositiveInfinity)));
    }

    [Fact]
    public void InvalidProviderRangesThrow()
    {
        var camera = SpikeCamera();

        var inverted = new CdlodQuadtree(100, 100, new ConstantProvider(100f, 10f), 10, Threshold);
        Assert.Throws<InvalidOperationException>(() => inverted.Select(camera));

        var nanMin = new CdlodQuadtree(100, 100, new ConstantProvider(float.NaN, 10f), 10, Threshold);
        Assert.Throws<InvalidOperationException>(() => nanMin.Select(camera));

        var nanMax = new CdlodQuadtree(100, 100, new ConstantProvider(0f, float.NaN), 10, Threshold);
        Assert.Throws<InvalidOperationException>(() => nanMax.Select(camera));

        var infiniteMin = new CdlodQuadtree(100, 100, new ConstantProvider(float.NegativeInfinity, 10f), 10, Threshold);
        Assert.Throws<InvalidOperationException>(() => infiniteMin.Select(camera));

        var infiniteMax = new CdlodQuadtree(100, 100, new ConstantProvider(0f, float.PositiveInfinity), 10, Threshold);
        Assert.Throws<InvalidOperationException>(() => infiniteMax.Select(camera));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(20)]
    public void TightCapacitiesSatisfyBoundCoverageAndRestrictedRule(int maxNodes)
    {
        var selection = SpikeSelection(maxNodes);

        Assert.True(selection.Nodes.Count <= maxNodes);
        AssertExactCoverage(selection, SpikeWidth, SpikeWidth, 29);

        foreach (var a in selection.Nodes)
        {
            foreach (var b in selection.Nodes)
            {
                if (Direction(a.Node, b.Node, SpikeWidth, SpikeWidth) != CdlodEdges.None)
                {
                    Assert.True(Math.Abs(a.Node.Level - b.Node.Level) <= 1);
                }
            }
        }
    }

    [Fact]
    public void CameraContainingNodeIsLeafWhenErrorIsHigh()
    {
        var selection = SpikeSelection(4096);

        Assert.Contains(
            selection.Nodes,
            n => n.Node.Level == 0 && Covers(n.Node.ClippedExtent(SpikeWidth, SpikeWidth), SpikeCenter, SpikeCenter, SpikeWidth, SpikeWidth));
    }

    [Fact]
    public void CapacityBoundsSelectionAndKeepsCoverage()
    {
        const int maxNodes = 20;

        var selection = SpikeSelection(maxNodes);

        Assert.True(selection.Nodes.Count <= maxNodes);
        AssertExactCoverage(selection, SpikeWidth, SpikeWidth, 29);
    }

    [Fact]
    public void PreviouslySplitNodeStaysSplitWithinHysteresisBand()
    {
        const int width = 65;
        var provider = new ConstantProvider(0f, 900f);
        var camera = Camera(new Vector3(320f, 320f, 1000f), new Vector3(320f, 320f, 0f), Wide(), 1f);

        var previous = new CdlodQuadtree(width, width, provider, 4096, 1f).Select(camera);
        var withoutPrevious = new CdlodQuadtree(width, width, provider, 4096, 10f).Select(camera);
        var withPrevious = new CdlodQuadtree(width, width, provider, 4096, 10f).Select(camera, previous);

        Assert.Single(withoutPrevious.Nodes);
        Assert.True(withPrevious.Nodes.Count > 1);
    }

    [Fact]
    public void MorphFactorDecreasesAsScreenErrorIncreases()
    {
        const int width = 65;
        var provider = new ConstantProvider(0f, 100f);

        var near = Camera(new Vector3(320f, 320f, 300f), new Vector3(320f, 320f, 0f), Wide(), 1f);
        var far = Camera(new Vector3(320f, 320f, 500f), new Vector3(320f, 320f, 0f), Wide(), 1f);

        var nearSelection = new CdlodQuadtree(width, width, provider, 4096, 10f).Select(near);
        var farSelection = new CdlodQuadtree(width, width, provider, 4096, 10f).Select(far);

        var nearMorph = Assert.Single(nearSelection.Nodes).MorphFactor;
        var farMorph = Assert.Single(farSelection.Nodes).MorphFactor;
        Assert.True(farMorph > nearMorph);
        Assert.True(Math.Abs(nearMorph - 0.95f) < 1e-4f);
        Assert.True(Math.Abs(farMorph - 0.975f) < 1e-4f);
    }

    [Fact]
    public void HugeLatticeSelectsRootWithoutOverflow()
    {
        const int width = int.MaxValue;
        var camera = Camera(new Vector3(1e9f, 1e9f, 500f), new Vector3(1e9f, 1e9f, 0f), Wide(), 1f);

        var selection = new CdlodQuadtree(width, width, new ConstantProvider(0f, 0f), 4096, Threshold).Select(camera);

        var selected = Assert.Single(selection.Nodes);
        Assert.Equal(27, selected.Node.Level);
        Assert.Equal(0, selected.Node.X);
        Assert.Equal(0, selected.Node.Y);
    }

    [Fact]
    public void CoarserEdgesMatchNeighbourLevels()
    {
        var selection = SpikeSelection(4096);

        foreach (var a in selection.Nodes)
        {
            var expected = CdlodEdges.None;
            foreach (var b in selection.Nodes)
            {
                var direction = Direction(a.Node, b.Node, SpikeWidth, SpikeWidth);
                if (direction != CdlodEdges.None && b.Node.Level == a.Node.Level + 1)
                {
                    expected |= direction;
                }
            }

            Assert.Equal(expected, a.CoarserEdges);
        }

        Assert.Contains(selection.Nodes, n => n.CoarserEdges != CdlodEdges.None);
    }

    [Fact]
    public void ProviderIsCalledOncePerVisitedNodeWithinBound()
    {
        const int width = 4109;
        var provider = new CountingProvider(new ConstantProvider(0f, 0f));
        var camera = Camera(new Vector3(20540f, 20540f, 500f), new Vector3(20540f, 20540f, 0f), Wide(), 1f);

        var selection = new CdlodQuadtree(width, width, provider, 4096, Threshold).Select(camera);

        Assert.Equal(provider.Calls.Distinct().Count(), provider.Calls.Count);
        Assert.True(provider.Calls.Count <= (4 * selection.Nodes.Count) + 10);
    }

    private static CdlodSelection SpikeSelection(int maxNodes)
    {
        var provider = new SpikeProvider(SpikeWidth, SpikeWidth, SpikeCenter, SpikeCenter, SpikeHeight);
        return new CdlodQuadtree(SpikeWidth, SpikeWidth, provider, maxNodes, Threshold).Select(SpikeCamera());
    }

    private static CdlodCamera SpikeCamera()
    {
        return Camera(new Vector3(5120f, 5120f, 100f), new Vector3(5120f, 5120f, 0f), Wide(), 1f);
    }

    private static CdlodCamera Camera(Vector3 position, Vector3 target, Matrix4x4 projection, float projectedScale)
    {
        var view = Matrix4x4.CreateLookAt(position, target, Vector3.UnitY);
        return new CdlodCamera(position, new BoundingFrustum(view * projection), projectedScale);
    }

    private static Matrix4x4 Wide()
    {
        return Matrix4x4.CreateOrthographic(60000f, 60000f, 1f, 1000000f);
    }

    private static Matrix4x4 Perspective(float fov)
    {
        return Matrix4x4.CreatePerspectiveFieldOfView(fov, 1f, 1f, 1000000f);
    }

    private static void AssertExactCoverage(CdlodSelection selection, int width, int height, int step)
    {
        foreach (var y in Samples(height, step))
        {
            foreach (var x in Samples(width, step))
            {
                var covering = selection.Nodes.Count(n => Covers(n.Node.ClippedExtent(width, height), x, y, width, height));
                Assert.Equal(1, covering);
            }
        }
    }

    private static IEnumerable<int> Samples(int count, int step)
    {
        return Enumerable.Range(0, ((count - 1) / step) + 1).Select(i => i * step).Append(count - 1);
    }

    private static bool Covers(Rectangle extent, int x, int y, int width, int height)
    {
        return InSpan(x, extent.X, extent.Right, width) && InSpan(y, extent.Y, extent.Bottom, height);
    }

    private static bool InSpan(int value, int start, int end, int count)
    {
        return value >= start && (value < end - 1 || (end == count && value == end - 1));
    }

    private static bool PartialExtent(CdlodNode node, int width, int height)
    {
        var extent = node.ClippedExtent(width, height);
        var full = (16 << node.Level) + 1;
        return extent.Width < full || extent.Height < full;
    }

    private static CdlodEdges Direction(CdlodNode a, CdlodNode b, int width, int height)
    {
        var ea = a.ClippedExtent(width, height);
        var eb = b.ClippedExtent(width, height);
        var rowsOverlap = ea.Y < eb.Bottom - 1 && eb.Y < ea.Bottom - 1;
        var columnsOverlap = ea.X < eb.Right - 1 && eb.X < ea.Right - 1;

        if (rowsOverlap && ea.Right - 1 == eb.X)
        {
            return CdlodEdges.East;
        }

        if (rowsOverlap && eb.Right - 1 == ea.X)
        {
            return CdlodEdges.West;
        }

        if (columnsOverlap && ea.Bottom - 1 == eb.Y)
        {
            return CdlodEdges.North;
        }

        if (columnsOverlap && eb.Bottom - 1 == ea.Y)
        {
            return CdlodEdges.South;
        }

        return CdlodEdges.None;
    }

    private static bool IsSortedByLevelYX(IReadOnlyList<CdlodSelectedNode> nodes)
    {
        for (var i = 1; i < nodes.Count; i++)
        {
            var p = nodes[i - 1].Node;
            var c = nodes[i].Node;
            var ordered = p.Level != c.Level ? p.Level < c.Level : p.Y != c.Y ? p.Y < c.Y : p.X <= c.X;
            if (!ordered)
            {
                return false;
            }
        }

        return true;
    }

    private sealed class ConstantProvider(float min, float max) : ICdlodErrorProvider
    {
        public (float Min, float Max) GetHeightRange(CdlodNode node)
        {
            return (min, max);
        }
    }

    private sealed class SpikeProvider(int width, int height, int spikeX, int spikeY, float spikeHeight) : ICdlodErrorProvider
    {
        public (float Min, float Max) GetHeightRange(CdlodNode node)
        {
            var extent = node.ClippedExtent(width, height);
            var contains = spikeX >= extent.X && spikeX < extent.Right && spikeY >= extent.Y && spikeY < extent.Bottom;
            return contains ? (0f, spikeHeight) : (0f, 0f);
        }
    }

    private sealed class CountingProvider(ICdlodErrorProvider inner) : ICdlodErrorProvider
    {
        public List<CdlodNode> Calls { get; } = new();

        public (float Min, float Max) GetHeightRange(CdlodNode node)
        {
            Calls.Add(node);
            return inner.GetHeightRange(node);
        }
    }
}
