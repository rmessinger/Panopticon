using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Simulation;
using Xunit;

namespace Panopticon.Tests.Simulation;

public sealed class DomainStateFrameTests : StatePersisterTest
{
    private static DomainStateField Int(string name, string family = "vehicles", DomainConservationRole role = DomainConservationRole.Holding)
        => new(name, "persons", DomainQuantityType.Integer, role, family);

    private static DomainStateSchema Schema() => new(
        "transport-graph",
        1,
        new[]
        {
            new DomainRecordKind(
                "node",
                "network",
                new DomainStateField[]
                {
                    Int("occupancy"),
                },
                Array.Empty<string>()),
            new DomainRecordKind(
                "edge",
                "network",
                new DomainStateField[]
                {
                    Int("crossings", family: null, role: DomainConservationRole.None),
                },
                new[] { "from", "to" }),
            new DomainRecordKind(
                "vehicle",
                "fleet",
                new DomainStateField[]
                {
                    new("progress", "ratio", DomainQuantityType.Real, DomainConservationRole.None, null),
                },
                new[] { "edge" }),
        });

    private static DomainStateRecord Node(string key, int owner, params long[] integers) => new()
    {
        Key = key,
        Kind = "node",
        OwnerIndex = owner,
        Integers = integers,
    };

    private static DomainStateRecord Edge(string key, int owner, long crossings, string from, string to) => new()
    {
        Key = key,
        Kind = "edge",
        OwnerIndex = owner,
        Integers = [crossings],
        References = [from, to],
    };

    private static DomainStateRecord Vehicle(string key, int owner, double progress, string edge) => new()
    {
        Key = key,
        Kind = "vehicle",
        OwnerIndex = owner,
        Reals = [progress],
        References = [edge],
    };

    private static DomainStateFrame Frame(uint frame, params DomainStateRecord[] records)
    {
        var result = new DomainStateFrame { Frame = frame };
        result.Records.AddRange(records.OrderBy(r => r.Key, StringComparer.Ordinal));
        return result;
    }

