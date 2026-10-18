#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon;
using Panopticon.Recording;
using Panopticon.Simulation;
using Panopticon.Tools.ReplaySketch.Profiles;
using Panopticon.Tools.ReplaySketch.Services;
using SkiaSharp;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// Card WI-A5-25: replay primitives batch through (owner, kind, LOD) groups with one submission
/// per batch, mirroring B5's instance-batch contract, and select LODs by projected screen size.
/// All counts are asserted headlessly; no GPU code is touched.
/// </summary>
public sealed class ReplayLodBatchingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rs-lod-{Guid.NewGuid():N}");

    private const float CellSize = 100f;

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void RepeatedEntities_BatchIntoOneSubmission()
    {
        var points = Repeat(owner: 0, kind: "AmericaTankCrusader", size: 1f, count: 500);

        var (batches, stats) = ReplayPrimitiveBatcher.BuildBatches(points, worldToScreenScale: 1f);

        var batch = Assert.Single(batches);
        Assert.Equal(500, batch.Points.Count);
        Assert.Equal(1, stats.BatchCount);
        Assert.Equal(ReplayLodLevel.Dot, batch.Lod);
    }

    [Fact]
    public void MixedOwnersAndKinds_SplitIntoBatches()
    {
        var points = new List<DomainVisualPoint>();
        points.AddRange(Repeat(0, "KindA", 1f, 10));
        points.AddRange(Repeat(1, "KindA", 1f, 10));
        points.AddRange(Repeat(0, "KindB", 1f, 10));
        points.AddRange(Repeat(1, "KindB", 1f, 10));

        var (batches, stats) = ReplayPrimitiveBatcher.BuildBatches(points, worldToScreenScale: 1f);

        Assert.Equal(4, stats.BatchCount);
        Assert.All(batches, b => Assert.Equal(10, b.Points.Count));
    }

    [Fact]
    public void LodThresholds_SelectAtDocumentedSizes()
    {
        var options = ReplayLodOptions.Default;
        Assert.Equal(ReplayLodLevel.Full, options.Select(6f));
        Assert.Equal(ReplayLodLevel.Dot, options.Select(2f));
        Assert.Equal(ReplayLodLevel.Pixel, options.Select(1.99f));

        // Scale 1: size 3 projects to a 6px diameter (Full), 1 to 2px (Dot), 0.5 to 1px (Pixel).
        var points = new List<DomainVisualPoint>
        {
            Point(0, "Kind", 3f),
            Point(0, "Kind", 1f),
            Point(0, "Kind", 0.5f),
        };

        var (batches, stats) = ReplayPrimitiveBatcher.BuildBatches(points, worldToScreenScale: 1f);

        Assert.Equal(3, stats.BatchCount);
        Assert.Equal(1, stats.FullCount);
        Assert.Equal(1, stats.DotCount);
        Assert.Equal(1, stats.PixelCount);
        Assert.Contains(batches, b => b.Lod == ReplayLodLevel.Full && b.Points.Count == 1);
        Assert.Contains(batches, b => b.Lod == ReplayLodLevel.Dot && b.Points.Count == 1);
        Assert.Contains(batches, b => b.Lod == ReplayLodLevel.Pixel && b.Points.Count == 1);
    }

    [Fact]
    public void Renderer_DrawsBatchedLods_AndKeepsHitTestMarkers()
    {
        var primitives = new List<DomainVisualPrimitive>
        {
            Point(0, "Kind", 3f),
            Point(0, "Kind", 1f),
            Point(1, "Kind", 0.5f),
        };
        var markers = new List<FramePointMarker>
        {
            new(11, (DomainVisualPoint)primitives[0]),
            new(12, (DomainVisualPoint)primitives[1]),
            new(13, (DomainVisualPoint)primitives[2]),
        };

        using var surface = SKSurface.Create(new SKImageInfo(300, 200));
        var result = RunFrameRenderer.Render(
            surface.Canvas, 0, 0, 300, 200, primitives, markers, selectedObjectId: 12, lodOptions: ReplayLodOptions.Default);

        Assert.Equal(3, result.Count);
        var selected = Assert.Single(result, m => m.ObjectId == 12);
        Assert.Equal(0, selected.OwnerIndex);
        Assert.NotNull(RunFrameRenderer.HitTest(result, selected.ScreenX, selected.ScreenY));
    }

    [Fact]
    public void StreamingCullingOutput_FeedsBatching()
    {
        var path = WriteIndexedArtifact();
        using var reader = new WorldStateArtifactReader(path, SageGame.CncGenerals);
        var streamer = new ReplayStreamingService(reader, CellSize);

        var streamed = streamer.Query(0, new WorldViewport(0, 0, 80, 80), prefetchCells: 0);
        var visiblePoints = streamed.Primitives.Cast<DomainVisualPoint>().ToList();

        var (batches, stats) = ReplayPrimitiveBatcher.BuildBatches(visiblePoints, worldToScreenScale: 1f);

        // Four visible entities, alternating two kinds, one owner, one LOD: two batches.
        Assert.Equal(4, visiblePoints.Count);
        Assert.Equal(4, batches.Sum(b => b.Points.Count));
        Assert.Equal(2, stats.BatchCount);
        Assert.All(visiblePoints, p => Assert.Equal("cell:0,0", p.BucketKey));
    }

    private static List<DomainVisualPoint> Repeat(int owner, string kind, float size, int count)
    {
        var points = new List<DomainVisualPoint>(count);
        for (var i = 0; i < count; i++)
        {
            points.Add(Point(owner, kind, size));
        }

        return points;
    }

    private static DomainVisualPoint Point(int owner, string kind, float size)
    {
        return new DomainVisualPoint("cell:0,0", new System.Numerics.Vector3(10, 10, 0), owner, kind, size);
    }

    private string WriteIndexedArtifact()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"lod-{Guid.NewGuid():N}.bin");
        using var writer = new WorldStateArtifactWriter(path, SageGame.CncGenerals, ClassifyByCell, null);
        var summary = new WorldStateSummary { Frame = 0 };
        summary.PlayerMoney.AddRange([1000u, 2000u]);
        for (var cellX = 0; cellX < 6; cellX++)
        {
            for (var i = 0; i < 4; i++)
            {
                var id = (uint)(cellX * 4 + i + 1);
                summary.Objects.Add(new WorldObjectState
                {
                    ObjectId = id,
                    DefinitionName = id % 2 == 0 ? "ThingEven" : "ThingOdd",
                    OwnerIndex = 0,
                    Position = new System.Numerics.Vector3(cellX * CellSize + 10 + i * 20, 10, 0),
                    Health = 100,
                    MaxHealth = 100,
                });
            }
        }

        writer.WriteSnapshot(summary);
        return path;
    }

    private static string ClassifyByCell(WorldObjectState obj)
    {
        return RecordedStateProjector.BucketKey(obj.Position.X, obj.Position.Y, CellSize);
    }
}
