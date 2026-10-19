#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon;
using Panopticon.Recording;
using Xunit;

namespace Panopticon.Tests.Recording;

/// <summary>
/// Card WI-A5-29: region-indexed state artifacts. A classifier-bearing writer emits v2 records
/// (region snapshots, region deltas, frame globals behind a schema header); the indexed reader
/// answers whole-frame and per-region queries from the same file. Engine-free: the SageGame
/// constructor path on writer and reader covers install-free construction (WI-A5-04).
/// </summary>
public sealed class RegionIndexedStateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rs-regionindex-{Guid.NewGuid():N}");

    private static string Classify(WorldObjectState o)
    {
        return o.Position.X < 100f ? "near" : "far";
    }

    private static WorldObjectState Object(uint id, string definition, int owner, float x, float y, float health)
    {
        return new WorldObjectState
        {
            ObjectId = id,
            DefinitionName = definition,
            OwnerIndex = owner,
            Position = new System.Numerics.Vector3(x, y, 0f),
            Health = health,
            MaxHealth = 100f,
        };
    }

    private static WorldStateSummary SummaryAt(uint frame, IReadOnlyList<WorldObjectState> objects)
    {
        var summary = new WorldStateSummary { Frame = frame };
        summary.Objects.AddRange(objects.OrderBy(o => o.ObjectId));
        summary.PlayerMoney.AddRange([1000u - frame, 2000u + frame]);
        return summary;
    }

    private static List<WorldObjectState> BaseObjects()
    {
        return
        [
            Object(1, "Thing1", 0, 10f, 10f, 90f),
            Object(2, "Thing2", 0, 20f, 20f, 80f),
            Object(3, "FarThing", 1, 150f, 30f, 70f),
        ];
    }

    /// <summary>Writes frames 0..lastFrame with a snapshot at 0 and deltas after each frame.</summary>
    private (string Path, Dictionary<uint, WorldStateSummary> Summaries) WriteIndexed(
        Func<uint, List<WorldObjectState>> objectsAt,
        uint lastFrame,
        string name = "indexed.bin")
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, name);
        var summaries = new Dictionary<uint, WorldStateSummary>();
        using (var writer = new WorldStateArtifactWriter(path, SageGame.CncGenerals, Classify))
        {
            WorldStateSummary? previous = null;
            for (var frame = 0u; frame <= lastFrame; frame++)
            {
                var summary = SummaryAt(frame, objectsAt(frame));
                summaries[frame] = summary;
                if (previous is null)
                {
                    writer.WriteSnapshot(summary);
                }
                else
                {
                    writer.WriteDelta(WorldStateDelta.Diff(frame, previous, summary));
                }

                previous = summary;
            }
        }

        return (path, summaries);
    }

    private static void AssertStatesEqual(WorldStateSummary expected, WorldStateSummary actual)
    {
        Assert.Equal(expected.Frame, actual.Frame);
        Assert.Equal(expected.PlayerMoney, actual.PlayerMoney);
        Assert.Equal(
            expected.Objects.Select(o => (o.ObjectId, o.DefinitionName, o.OwnerIndex, o.Position.X, o.Health, o.MaxHealth)),
            actual.Objects.Select(o => (o.ObjectId, o.DefinitionName, o.OwnerIndex, o.Position.X, o.Health, o.MaxHealth)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void MultiRegion_QueryMatchesFilteredWholeFrame()
    {
        var (path, summaries) = WriteIndexed(frame => frame switch
        {
            <= 2u => BaseObjects(),
            _ => [.. BaseObjects(), Object(4, "Thing4", 1, 30f, 40f, 60f)],
        }, lastFrame: 6);

        using var reader = new WorldStateArtifactReader(path, SageGame.CncGenerals);
        Assert.True(reader.HasRegionIndex);
        Assert.Contains("near", reader.KnownRegions);
        Assert.Contains("far", reader.KnownRegions);

        var whole = reader.ReconstructStateAt(5u);
        Assert.Equal([1u, 2u, 3u, 4u], whole.Objects.Select(o => o.ObjectId).OrderBy(id => id));

        var near = reader.ReconstructRegionStateAt(5u, ["near"]);
        Assert.Equal([1u, 2u, 4u], near.Objects.Select(o => o.ObjectId).OrderBy(id => id));
        Assert.Equal(summaries[5u].PlayerMoney, near.PlayerMoney);

        var far = reader.ReconstructRegionStateAt(5u, ["far"]);
        Assert.Equal([3u], far.Objects.Select(o => o.ObjectId));
        AssertStatesEqual(summaries[5u], whole);
    }

    [Fact]
    public void MovedBetweenRegions_ReconstructsExactlyOnce()
    {
        var (path, summaries) = WriteIndexed(frame => frame switch
        {
            <= 3u => [Object(7, "Mover", 0, 40f, 10f, 95f)],
            _ => [Object(7, "Mover", 0, 160f, 10f, 95f)],
        }, lastFrame: 6);

        using var reader = new WorldStateArtifactReader(path, SageGame.CncGenerals);

        var wholeAfter = reader.ReconstructStateAt(5u);
        Assert.Single(wholeAfter.Objects, o => o.ObjectId == 7u);
        Assert.Equal(160f, wholeAfter.Objects.Single(o => o.ObjectId == 7u).Position.X);

        var farAfter = reader.ReconstructRegionStateAt(5u, ["far"]);
        Assert.Contains(7u, farAfter.Objects.Select(o => o.ObjectId));

        var nearAfter = reader.ReconstructRegionStateAt(5u, ["near"]);
        Assert.DoesNotContain(7u, nearAfter.Objects.Select(o => o.ObjectId));

        var nearBefore = reader.ReconstructRegionStateAt(3u, ["near"]);
        Assert.Contains(7u, nearBefore.Objects.Select(o => o.ObjectId));
        AssertStatesEqual(summaries[5u], wholeAfter);
    }

    [Fact]
    public void LongRun_RegionQuery_DecodesOnlyRequestedRegions()
    {
        const int nearCount = 150;
        const int farCount = 150;
        // Near churns every frame; far is static, so the artifact's per-frame delta work is
        // almost entirely near-region records that a far-only query never decodes.
        var near = Enumerable.Range(1, nearCount).Select(i => Object((uint)i, "Near", 0, (i % 90) + 1f, i, 90f)).ToList();
        var far = Enumerable.Range(1, farCount).Select(i => Object((uint)(i + nearCount), "Far", 1, 110f + (i % 50), i, 80f)).ToList();
        var all = near.Concat(far).ToList();

        var (path, _) = WriteIndexed(frame => [.. all.Select(o => o.DefinitionName == "Near" ? Object(o.ObjectId, o.DefinitionName, o.OwnerIndex, o.Position.X, o.Position.Y, 90f - (frame % 3f)) : o)], lastFrame: 100, name: "longrun.bin");

        using var reader = new WorldStateArtifactReader(path, SageGame.CncGenerals);
        var farState = reader.ReconstructRegionStateAt(100u, ["far"]);

        Assert.Equal(farCount, farState.Objects.Count);
        // 203 records: header, two snapshot parts, and one near-region delta part plus a
        // frame global per frame. Each near part carries 150 changed entities, so the file
        // holds ~15,000 entity records the far-only query never decodes.
        Assert.True(reader.RecordCount > 100, $"record count {reader.RecordCount}");
        Assert.True(reader.DecodedEntitiesLastQuery < (uint)farCount + 50u, $"decoded {reader.DecodedEntitiesLastQuery}");
    }

    [Fact]
    public void IncompatibleSchemaVersion_RejectedAtConstruction()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "badheader.bin");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        using (var writer = new StateWriter(stream, SageGame.CncGenerals))
        {
            var tag = RegionIndexedFormat.SchemaHeaderTag;
            writer.PersistByte(ref tag, "RecordType");
            var header = new RegionSchemaHeader { SchemaVersion = 99, RegionSchemaId = "topology-key/99" };
            writer.PersistObject(header, "header");
        }

        var error = Assert.Throws<InvalidOperationException>(() => new WorldStateArtifactReader(path, SageGame.CncGenerals));
        Assert.Contains("99", error.Message);
        Assert.Contains("1", error.Message);
    }

    [Fact]
    public void WholeFrame_Query_MatchesAuthoredSummaries()
    {
        var (path, summaries) = WriteIndexed(frame => frame switch
        {
            <= 1u => BaseObjects(),
            2u => [.. BaseObjects(), Object(5, "Thing5", 0, 60f, 60f, 50f)],
            <= 4u => [.. BaseObjects(), Object(5, "Thing5", 0, 60f, 60f, 45f)],
            _ => [BaseObjects()[0], BaseObjects()[2], Object(5, "Thing5", 0, 60f, 60f, 45f)],
        }, lastFrame: 6);

        using var reader = new WorldStateArtifactReader(path, SageGame.CncGenerals);
        foreach (var frame in new uint[] { 2u, 4u, 6u })
        {
            AssertStatesEqual(summaries[frame], reader.ReconstructStateAt(frame));
        }

        Assert.Equal([0u], reader.SnapshotFrames);
        Assert.Equal(6u, reader.LastFrame);
    }
}
