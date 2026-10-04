using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Recording;
using Panopticon.Simulation;
using Panopticon.Simulation.Transport;
using Panopticon.Tests.Data;
using Xunit;

namespace Panopticon.Tests.Recording;

// WI-A1-24: operational-recording conformance. Same-seed Full and Operational runs of the RTS
// and transport-graph domains must produce identical state, reconstruction must verify every
// recorded boundary hash while delivering state that equals the Full run, and the derived
// checkpoint cache must not change reconstruction output. Full-mode JSON must stay byte-shaped
// like pre-A1-6 output (no operational manifest fields).

[Collection("SerialGameTests")]
public sealed class ReconstructionConformanceTests : MockedGameTest, IDisposable
{
    private const uint Seed = 41u;

    private readonly List<Game> _bootedGames = new();

    public new void Dispose()
    {
        foreach (var game in _bootedGames)
        {
            game.Dispose();
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void RtsOperationalBundle_MatchesFullRunAtEveryBoundary()
    {
        // Full run (the pre-A1-6 shape): no recording options, summary sampled every frame.
        var gameA = BootGame();
        _bootedGames.Add(gameA);
        var hostA = new SimulationHost(gameA.GameEngine, new RtsSimulationDomainModel(gameA.GameEngine));
        var recordingA = RunRecording.Attach(hostA, gameA, seed: Seed, scenarioId: "a1-24-conformance");
        var fullSummaries = new SortedDictionary<uint, byte[]>();
        recordingA.Sampler.FrameSampled += (summary, _) =>
        {
            fullSummaries[summary.Frame] = PersistedSummary(summary, gameA);
        };
        hostA.Run(45);
        var outputA = recordingA.Complete();
        recordingA.Dispose();

        // Full-mode JSON carries none of the operational manifest fields.
        var fullJson = outputA.ToJson();
        Assert.DoesNotContain("recordingMode", fullJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("operationalStreamPath", fullJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("checkpoints", fullJson, StringComparison.OrdinalIgnoreCase);

        // Operational run of the same scenario and seed.
        var bundle = RecordRtsBundle(injectOrder: true, frames: 45);

        // "Random" (seed-derived, deterministic) window: reconstruct [5, 35] from checkpoint 0.
        var from = 5u * (Seed % 4u);
        var to = 45u - 5u * (Seed % 3u);
        var reconstructor = RunReconstructor.Open(bundle, CreateRtsBootstrap());
        var sink = new BufferedReconstructionSink();
        var reconstructedHost = reconstructor.Reconstruct(from, to, sink);
        var reconstructedGame = (Game)reconstructedHost.Game;

        var expectedFrames = Enumerable.Range((int)(from / 5), (int)(to / 5 - from / 5) + 1).Select(i => (uint)(i * 5));
        Assert.Equal(expectedFrames, sink.Frames);
        for (var i = 0; i < sink.Frames.Count; i++)
        {
            Assert.Equal(fullSummaries[sink.Frames[i]], PersistedSummary(sink.Summaries[i], reconstructedGame));
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void TransportOperationalBundle_MatchesFullRunDomainState()
    {
        // Full run: collect the projected domain state at every sampled frame.
        var gameA = BootGame();
        _bootedGames.Add(gameA);
        var domainA = TransportGraphDomainModel.CreateDefault(gameA.GameEngine);
        var hostA = new SimulationHost(gameA.GameEngine, domainA);
        var recordingA = RunRecording.Attach(hostA, gameA, seed: Seed, scenarioId: "a1-24-transport");
        var fullStates = new SortedDictionary<uint, byte[]>();
        recordingA.Sampler.FrameSampled += (summary, _) =>
        {
            using var stream = new MemoryStream();
            using (var writer = new StateWriter(stream, gameA))
            {
                writer.PersistObject(domainA.CaptureState(summary.Frame), "DomainStateFrame");
            }
            fullStates[summary.Frame] = stream.ToArray();
        };
        hostA.Run(600, _ => domainA.AllRoutesComplete);
        Assert.True(domainA.AllRoutesComplete);
        var finalFrame = hostA.LogicFrame.Value;
        recordingA.Complete();
        recordingA.Dispose();

        // Operational run: identical boot and cadence, output to a bundle.
        var gameB = BootGame();
        _bootedGames.Add(gameB);
        var cadence = new RecordingCadence(frame => frame % 5 == 0, frame => frame % 15 == 0);
        var domainB = TransportGraphDomainModel.CreateDefault(gameB.GameEngine);
        domainB.Cadence = cadence;
        var hostB = new SimulationHost(gameB.GameEngine, domainB);
        var bundle = Path.Combine(Path.GetTempPath(), "a1-24-transport-" + Guid.NewGuid().ToString("N"));
        var recordingB = RunRecording.Attach(
            hostB, gameB, seed: Seed, scenarioId: "a1-24-transport",
            options: new RecordingOptions { Mode = RecordingMode.Operational, BundleDirectory = bundle, DefaultCadence = cadence });
        hostB.Run(600, _ => domainB.AllRoutesComplete);
        recordingB.Complete();
        recordingB.Dispose();
        Assert.Equal(finalFrame, hostB.LogicFrame.Value);

        // Reconstruct the whole run; every boundary must match the Full run's captured state.
        var reconstructor = RunReconstructor.Open(bundle, CreateTransportBootstrap(cadence));
        var sink = new BufferedReconstructionSink();
        var reconstructedHost = reconstructor.Reconstruct(0, finalFrame, sink);
        var reconstructedGame = (Game)reconstructedHost.Game;

        var expectedFrames = Enumerable.Range(1, (int)(finalFrame / 5)).Select(i => (uint)(i * 5));
        Assert.Equal(expectedFrames, sink.Frames);
        var baseline = domainA.CaptureState(0);
        for (var i = 0; i < sink.Frames.Count; i++)
        {
            var frame = sink.Frames[i];
            var domainState = sink.DomainStates[i];
            Assert.NotNull(domainState);
            Assert.Equal(fullStates[frame], PersistedFrame(domainState!, reconstructedGame));

            // Vehicle conservation holds across reconstruction, not just the live run.
            Assert.Empty(DomainConservation.Residuals(domainB.StateSchema, baseline, domainState!));
        }

        // The operational run reaches the same terminal edge throughput as the Full run; the
        // last delivered boundary may precede the final crossing by up to one cadence interval.
        var lastEdgeRecords = sink.DomainStates[^1]!.Records.Where(r => r.Kind == "edge").ToList();
        Assert.NotEmpty(lastEdgeRecords);
        foreach (var record in lastEdgeRecords)
        {
            Assert.Equal(domainA.EdgeThroughput(record.Key), domainB.EdgeThroughput(record.Key));
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void SecondReconstruction_WithCache_ProducesIdenticalOutput()
    {
        var bundle = RecordRtsBundle(injectOrder: false, frames: 45);

        var cache = new ReconstructionCache(4_000_000);
        var first = RunReconstructor.Open(bundle, CreateRtsBootstrap(), cache: cache);
        var firstSink = new BufferedReconstructionSink();
        var firstHost = first.Reconstruct(0, 45, firstSink);

        var second = RunReconstructor.Open(bundle, CreateRtsBootstrap(), cache: cache);
        var secondSink = new BufferedReconstructionSink();
        var secondHost = second.Reconstruct(5, 45, secondSink);

        // The cached boundary-5 checkpoint serves the second pass; delivered state is identical.
        Assert.Equal(firstSink.Frames, secondSink.Frames);
        for (var i = 0; i < firstSink.Frames.Count; i++)
        {
            Assert.Equal(
                PersistedSummary(firstSink.Summaries[i], (Game)firstHost.Game),
                PersistedSummary(secondSink.Summaries[i], (Game)secondHost.Game));
        }
    }

    private string RecordRtsBundle(bool injectOrder, uint frames)
    {
        var game = BootGame();
        _bootedGames.Add(game);
        var host = new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));
        var bundle = Path.Combine(Path.GetTempPath(), "a1-24-bundle-" + Guid.NewGuid().ToString("N"));
        var cadence = new RecordingCadence(frame => frame % 5 == 0, frame => frame % 15 == 0);

        var recording = RunRecording.Attach(
            host, game, seed: Seed, scenarioId: "a1-24-conformance",
            options: new RecordingOptions { Mode = RecordingMode.Operational, BundleDirectory = bundle, DefaultCadence = cadence });
        if (injectOrder)
        {
            host.Run(10);
            host.InjectOrders(12, [Order.CreateMoveOrder(1, new Vector3(4, 5, 6))]);
            host.Run((int)frames - 10);
        }
        else
        {
            host.Run((int)frames);
        }

        recording.Complete();
        recording.Dispose();
        return bundle;
    }

    private InstalledBootstrap CreateRtsBootstrap()
    {
        return new InstalledBootstrap(() =>
        {
            var game = BootGame();
            _bootedGames.Add(game);
            return new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));
        });
    }

    private InstalledBootstrap CreateTransportBootstrap(RecordingCadence cadence)
    {
        return new InstalledBootstrap(() =>
        {
            var game = BootGame();
            _bootedGames.Add(game);
            var domain = TransportGraphDomainModel.CreateDefault(game.GameEngine);
            domain.Cadence = cadence;
            return new SimulationHost(game.GameEngine, domain);
        });
    }

    private static byte[] PersistedSummary(WorldStateSummary summary, Game game)
    {
        using var stream = new MemoryStream();
        using (var writer = new StateWriter(stream, game))
        {
            writer.PersistObject(summary, "Summary");
        }
        return stream.ToArray();
    }

    private static byte[] PersistedFrame(DomainStateFrame frame, Game game)
    {
        using var stream = new MemoryStream();
        using (var writer = new StateWriter(stream, game))
        {
            writer.PersistObject(frame, "DomainStateFrame");
        }
        return stream.ToArray();
    }

    private static Game BootGame()
    {
        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        Platform.Start();
        var game = new Game(installation, null, new Configuration
        {
            NoAudio = true,
            SkipStartingBuildings = true,
        }, null);
        var playerSettings = new PlayerSetting[]
        {
            new(0, "FactionAmerica", new ColorRgb(255, 0, 0), 0, PlayerOwner.Player, "USA"),
            new(1, "FactionChina", new ColorRgb(0, 255, 0), 0, PlayerOwner.EasyAi, "China"),
        };
        game.StartSkirmishOrMultiPlayerGame(
            "maps/alpine assault/alpine assault.map",
            new EchoConnection(),
            playerSettings,
            seed: 12345,
            isMultiPlayer: false);
        game.StartRun();
        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        foreach (var player in game.Scene3D.Players)
        {
            player.DisableAI();
        }

        game.EnableFixedLogicTimestep();
        return game;
    }

    private sealed class InstalledBootstrap(Func<SimulationHost> factory) : IReconstructionBootstrap
    {
        public SimulationHost Boot() => factory();
    }
}