    private static void AssertRecordsEqual(DomainStateRecord expected, DomainStateRecord actual)
    {
        Assert.Equal(expected.Key, actual.Key);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.OwnerIndex, actual.OwnerIndex);
        Assert.Equal(expected.Integers, actual.Integers);
        Assert.Equal(expected.Reals, actual.Reals);
        Assert.Equal(expected.References, actual.References);
    }

    private static void AssertFramesEqual(DomainStateFrame expected, DomainStateFrame actual)
    {
        Assert.Equal(expected.Frame, actual.Frame);
        Assert.Equal(expected.Records.Count, actual.Records.Count);
        for (var i = 0; i < expected.Records.Count; i++)
        {
            AssertRecordsEqual(expected.Records[i], actual.Records[i]);
        }
    }

    private (MemoryStream Stream, StateWriter Writer) Writer()
    {
        var stream = new MemoryStream();
        return (stream, new StateWriter(stream, Generals));
    }

    [Fact]
    public void RecordRoundTripsThroughStatePersister()
    {
        var record = new DomainStateRecord
        {
            Key = "edge:0->1",
            Kind = "edge",
            OwnerIndex = 2,
            Integers = [17, -4],
            Reals = [0.25, -1.75e300],
            References = ["node:0", "node:1"],
        };

        var (stream, writer) = Writer();
        writer.PersistObject(record, "Record");
        stream.Position = 0;

        var reader = new StateReader(stream, Generals);
        var loaded = new DomainStateRecord();
        reader.PersistObject(loaded, "Record");

        AssertRecordsEqual(record, loaded);
    }

    [Fact]
    public void RecordReadReplacesLongerVectorWithShorterPersistedVector()
    {
        var (stream, writer) = Writer();
        var record = new DomainStateRecord
        {
            Key = "node:0",
            Kind = "node",
            Integers = [1],
        };
        writer.PersistObject(record, "Record");
        stream.Position = 0;

        var reader = new StateReader(stream, Generals);
        var loaded = new DomainStateRecord
        {
            Key = "stale",
            Kind = "stale",
            OwnerIndex = 9,
            Integers = [1, 2, 3, 4, 5],
        };
        reader.PersistObject(loaded, "Record");

        Assert.Equal("node:0", loaded.Key);
        Assert.Equal("node", loaded.Kind);
        Assert.Equal(0, loaded.OwnerIndex);
        Assert.Single(loaded.Integers);
        Assert.Equal(1, loaded.Integers[0]);
        Assert.Empty(loaded.References);
    }

    [Fact]
    public void FrameRoundTripsThroughStatePersister()
    {
        var frame = Frame(
            120,
            Node("node:0", 0, 5),
            Node("node:1", 1, 0),
            Edge("edge:0->1", 0, 3, "node:0", "node:1"),
            Vehicle("vehicle:7", 0, 0.5, "edge:0->1"));

        var (stream, writer) = Writer();
        writer.PersistObject(frame, "Frame");
        stream.Position = 0;

        var reader = new StateReader(stream, Generals);
        var loaded = new DomainStateFrame();
        reader.PersistObject(loaded, "Frame");

        AssertFramesEqual(frame, loaded);
    }

    [Fact]
    public void DeltaRoundTripsThroughStatePersister()
    {
        var delta = new DomainStateDelta { Frame = 121 };
        delta.Upserted.Add(Vehicle("vehicle:8", 1, 0.0, "edge:0->1"));
        delta.Upserted.Add(Node("node:0", 0, 6));
        delta.Removed.Add("vehicle:7");

        var (stream, writer) = Writer();
        writer.PersistObject(delta, "Delta");
        stream.Position = 0;

        var reader = new StateReader(stream, Generals);
        var loaded = new DomainStateDelta();
        reader.PersistObject(loaded, "Delta");

        Assert.Equal(delta.Frame, loaded.Frame);
        Assert.Equal(delta.Upserted.Count, loaded.Upserted.Count);
        for (var i = 0; i < delta.Upserted.Count; i++)
        {
            AssertRecordsEqual(delta.Upserted[i], loaded.Upserted[i]);
        }

        Assert.Equal(delta.Removed, loaded.Removed);
    }

    [Fact]
    public void DiffAndApplyReproduceCurrentFrame()
    {
        var previous = Frame(
            100,
            Node("node:0", 0, 5),
            Node("node:1", 1, 0),
            Edge("edge:0->1", 0, 3, "node:0", "node:1"),
            Vehicle("vehicle:7", 0, 0.5, "edge:0->1"));
        var current = Frame(
            101,
            Node("node:0", 0, 6),
            Node("node:1", 1, 0),
            Edge("edge:0->1", 0, 3, "node:0", "node:1"),
            Vehicle("vehicle:8", 1, 0.0, "edge:0->1"));

        var delta = DomainStateDelta.Diff(101, previous, current);

        var upserted = Assert.Single(delta.Upserted, r => r.Key == "node:0");
        Assert.Equal(6, upserted.Integers[0]);
        Assert.NotNull(delta.Upserted.SingleOrDefault(r => r.Key == "vehicle:8"));
        var removed = Assert.Single(delta.Removed);
        Assert.Equal("vehicle:7", removed);
        Assert.DoesNotContain(delta.Upserted, r => r.Key is "node:1" or "edge:0->1" or "vehicle:7");

        var records = new SortedDictionary<string, DomainStateRecord>(StringComparer.Ordinal);
        foreach (var record in previous.Records)
        {
            records[record.Key] = record;
        }

        delta.Apply(records);

        Assert.Equal(current.Records.Count, records.Count);
        foreach (var expected in current.Records)
        {
            Assert.True(records.TryGetValue(expected.Key, out var actual), expected.Key);
            AssertRecordsEqual(expected, actual);
        }
    }

    [Fact]
    public void ReconstructAtSnapshotFrameReturnsSnapshot()
    {
        var snapshots = new[]
        {
            Frame(0, Node("node:0", 0, 5), Node("node:1", 1, 0)),
            Frame(100, Node("node:0", 0, 7), Node("node:1", 1, 0)),
        };
        var deltas = new[]
        {
            DiffOf(snapshots[0], Frame(50, Node("node:0", 0, 6), Node("node:1", 1, 0)), 50),
        };

        var reconstructed = DomainStateReconstruction.Reconstruct(snapshots, deltas, 100);

        AssertFramesEqual(snapshots[1], reconstructed);
    }

    [Fact]
    public void ReconstructBetweenSnapshotsAppliesDeltas()
    {
        var snapshots = new[]
        {
            Frame(0, Node("node:0", 0, 5), Node("node:1", 1, 0)),
            Frame(100, Node("node:0", 0, 7), Node("node:1", 1, 0)),
        };
        var deltas = new[]
        {
            DiffOf(snapshots[0], Frame(50, Node("node:0", 0, 6), Node("node:1", 1, 0)), 50),
        };

        var reconstructed = DomainStateReconstruction.Reconstruct(snapshots, deltas, 50);

        var node0 = Assert.Single(reconstructed.Records, r => r.Key == "node:0");
        Assert.Equal(6, node0.Integers[0]);
        Assert.Equal(50u, reconstructed.Frame);
    }

    [Fact]
    public void ReconstructBeforeFirstSnapshotThrows()
    {
        var snapshots = new[]
        {
            Frame(100, Node("node:0", 0, 5)),
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => DomainStateReconstruction.Reconstruct(snapshots, Array.Empty<DomainStateDelta>(), 99));
    }

    [Fact]
    public void ResidualsStayZeroAfterHoldingToHoldingMove()
    {
        var schema = Schema();
        var baseline = Frame(0, Node("node:0", 0, 5), Node("node:1", 0, 3), Node("node:2", 1, 4));
        var moved = Frame(10, Node("node:0", 0, 2), Node("node:1", 0, 6), Node("node:2", 1, 4));

        Assert.Empty(DomainConservation.Residuals(schema, baseline, moved));
    }

    [Fact]
    public void ResidualsReportUnbalancedChange()
    {
        var schema = Schema();
        var baseline = Frame(0, Node("node:0", 0, 5), Node("node:1", 0, 3), Node("node:2", 1, 4));
        var lost = Frame(10, Node("node:0", 0, 2), Node("node:1", 0, 3), Node("node:2", 1, 4));

        var residuals = DomainConservation.Residuals(schema, baseline, lost);

        var residual = Assert.Single(residuals);
        Assert.Equal("vehicles", residual.Family);
        Assert.Equal(0, residual.OwnerIndex);
        Assert.Equal(-3, residual.Difference);
    }

    [Fact]
    public void BalancesSumHoldingSinkAndSourcePerOwner()
    {
        var schema = new DomainStateSchema(
            "flow",
            1,
            new[]
            {
                new DomainRecordKind(
                    "holder",
                    "network",
                    new DomainStateField[]
                    {
                        Int("stored", family: null, role: DomainConservationRole.None),
                        new("delivered", "persons", DomainQuantityType.Integer, DomainConservationRole.Sink, "persons"),
                        new("produced", "persons", DomainQuantityType.Integer, DomainConservationRole.Source, "persons"),
                    },
                    Array.Empty<string>()),
            });

        var frame = new DomainStateFrame { Frame = 10 };
        frame.Records.Add(new DomainStateRecord
        {
            Key = "holder:0",
            Kind = "holder",
            OwnerIndex = 1,
            Integers = [4, 10, 12],
        });
        frame.Records.Add(new DomainStateRecord
        {
            Key = "holder:1",
            Kind = "holder",
            OwnerIndex = 2,
            Integers = [7, 3, 0],
        });

        var balances = DomainConservation.Balances(schema, frame);

        Assert.Equal(
        [
            new DomainConservationBalance("persons", 1, 10 - 12),
            new DomainConservationBalance("persons", 2, 3 - 0),
        ], balances);
    }

    private static DomainStateDelta DiffOf(DomainStateFrame previous, DomainStateFrame current, uint frame)
        => DomainStateDelta.Diff(frame, previous, current);
}
