#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Panopticon;
using Panopticon.Recording;
using Panopticon.Simulation;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// Card WI-A5-24: viewport-driven region streaming over a region-indexed artifact. Primitives,
/// decoded entities, and cached frames stay bounded by the viewport and cache capacity, not the
/// run length. Engine-free: artifacts are written through the SageGame-only writer constructor.
/// </summary>
public sealed class ReplayStreamingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rs-streaming-{Guid.NewGuid():N}");

    private const float CellSize = 100f;

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ViewportQuery_ProducesNoPrimitivesForOffScreenEntities()
    {
        var path = WriteIndexedArtifact(entitiesPerCell: 4, cellsX: 6, cellsY: 1, lastFrame: 0);
        using var reader = new WorldStateArtifactReader(path, SageGame.CncGenerals);
        var streamer = new ReplayStreamingService(reader, CellSize);

        // Viewport covers only cell 0 ([0,100)); cells 1-5 hold 20 off-screen entities.
        var result = streamer.Query(0, new WorldViewport(0, 0, 80, 80), prefetchCells: 0);

        Assert.Equal(4, result.VisibleEntityCount);
        Assert.Equal(4, result.Primitives.Count);
        Assert.All(result.Primitives, p => Assert.Equal("cell:0,0", ((DomainVisualPoint)p).BucketKey));
    }

    [Fact]
    public void PanAndZoom_UpdateTheVisibleSet()
    {
        var path = WriteIndexedArtifact(entitiesPerCell: 2, cellsX: 6, cellsY: 1, lastFrame: 0);
        using var reader = new WorldStateArtifactReader(path, SageGame.CncGenerals);
        var streamer = new ReplayStreamingService(reader, CellSize);

        var left = streamer.Query(0, new WorldViewport(0, 0, 80, 80), prefetchCells: 0);
        var right = streamer.Query(0, new WorldViewport(500, 0, 80, 80), prefetchCells: 0);
        Assert.All(left.Primitives, p => Assert.Equal("cell:0,0", ((DomainVisualPoint)p).BucketKey));
        Assert.All(right.Primitives, p => Assert.Equal("cell:5,0", ((DomainVisualPoint)p).BucketKey));

        var zoomedOut = streamer.Query(0, new WorldViewport(0, 0, 600, 100), prefetchCells: 0);
        Assert.Equal(12, zoomedOut.VisibleEntityCount);
    }

    [Fact]
    public void Prefetch_DecodesAdjacentRegionsWithoutDrawingThem()
    {
        var path = WriteIndexedArtifact(entitiesPerCell: 3, cellsX: 4, cellsY: 1, lastFrame: 0);
        using var reader = new WorldStateArtifactReader(path, SageGame.CncGenerals);
        var streamer = new ReplayStreamingService(reader, CellSize);

        // Viewport on cell 1 with one prefetch ring: cells 0-2 decode (9 entities), only the
        // viewport cell's 3 entities produce primitives.
        var result = streamer.Query(0, new WorldViewport(100, 0, 80, 80), prefetchCells: 1);

        Assert.Equal(3, result.VisibleEntityCount);
        Assert.Equal(3, result.Primitives.Count);
        Assert.Equal(9, result.DecodedEntityCount);
    }

    [Fact]
    public void LongRun_CachedFramesStayBoundedByCapacity()
    {
        var path = WriteIndexedArtifact(entitiesPerCell: 2, cellsX: 3, cellsY: 1, lastFrame: 40);
        using var reader = new WorldStateArtifactReader(path, SageGame.CncGenerals);
        var streamer = new ReplayStreamingService(reader, CellSize, cacheCapacity: 3);

        var viewport = new WorldViewport(0, 0, 80, 80);
        for (var frame = 0u; frame <= 40u; frame += 2)
        {
            var result = streamer.Query(frame, viewport, prefetchCells: 0);
            Assert.Equal(2, result.VisibleEntityCount);
            Assert.InRange(result.CachedFrames, 1, 3);
        }
    }

    [Fact]
    public void RegionKeyQuery_ServesGraphTopologiesThroughTheSameApi()
    {
        // Graph fixtures have no spatial cells; visibility is the selected focus set.
        var path = WriteIndexedArtifact(entitiesPerCell: 2, cellsX: 3, cellsY: 1, lastFrame: 0);
        using var reader = new WorldStateArtifactReader(path, SageGame.CncGenerals);
        var streamer = new ReplayStreamingService(reader, CellSize);

        var result = streamer.Query(0, (IReadOnlyCollection<string>)["cell:1,0"]);

        Assert.Equal(2, result.VisibleEntityCount);
        Assert.All(result.Primitives, p => Assert.Equal("cell:1,0", ((DomainVisualPoint)p).BucketKey));
    }

    [Fact]
    public void V1Artifact_FallsBackToWholeFrameDecodeAndStillCullsPrimitives()
    {
        var path = WriteV1Artifact(entityCount: 10, lastFrame: 0);
        using var reader = new WorldStateArtifactReader(path, SageGame.CncGenerals);
        var streamer = new ReplayStreamingService(reader, CellSize);

        var result = streamer.Query(0, new WorldViewport(0, 0, 50, 100), prefetchCells: 0);

        Assert.True(result.UsedWholeFrameFallback);
        // Entities sit at x = 0,10,...,90; the viewport [0,50] keeps the first six.
        Assert.Equal(6, result.VisibleEntityCount);
        Assert.Equal(6, result.Primitives.Count);
    }

    private string WriteIndexedArtifact(int entitiesPerCell, int cellsX, int cellsY, uint lastFrame)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"indexed-{Guid.NewGuid():N}.bin");
        using var writer = new WorldStateArtifactWriter(path, SageGame.CncGenerals, ClassifyByCell, null);
        for (var frame = 0u; frame <= lastFrame; frame++)
        {
            var summary = IndexedSummaryAt(frame, entitiesPerCell, cellsX, cellsY);
            if (frame % 10 == 0)
            {
                writer.WriteSnapshot(summary);
            }
            else
            {
                writer.WriteDelta(WorldStateDelta.Diff(frame, IndexedSummaryAt(frame - 1, entitiesPerCell, cellsX, cellsY), summary));
            }
        }

        return path;
    }

    private static WorldStateSummary IndexedSummaryAt(uint frame, int entitiesPerCell, int cellsX, int cellsY)
    {
        var summary = new WorldStateSummary { Frame = frame };
        summary.PlayerMoney.AddRange([1000u - frame, 2000u + frame]);
        var id = 1u;
        for (var cellY = 0; cellY < cellsY; cellY++)
        {
            for (var cellX = 0; cellX < cellsX; cellX++)
            {
                for (var i = 0; i < entitiesPerCell; i++)
                {
                    summary.Objects.Add(Object(id++, cellX * CellSize + 10 + i * 20, cellY * CellSize + 10));
                }
            }
        }

        summary.Objects.Sort((a, b) => a.ObjectId.CompareTo(b.ObjectId));
        return summary;
    }

    private static string ClassifyByCell(WorldObjectState obj)
    {
        return RecordedStateProjector.BucketKey(obj.Position.X, obj.Position.Y, CellSize);
    }

    private string WriteV1Artifact(int entityCount, uint lastFrame)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"v1-{Guid.NewGuid():N}.bin");
        using var writer = new WorldStateArtifactWriter(path, SageGame.CncGenerals);
        for (var frame = 0u; frame <= lastFrame; frame++)
        {
            var summary = new WorldStateSummary { Frame = frame };
            summary.PlayerMoney.AddRange([500u, 600u]);
            for (var i = 0; i < entityCount; i++)
            {
                summary.Objects.Add(Object((uint)(i + 1), i * 10, 0));
            }

            if (frame % 10 == 0)
            {
                writer.WriteSnapshot(summary);
            }
            else
            {
                writer.WriteDelta(WorldStateDelta.Diff(frame, PreviousV1(frame, entityCount), summary));
            }
        }

        return path;
    }

    private static WorldStateSummary PreviousV1(uint frame, int entityCount)
    {
        var summary = new WorldStateSummary { Frame = frame - 1 };
        summary.PlayerMoney.AddRange([500u, 600u]);
        for (var i = 0; i < entityCount; i++)
        {
            summary.Objects.Add(Object((uint)(i + 1), i * 10, 0));
        }

        return summary;
    }

    private static WorldObjectState Object(uint id, float x, float y)
    {
        return new WorldObjectState
        {
            ObjectId = id,
            DefinitionName = $"Thing{id % 3}",
            OwnerIndex = (int)(id % 2),
            Position = new System.Numerics.Vector3(x, y, 0),
            Health = 100,
            MaxHealth = 100,
        };
    }
}
