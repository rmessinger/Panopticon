using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Mathematics;
using Panopticon.Recording;
using Panopticon.Simulation;
using Xunit;

namespace Panopticon.Tests.Recording;

public class OperationalStreamTests : StatePersisterTest
{
    private static DomainStateRecord Record(string key, string kind, params long[] integers)
    {
        return new DomainStateRecord
        {
            Key = key,
            Kind = kind,
            OwnerIndex = 0,
            Integers = integers,
            Reals = Array.Empty<double>(),
            References = Array.Empty<string>(),
        };
    }

    private static DomainStateRecord Record(string key, string kind, double real, string reference)
    {
        return new DomainStateRecord
        {
            Key = key,
            Kind = kind,
            OwnerIndex = 1,
            Integers = Array.Empty<long>(),
            Reals = new[] { real },
            References = new[] { reference },
        };
    }

    private static Order BuildOrder()
    {
        var order = new Order(3, OrderType.SpecialPower);
        order.AddIntegerArgument(-5);
        order.AddFloatArgument(1.5f);
        order.AddBooleanArgument(true);
        order.AddObjectIdArgument(new ObjectId(77));
        order.AddPositionArgument(new System.Numerics.Vector3(1f, 2f, 3f));
        order.AddScreenPositionArgument(new Point2D(4, 5));
        order.AddScreenRectangleArgument(new Rectangle(6, 7, 8, 9));
        return order;
    }

    private static List<OperationalRecord> BoundaryRecords()
    {
        var delta = new DomainStateDelta
        {
            Frame = 10,
        };

        delta.Upserted.Add(Record("node:1", "node", 5));
        delta.Upserted.Add(Record("vehicle:9", "vehicle", 0.5, "edge:1->2"));
        delta.Removed.Add("node:3");

        var frame = new DomainStateFrame
        {
            Frame = 10,
        };

        frame.Records.Add(Record("node:1", "node", 5));
        frame.Records.Add(Record("node:2", "node", 7));

        return new List<OperationalRecord>
        {
            new OperationalTick(10, 10, 123_456L),
            new OperationalDomainDelta(10, delta),
            new OperationalMetrics(10, new[] { new OperationalMetricSample("money", "player:0", 3.5) }),
            new OperationalEvents(10, new[] { new DomainEvent("attack", "tank fired") }),
            new OperationalInputs(new[] { new OperationalDispatchedOrder(9, BuildOrder()) }),
            new OperationalHashes(10, new OperationalStateHashes
            {
                WorldHash = 0x1111111111111111UL,
                GlobalsHash = 0x2222222222222222UL,
                Buckets = new Dictionary<string, ulong>
                {
                    ["cell:1,1"] = 0xAAAAAAAAAAAAAAAAUL,
                    ["cell:2,2"] = 0xBBBBBBBBBBBBBBBBUL,
                },
            }),
            new OperationalT1(10, new byte[] { 1, 2, 3, 4 }),
        };
    }

    private static byte[] WriteCompleteStream(bool withFooter)
    {
        using var stream = new MemoryStream();
        using (var writer = new OperationalStreamWriter(stream, "run-1", "rts", 7))
        {
            writer.WriteChunk(10, BoundaryRecords());

            var snapshotFrame = new DomainStateFrame
            {
                Frame = 20,
            };

            snapshotFrame.Records.Add(Record("node:1", "node", 5));
            writer.WriteChunk(20, new OperationalRecord[]
            {
                new OperationalDomainSnapshot(20, snapshotFrame),
                new OperationalRollups(20, new[] { Record("rollup:region", "region", 42) }),
            });

            if (withFooter)
            {
                writer.WriteFooter();
            }
        }

        return stream.ToArray();
    }

