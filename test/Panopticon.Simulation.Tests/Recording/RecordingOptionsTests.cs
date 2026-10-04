using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Recording;
using Panopticon.Simulation;
using Xunit;

namespace Panopticon.Tests.Recording;

// WI-A1-19: recording modes and cadence. Omitting options must follow the existing Full path
// unchanged; Operational mode samples only at cadence boundaries and has no live T2 capture; the
// domain cadence wins over the caller default, which wins over the 1800/648000 built-in default.

public class RecordingOptionsTests : MockedGameTest
{
    [Fact]
    public void DefaultCadence_Is1800OperationalAnd648000Checkpoint()
    {
        var cadence = RecordingOptions.CreateDefaultCadence();

        Assert.True(cadence.IsOperationalBoundary(0));
        Assert.True(cadence.IsOperationalBoundary(RecordingOptions.DefaultOperationalInterval));
        Assert.False(cadence.IsOperationalBoundary(RecordingOptions.DefaultOperationalInterval - 1));
        Assert.True(cadence.IsOperationalBoundary(2 * RecordingOptions.DefaultOperationalInterval));

        Assert.True(cadence.IsCheckpointBoundary(0));
        Assert.True(cadence.IsCheckpointBoundary(RecordingOptions.DefaultCheckpointInterval));
        Assert.False(cadence.IsCheckpointBoundary(RecordingOptions.DefaultOperationalInterval));
        Assert.False(cadence.IsCheckpointBoundary(108_000));
        Assert.Equal(648_000u, RecordingOptions.DefaultCheckpointInterval);
    }

    [Fact]
    public void Attach_WithFullOptions_ProducesByteIdenticalJsonToOmittedOptions()
    {
        // The run id is generated per attach, so it is normalized out before comparing.
        var withoutOptions = NormalizeRunId(RunFullRecording(recordingOptions: null));
        var withFullOptions = NormalizeRunId(RunFullRecording(recordingOptions: new RecordingOptions { Mode = RecordingMode.Full }));

        Assert.Equal(withoutOptions, withFullOptions);
    }

    [Fact]
    public void Attach_OperationalMode_SamplesOnlyAtBoundariesAndSkipsT2()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var host = new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));

        var cadence = new RecordingCadence(frame => frame % 5 == 0, frame => frame % 15 == 0);
        var options = new RecordingOptions { Mode = RecordingMode.Operational, DefaultCadence = cadence };
        var sampledFrames = new List<uint>();

        using (var recording = RunRecording.Attach(host, game, seed: 21u, scenarioId: "a1-19-operational", options: options))
        {
            recording.Sampler.FrameSampled += (summary, _) => sampledFrames.Add(summary.Frame);
            host.Run(21);
            Assert.Null(recording.T2);
            Assert.Same(cadence, recording.EffectiveCadence);
        }

        Assert.Equal(new uint[] { 5, 10, 15, 20 }, sampledFrames);
    }

    [Fact]
    public void Attach_DomainCadence_WinsOverCallerDefault()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var domain = new ReconstructableRtsDomain(
            new RtsSimulationDomainModel(game.GameEngine),
            new RecordingCadence(frame => frame % 3 == 0, frame => frame % 9 == 0));
        var host = new SimulationHost(game.GameEngine, domain);

        var callerCadence = new RecordingCadence(_ => true, _ => false);
        var options = new RecordingOptions { Mode = RecordingMode.Operational, DefaultCadence = callerCadence };
        var sampledFrames = new List<uint>();

        using (var recording = RunRecording.Attach(host, game, seed: 22u, scenarioId: "a1-19-domain-cadence", options: options))
        {
            recording.Sampler.FrameSampled += (summary, _) => sampledFrames.Add(summary.Frame);
            host.Run(10);
            Assert.Same(domain.Cadence, recording.EffectiveCadence);
            Assert.False(recording.EffectiveCadence.IsOperationalBoundary(4));
        }

        Assert.Equal(new uint[] { 3, 6, 9 }, sampledFrames);
    }

    private static string NormalizeRunId(string json)
    {
        // The run id and wall-clock durations are generated per attach, so they are normalized
        // out before comparing byte identity of everything else.
        var normalized = System.Text.RegularExpressions.Regex.Replace(json, "\"runId\": \"[0-9a-f]+\"", "\"runId\": \"normalized\"");
        return System.Text.RegularExpressions.Regex.Replace(normalized, "\"[A-Za-z]*Milliseconds\": [0-9.eE+-]+", "\"durationMilliseconds\": 0");
    }

    private static string RunFullRecording(RecordingOptions? recordingOptions)
    {
        var game = new TestGame(SageGame.CncGenerals);
        var host = new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));

        using (var recording = RunRecording.Attach(host, game, seed: 23u, scenarioId: "a1-19-full", snapshotInterval: 10, options: recordingOptions))
        {
            host.Run(25);
            return recording.Complete().ToJson();
        }
    }

    private sealed class ReconstructableRtsDomain : ISimulationDomainModel, IReconstructableDomain
    {
        private readonly ISimulationDomainModel _inner;

        public ReconstructableRtsDomain(ISimulationDomainModel inner, RecordingCadence cadence)
        {
            _inner = inner;
            Cadence = cadence;
        }

        public RecordingCadence Cadence { get; }

        public IReadOnlySet<string> GranularLayers { get; } = new HashSet<string>();

        public IReadOnlyList<DomainStateRecord> CaptureRollups(uint frame) => Array.Empty<DomainStateRecord>();

        public IReadOnlyList<DomainEvent> DrainOperationalEvents() => Array.Empty<DomainEvent>();

        public void PersistRuntime(StatePersister persister)
        {
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
}
