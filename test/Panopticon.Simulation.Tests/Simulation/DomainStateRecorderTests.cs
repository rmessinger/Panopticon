using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Recording;
using Panopticon.Simulation;
using Panopticon.Simulation.Transport;
using Xunit;

namespace Panopticon.Tests.Simulation;

// WI-A4-44: domain state recording through the run recording. An in-test domain wraps
// TransportGraphDomainModel with a trivial projection; the artifact must round-trip the
// in-memory frames, reconstruction must match live capture at every sampled frame, and an
// RTS-style domain without the projection must produce run-output JSON without any of the
// domain-state properties.

public class DomainStateRecorderTests : MockedGameTest
{
    private sealed class ProjectedTransportDomain : ISimulationDomainModel, IDomainStateProjection
    {
        private readonly TransportGraphDomainModel _inner;

        public ProjectedTransportDomain(TransportGraphDomainModel inner)
        {
            _inner = inner;
        }

        public DomainStateSchema StateSchema { get; } = new(
            "transport-graph",
            1,
            new DomainRecordKind[]
            {
                new(
                    "node",
                    "network",
                    new DomainStateField[]
                    {
                        new("occupancy", "vehicles", DomainQuantityType.Integer, DomainConservationRole.Holding, "vehicles"),
                    },
                    []),
                new(
                    "edge",
                    "network",
                    new DomainStateField[]
                    {
                        new("throughput", "crossings", DomainQuantityType.Integer, DomainConservationRole.None, null),
                    },
                    new[] { "from", "to" }),
                new(
                    "vehicle",
                    "fleet",
                    new DomainStateField[]
                    {
                        new("progress", "ratio", DomainQuantityType.Real, DomainConservationRole.None, null),
                    },
                    new[] { "location" }),
            });

        public DomainStateFrame CaptureState(uint frame)
        {
            var result = new DomainStateFrame { Frame = frame };

            foreach (var node in _inner.Topology.Nodes)
            {
                result.Records.Add(new DomainStateRecord
                {
                    Key = node.Key,
                    Kind = "node",
                    Integers = [_inner.NodeOccupancy(node.Index)],
                });
            }

            foreach (var edge in _inner.Topology.Edges)
            {
                result.Records.Add(new DomainStateRecord
                {
                    Key = edge.Key,
                    Kind = "edge",
                    Integers = [_inner.EdgeThroughput(edge.Key)],
                    References = [edge.From.Key, edge.To.Key],
                });
            }

            foreach (var entity in _inner.CaptureEntities())
            {
                if (entity.KindName != "TransportVehicle")
                {
                    continue;
                }

                var topologyKey = _inner.TopologyKey(in entity);
                result.Records.Add(new DomainStateRecord
                {
                    Key = $"vehicle:{entity.EntityId}",
                    Kind = "vehicle",
                    Reals = [entity.ScalarA],
                    References = [topologyKey],
                });
            }

            result.Records.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return result;
        }

        public string DomainId => _inner.DomainId;

        public DomainFidelityPolicy Fidelity => _inner.Fidelity;

        public void Initialize(SimulationHost host) => _inner.Initialize(host);

        public void RunStarting() => _inner.RunStarting();

        public void RunComplete(in SimulationHostRunResult result) => _inner.RunComplete(in result);

        public IReadOnlyList<DomainEntitySummary> CaptureEntities() => _inner.CaptureEntities();

        public IReadOnlyList<uint> CapturePlayerMoneyVector() => _inner.CapturePlayerMoneyVector();

        public string TopologyKey(in DomainEntitySummary entity) => _inner.TopologyKey(in entity);

        public DomainTopologyDescription DescribeTopology() => _inner.DescribeTopology();

        public DomainMetricsSnapshot SampleMetrics() => _inner.SampleMetrics();

        public uint ResolveInteractionAuthority(in DomainInteraction interaction) => _inner.ResolveInteractionAuthority(in interaction);
    }

    [Fact]
    public void ProjectionDomain_ArtifactEqualsInMemoryFrames_AndReconstructionMatchesLiveCapture()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var domain = new ProjectedTransportDomain(TransportGraphDomainModel.CreateDefault(game.GameEngine));
        var host = new SimulationHost(game.GameEngine, domain);

        var outputDir = Path.Combine(Path.GetTempPath(), $"panopticon_domainstate_{Guid.NewGuid():N}");
        var artifactPath = Path.Combine(outputDir, "domain-state.bin");

        // Live capture of every sampled frame, taken through the same projection the recorder
        // uses, so reconstruction has an independent reference.
        var liveFrames = new Dictionary<uint, DomainStateFrame>();
        RunOutput output;
        RunRecording recording = null!;
        using (recording = RunRecording.Attach(
            host, game, seed: 11u, scenarioId: "a4-domain-state",
            snapshotInterval: 10, domainStateArtifactPath: artifactPath))
        {
            recording.Sampler.FrameSampled += (summary, _) => liveFrames[summary.Frame] = domain.CaptureState(summary.Frame);
            host.Run(45);
            output = recording.Complete();
        } // dispose before touching the artifact file: Windows requires it