    private void AssertRoundTripsFirstBoundary(IReadOnlyList<OperationalRecord> actual)
    {
        var tick = Assert.IsType<OperationalTick>(actual.Single(r => r is OperationalTick));
        Assert.Equal(10u, tick.HostFrame);
        Assert.Equal(10u, tick.LogicFrame);
        Assert.Equal(123_456L, tick.TheaterTimeTicks);

        var delta = Assert.IsType<OperationalDomainDelta>(actual.Single(r => r is OperationalDomainDelta));
        Assert.Equal(10u, delta.BoundaryFrame);
        Assert.Equal(10u, delta.Delta.Frame);
        Assert.Equal(2, delta.Delta.Upserted.Count);
        Assert.Equal("node:1", delta.Delta.Upserted[0].Key);
        Assert.Equal("node", delta.Delta.Upserted[0].Kind);
        Assert.Equal(new long[] { 5 }, delta.Delta.Upserted[0].Integers);
        Assert.Equal("vehicle:9", delta.Delta.Upserted[1].Key);
        Assert.Equal(1, delta.Delta.Upserted[1].OwnerIndex);
        Assert.Equal(new[] { 0.5 }, delta.Delta.Upserted[1].Reals);
        Assert.Equal(new[] { "edge:1->2" }, delta.Delta.Upserted[1].References);
        Assert.Equal(new[] { "node:3" }, delta.Delta.Removed);

        var metrics = Assert.IsType<OperationalMetrics>(actual.Single(r => r is OperationalMetrics));
        var sample = Assert.Single(metrics.Samples);
        Assert.Equal("money", sample.Series);
        Assert.Equal("player:0", sample.Key);
        Assert.Equal(3.5, sample.Value);

        var events = Assert.IsType<OperationalEvents>(actual.Single(r => r is OperationalEvents));
        var domainEvent = Assert.Single(events.Events);
        Assert.Equal("attack", domainEvent.Kind);
        Assert.Equal("tank fired", domainEvent.Detail);

        var inputs = Assert.IsType<OperationalInputs>(actual.Single(r => r is OperationalInputs));
        var dispatched = Assert.Single(inputs.Orders);
        Assert.Equal(9u, dispatched.DispatchFrame);
        Assert.Equal(BuildOrder().ToString(), dispatched.Order.ToString());

        var hashes = Assert.IsType<OperationalHashes>(actual.Single(r => r is OperationalHashes));
        Assert.Equal(0x1111111111111111UL, hashes.Hashes.WorldHash);
        Assert.Equal(0x2222222222222222UL, hashes.Hashes.GlobalsHash);
        Assert.Equal(2, hashes.Hashes.Buckets.Count);
        Assert.Equal(0xAAAAAAAAAAAAAAAAUL, hashes.Hashes.Buckets["cell:1,1"]);
        Assert.Equal(0xBBBBBBBBBBBBBBBBUL, hashes.Hashes.Buckets["cell:2,2"]);

        var t1 = Assert.IsType<OperationalT1>(actual.Single(r => r is OperationalT1));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, t1.Payload);
    }

    [Fact]
    public void RoundTrips_AllRecordTypes_AndResolvesInternedStrings()
    {
        var bytes = WriteCompleteStream(withFooter: true);

        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = OperationalStreamReader.Open(stream);
        Assert.Equal("run-1", reader.RunId);
        Assert.Equal("rts", reader.DomainId);
        Assert.Equal(7, reader.DomainSchemaVersion);

        var index = reader.ReadIndex();
        Assert.Equal(new[] { 10u, 20u }, index.Select(e => e.Frame).ToArray());

        var (frame1, records1) = reader.ReadChunk(index[0]);
        Assert.Equal(10u, frame1);
        AssertRoundTripsFirstBoundary(records1.Where(r => r.Type != OperationalRecordType.Strings).ToList());
        Assert.Contains(records1, r => r is OperationalStrings strings && strings.Entries.Contains("node:1"));

        var (frame2, records2) = reader.ReadChunk(index[1]);
        Assert.Equal(20u, frame2);
        var snapshot = Assert.IsType<OperationalDomainSnapshot>(records2.Single(r => r is OperationalDomainSnapshot));
        Assert.Equal(20u, snapshot.Frame.Frame);
        Assert.Equal("node:1", Assert.Single(snapshot.Frame.Records).Key);
        var rollups = Assert.IsType<OperationalRollups>(records2.Single(r => r is OperationalRollups));
        Assert.Equal(42, Assert.Single(rollups.Records).Integers[0]);
    }

    [Fact]
    public void CrashBeforeFooter_LoadsCompletedChunksViaScan()
    {
        var bytes = WriteCompleteStream(withFooter: false);

        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = OperationalStreamReader.Open(stream);
        Assert.Null(reader.TryReadFooterIndex());

        var index = reader.BuildScanIndex();
        Assert.Equal(new[] { 10u, 20u }, index.Select(e => e.Frame).ToArray());
        Assert.Equal(index, reader.ReadIndex());

        var (frame, records) = reader.ReadChunk(index[0]);
        Assert.Equal(10u, frame);
        Assert.Contains(records, r => r is OperationalTick tick && tick.TheaterTimeTicks == 123_456L);
    }

    [Fact]
    public void TruncatedFinalChunk_IgnoredByScanIndex()
    {
        var bytes = WriteCompleteStream(withFooter: false);
        var truncated = bytes[..^8];

        using var stream = new MemoryStream(truncated, writable: false);
        using var reader = OperationalStreamReader.Open(stream);

        var index = reader.BuildScanIndex();
        Assert.Equal(new[] { 10u }, index.Select(e => e.Frame).ToArray());

        var (frame, records) = reader.ReadChunk(index[0]);
        Assert.Equal(10u, frame);
        Assert.NotEmpty(records);
    }

    [Fact]
    public void UnknownVersion_Rejected()
    {
        var bytes = WriteCompleteStream(withFooter: true);
        bytes[4] = 99;

        using var stream = new MemoryStream(bytes, writable: false);
        Assert.Throws<InvalidDataException>(() => OperationalStreamReader.Open(stream));
    }

    [Fact]
    public void UnknownRecordType_Rejected()
    {
        using var stream = new MemoryStream();
        using (new OperationalStreamWriter(stream, "run-1", "rts", 1))
        {
        }

        // Hand-append one chunk whose payload starts with an unknown record type byte; no footer,
        // so the reader reaches it through the scan path.
        var payload = new byte[] { 0xFF, 0x01, 0x02 };
        using var compressed = new MemoryStream();
        using (var brotli = new System.IO.Compression.BrotliStream(compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            brotli.Write(payload);
        }

        using (var appender = new BinaryWriter(stream, Encoding.Unicode, leaveOpen: true))
        {
            appender.Write(payload.Length);
            appender.Write((int)compressed.Length);
            appender.Write(10u);
            appender.Write(compressed.ToArray());
        }

        using var reader = OperationalStreamReader.Open(new MemoryStream(stream.ToArray(), writable: false));
        var entry = Assert.Single(reader.BuildScanIndex());
        Assert.Throws<InvalidDataException>(() => reader.ReadChunk(entry));
    }

    [Fact]
    public void Hashes_MatchOperationalStateHasherOutputs()
    {
        var objects = new List<(string BucketKey, ulong ObjectHash)>
        {
            ("cell:1,1", OperationalStateHasher.HashBytes(new byte[] { 1, 2, 3 })),
            ("cell:1,1", OperationalStateHasher.HashBytes(new byte[] { 4, 5, 6 })),
            ("cell:2,2", OperationalStateHasher.HashBytes(new byte[] { 7, 8, 9 })),
        };

        var sections = new List<(string SectionName, ulong SectionHash)>
        {
            ("random", OperationalStateHasher.HashBytes(new byte[] { 0x0A })),
            ("host", OperationalStateHasher.HashBytes(new byte[] { 0x0B })),
        };

        var hashes = OperationalStateHasher.Build(objects, sections);

        using var stream = new MemoryStream();
        using (var writer = new OperationalStreamWriter(stream, "run-1", "rts", 1))
        {
            writer.WriteChunk(10, new OperationalRecord[] { new OperationalHashes(10, hashes) });
            writer.WriteFooter();
        }

        using var reader = OperationalStreamReader.Open(new MemoryStream(stream.ToArray(), writable: false));
        var entry = Assert.Single(reader.ReadIndex());
        var (_, records) = reader.ReadChunk(entry);
        var loaded = Assert.IsType<OperationalHashes>(records.Single(r => r is OperationalHashes));
        Assert.Equal(hashes.WorldHash, loaded.Hashes.WorldHash);
        Assert.Equal(hashes.GlobalsHash, loaded.Hashes.GlobalsHash);
        Assert.Equal(hashes.Buckets, loaded.Hashes.Buckets);
    }

    [Fact]
    public void FooterAndScanIndexes_Agree()
    {
        var bytes = WriteCompleteStream(withFooter: true);

        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = OperationalStreamReader.Open(stream);

        var footer = reader.TryReadFooterIndex();
        Assert.NotNull(footer);
        Assert.Equal(reader.BuildScanIndex(), footer);
    }
}
