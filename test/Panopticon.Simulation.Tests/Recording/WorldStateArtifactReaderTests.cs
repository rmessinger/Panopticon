using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Recording;
using Panopticon.Tools.ReplaySketch.Model;
using Xunit;

namespace Panopticon.Tests.Recording;

/// <summary>
/// WI-A5-08: the indexed bounded-memory artifact reader reconstructs forward, backward,
/// frame-zero, and intermediate frames identically to the full load; resident state stays
/// bounded; a torn tail yields an explicit valid prefix.
/// </summary>
[Collection("SerialGameTests")]
public sealed class WorldStateArtifactReaderTests : MockedGameTest, IDisposable
{
    private readonly TestGame _game = new(SageGame.CncGenerals);
    private readonly string _root;

    public WorldStateArtifactReaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "panopticon-a5-reader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public new void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        base.Dispose();
    }

    private string ArtifactPath(string name) => Path.Combine(_root, name);

    private static WorldStateSummary StateAt(uint frame)
    {
        var summary = new WorldStateSummary { Frame = frame };
        summary.PlayerMoney.Add(1000 - frame);
        summary.PlayerMoney.Add(2000 + frame);

        for (var id = 1u; id <= 3; id++)
        {
            summary.Objects.Add(new WorldObjectState
            {
                ObjectId = id,
                DefinitionName = id == 2 && frame % 5 == 0 ? "Renamed" : "Unit",
                OwnerIndex = (int)(id % 2),
                Position = new System.Numerics.Vector3(frame * id, id, 0),
                Health = 100 - frame,
                MaxHealth = 100,
            });
        }

        // Object 10 joins at frame 7 and leaves after frame 15; object 4 joins at frame 30.
        if (frame >= 7 && frame <= 15)
        {
            summary.Objects.Add(new WorldObjectState
            {
                ObjectId = 10,
                DefinitionName = "Temp",
                OwnerIndex = 0,
                Position = new System.Numerics.Vector3(7, 0, 0),
                Health = 5,
                MaxHealth = 5,
            });
        }

        if (frame >= 30)
        {
            summary.Objects.Add(new WorldObjectState
            {
                ObjectId = 4,
                DefinitionName = "Late",
                OwnerIndex = 1,
                Position = new System.Numerics.Vector3(30, 4, 0),
                Health = 50,
                MaxHealth = 50,
            });
        }

        return summary;
    }

    private string WriteArtifact(string name, uint lastFrame, uint snapshotInterval)
    {
        var path = ArtifactPath(name);
        using var writer = new WorldStateArtifactWriter(path, _game);
        var previous = StateAt(0);
        for (var frame = 0u; frame <= lastFrame; frame++)
        {
            var current = StateAt(frame);
            if (frame % snapshotInterval == 0)
            {
                writer.WriteSnapshot(current);
            }
            else
            {
                writer.WriteDelta(WorldStateDelta.Diff(frame, previous, current));
            }

            previous = current;
        }

        return path;
    }

    private static void AssertSummariesEqual(WorldStateSummary expected, WorldStateSummary actual)
    {
        Assert.Equal(expected.Frame, actual.Frame);
        Assert.Equal(expected.PlayerMoney, actual.PlayerMoney);
        Assert.Equal(
            expected.Objects.Select(o => (o.ObjectId, o.Position.X, o.Position.Y, o.Position.Z, o.Health, o.MaxHealth, o.OwnerIndex, o.DefinitionName)),
            actual.Objects.Select(o => (o.ObjectId, o.Position.X, o.Position.Y, o.Position.Z, o.Health, o.MaxHealth, o.OwnerIndex, o.DefinitionName)));
    }

    [Fact]
    public void Forward_Backward_IntermediateFrames_MatchFullLoad()
    {
        var path = WriteArtifact("roundtrip.bin", lastFrame: 30, snapshotInterval: 10);
        var (snapshots, deltas) = WorldStateArtifact.Load(path, _game);
        using var reader = new WorldStateArtifactReader(path, _game);

        var probes = new[] { 0u, 3u, 7u, 10u, 12u, 15u, 20u, 29u, 30u };
        foreach (var frame in probes.Concat(probes.Reverse()))
        {
            AssertSummariesEqual(
                WorldStateArtifact.Reconstruct(snapshots, deltas, frame),
                reader.ReconstructStateAt(frame));
        }
    }

    [Fact]
    public void FrameZero_IsTheBaselineSnapshot()
    {
        var path = WriteArtifact("frame-zero.bin", lastFrame: 12, snapshotInterval: 10);
        using var reader = new WorldStateArtifactReader(path, _game);

        var state = reader.ReconstructStateAt(0);
        Assert.Equal(0u, state.Frame);
        Assert.Equal(0u, reader.FirstFrame);
        Assert.Equal(12u, reader.LastFrame);
        AssertSummariesEqual(StateAt(0), state);
    }

    [Fact]
    public void LongFile_ResidentStateStaysBounded()
    {
        var path = WriteArtifact("long.bin", lastFrame: 600, snapshotInterval: 100);
        using var reader = new WorldStateArtifactReader(path, _game, cacheCapacity: 4);

        Assert.Equal(7, reader.SnapshotFrames.Count);
        Assert.Equal(594, 601 - reader.SnapshotFrames.Count);
        Assert.Equal(601, reader.RecordCount);

        for (var frame = 0u; frame <= 600; frame += 37)
        {
            reader.ReconstructStateAt(frame);
            Assert.True(reader.CachedFrameCount <= 4, $"cache grew to {reader.CachedFrameCount} at frame {frame}");
        }

        // Repeated seeks recycle cache entries instead of accumulating them.
        for (var frame = 0u; frame <= 600; frame += 53)
        {
            reader.ReconstructStateAt(frame);
            Assert.True(reader.CachedFrameCount <= 4, $"cache grew to {reader.CachedFrameCount} on revisit at frame {frame}");
        }
    }

    [Fact]
    public void TruncatedTail_ReturnsValidPrefixAndDiagnostic()
    {
        var complete = WriteArtifact("truncated-source.bin", lastFrame: 12, snapshotInterval: 10);
        var bytes = File.ReadAllBytes(complete);
        var torn = ArtifactPath("truncated.bin");
        File.WriteAllBytes(torn, bytes[..^4]);

        using var reader = new WorldStateArtifactReader(torn, _game);
        Assert.True(reader.IsTruncated);
        Assert.NotNull(reader.TruncationDiagnostic);
        Assert.Contains("offset", reader.TruncationDiagnostic);
        Assert.Equal(11u, reader.LastFrame);

        var (snapshots, deltas) = WorldStateArtifact.Load(complete, _game);
        AssertSummariesEqual(
            WorldStateArtifact.Reconstruct(snapshots, deltas, 11),
            reader.ReconstructStateAt(11));
    }

    [Fact]
    public void Cursor_SeeksClampAndStepAcrossBounds()
    {
        var path = WriteArtifact("cursor.bin", lastFrame: 30, snapshotInterval: 10);
        using var reader = new WorldStateArtifactReader(path, _game);
        var cursor = new RunStateCursor(reader);

        Assert.Equal(0u, cursor.CurrentFrame);
        Assert.Equal(0u, cursor.FirstFrame);
        Assert.Equal(30u, cursor.LastFrame);

        for (var step = 0; step < 3; step++)
        {
            Assert.Equal((uint)(step + 1), cursor.StepForward().Frame);
        }

        Assert.Equal(3u, cursor.CurrentFrame);
        var expectedFrames = new uint[] { 2, 1, 0, 0, 0 };
        for (var step = 0; step < 5; step++)
        {
            Assert.Equal(expectedFrames[step], cursor.StepBackward().Frame);
        }

        Assert.Equal(30u, cursor.SeekTo(999).Frame);
        Assert.Equal(30u, cursor.CurrentFrame);
        Assert.Equal(15u, cursor.SeekTo(15).Frame);
        AssertSummariesEqual(StateAt(15), cursor.SeekTo(15));
    }
}