        try
        {
            // Run output exposes the projection: schema version, artifact path, snapshot index.
            Assert.Equal(1, output.DomainStateSchemaVersion);
            Assert.Equal(artifactPath, output.DomainStateArtifactPath);
            Assert.Equal(liveFrames.Keys.Where(f => f % 10 == 0).OrderBy(f => f), output.DomainStateSnapshotFrames);

            // 45 stepped frames, one sampled frame each; snapshots at the interval.
            var (schema, snapshots, deltas) = DomainStateArtifact.Load(artifactPath, game);
            Assert.Equal(domain.StateSchema.DomainId, schema.DomainId);
            Assert.Equal(domain.StateSchema.Version, schema.Version);
            Assert.Equal(
                domain.StateSchema.Kinds.Select(k => (k.Name, k.Layer, k.ReferenceNames.Count)),
                schema.Kinds.Select(k => (k.Name, k.Layer, k.ReferenceNames.Count)));

            Assert.Equal(4, snapshots.Count); // frames 10, 20, 30, 40
            Assert.Equal(45, deltas.Count);
            Assert.Equal(
                recording.DomainState.Snapshots.Select(ProjectionOf),
                snapshots.Select(ProjectionOf));
            Assert.Equal(
                recording.DomainState.Deltas.Select(ProjectionOf),
                deltas.Select(ProjectionOf));

            // Reconstruction equals the live capture at every sampled frame at or after the
            // first snapshot; earlier frames legitimately have no snapshot baseline.
            foreach (var (frame, live) in liveFrames.OrderBy(kv => kv.Key))
            {
                if (frame < 10)
                {
                    continue;
                }

                var reconstructed = recording.DomainState.ReconstructStateAt(frame);
                Assert.Equal(frame, reconstructed.Frame);
                Assert.Equal(ProjectionOf(live.Records), ProjectionOf(reconstructed.Records));
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => recording.DomainState.ReconstructStateAt(5));
        }
        finally
        {
            Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public void ProjectionDomain_ArtifactRejectsUnknownRecordTagAndFormatVersion()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var domain = new ProjectedTransportDomain(TransportGraphDomainModel.CreateDefault(game.GameEngine));
        var host = new SimulationHost(game.GameEngine, domain);

        var path = Path.Combine(Path.GetTempPath(), $"panopticon_domainstate_{Guid.NewGuid():N}.bin");
        try
        {
            using (var recording = RunRecording.Attach(host, game, seed: 12u, domainStateArtifactPath: path))
            {
                host.Run(1);
            }

            // Unknown record tag: a bogus tag after the last complete record.
            var withBogusTag = Path.ChangeExtension(path, ".tag.bin");
            File.Copy(path, withBogusTag);
            File.AppendAllText(withBogusTag, "\xFF");
            var tagEx = Assert.Throws<InvalidDataException>(() => DomainStateArtifact.Load(withBogusTag, game));
            Assert.Contains("record type", tagEx.Message);
            File.Delete(withBogusTag);

            // Unknown format version: the header record is tag byte + version uint, so the
            // version occupies bytes 1..4.
            var withBogusVersion = Path.ChangeExtension(path, ".version.bin");
            File.Copy(path, withBogusVersion);
            var bytes = File.ReadAllBytes(withBogusVersion);
            bytes[1] = 0x63; // 99
            bytes[2] = 0x00;
            bytes[3] = 0x00;
            bytes[4] = 0x00;
            File.WriteAllBytes(withBogusVersion, bytes);
            var versionEx = Assert.Throws<InvalidDataException>(() => DomainStateArtifact.Load(withBogusVersion, game));
            Assert.Contains("format version", versionEx.Message);
            File.Delete(withBogusVersion);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RtsDomainWithoutProjection_JsonHasNoDomainStateProperties()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var host = new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));

        using var recording = RunRecording.Attach(host, game, seed: 13u, scenarioId: "a4-rts");
        host.Run(5);
        var output = recording.Complete();

        Assert.Null(recording.DomainState);
        Assert.Null(output.DomainStateSchemaVersion);
        Assert.Null(output.DomainStateArtifactPath);
        Assert.Null(output.DomainStateSnapshotFrames);

        var json = output.ToJson();
        Assert.DoesNotContain("domainState", json, StringComparison.OrdinalIgnoreCase);
    }

    // Tuples would nest arrays, which compare by reference; project to strings instead so
    // equality is by value.
    private static List<string> ProjectionOf(DomainStateFrame frame) => ProjectionOf(frame.Records);

    private static List<string> ProjectionOf(IReadOnlyList<DomainStateRecord> records) =>
    [
        .. records.Select(r =>
            $"{r.Key}|{r.Kind}|{r.OwnerIndex}|{string.Join(",", r.Integers)}|{string.Join(",", r.Reals)}|{string.Join(",", r.References)}"),
    ];

    private static string ProjectionOf(DomainStateDelta delta) =>
        $"{delta.Frame}|+[{string.Join(";", ProjectionOf(delta.Upserted))}]|-[{string.Join(";", delta.Removed)}]";
}
