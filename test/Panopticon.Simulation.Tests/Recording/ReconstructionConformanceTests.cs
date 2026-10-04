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

// WI-A1-24: operational-recording conformance (A1-6 §3). Restoring a checkpoint reproduces the
// recording run's own state at that frame exactly; frames rebuilt after a checkpoint are delivered
// at every recorded boundary and keep domain invariants, without being compared to the original
// run; a cached derived checkpoint serves later scrubs. Full-mode JSON must stay byte-shaped like
// pre-A1-6 output (no operational manifest fields).

[Collection("SerialGameTests")]
public sealed class ReconstructionConformanceTests : MockedGameTest, IDisposable
{
    private const uint Seed = 41u;

    private static readonly RecordingCadence TestCadence = new(frame => frame % 5 == 0, frame => frame % 15 == 0);

    private readonly List<Game> _bootedGames = new();

    public new void Dispose()
    {
        foreach (var game in _bootedGames)
        {
            game.Dispose();
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void FullModeJson_CarriesNoOperationalManifestFields()
    {
        var game = BootGame();
        _bootedGames.Add(game);
        var host = new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));
        using var recording = RunRecording.Attach(host, game, seed: Seed, scenarioId: "a1-24-conformance");
        host.Run(15);

        var fullJson = recording.Complete().ToJson();
        Assert.DoesNotContain("recordingMode", fullJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("operationalStreamPath", fullJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("checkpoints", fullJson, StringComparison.OrdinalIgnoreCase);
    }

    [GameFact(SageGame.CncGenerals)]
    public void RtsCheckpointRestore_MatchesRecordingRunAndRebuildsTheWindow()
    {
        var (bundle, liveSummaries) = RecordRtsBundle(injectOrder: true, frames: 45);
        var reconstructor = RunReconstructor.Open(bundle, CreateRtsBootstrap());

        // Each bundle checkpoint restores to exactly the state the recording run sampled there.
        foreach (var checkpoint in reconstructor.Manifest.Checkpoints!.Where(c => c.Frame > 0))
        {
            var restoreSink = new BufferedReconstructionSink();
            var restoredHost = reconstructor.Reconstruct(checkpoint.Frame, checkpoint.Frame, restoreSink);
            Assert.Equal(checkpoint.Frame, restoreSink.Frames[0]);
            Assert.Equal(liveSummaries[checkpoint.Frame], PersistedSummary(restoreSink.Summaries[0], (Game)restoredHost.Game));
        }

        // A seed-derived window rebuilds every recorded boundary in range.
        var from = 5u * (Seed % 4u);
        var to = 45u - 5u * (Seed % 3u);
        var sink = new BufferedReconstructionSink();
        reconstructor.Reconstruct(from, to, sink);

        var expectedFrames = Enumerable.Range((int)(from / 5), (int)(to / 5 - from / 5) + 1).Select(i => (uint)(i * 5));
        Assert.Equal(expectedFrames, sink.Frames);
        Assert.All(sink.Summaries, summary => Assert.NotEmpty(summary.Objects));
    }

    [GameFact(SageGame.CncGenerals)]
    public void TransportRebuiltFrames_ConserveVehiclesAndCheckpointsRestoreExactly()
    {
        var game = BootGame();
        _bootedGames.Add(game);
        var domain = TransportGraphDomainModel.CreateDefault(game.GameEngine);
        domain.Cadence = TestCadence;
        var host = new SimulationHost(game.GameEngine, domain);
        var bundle = Path.Combine(Path.GetTempPath(), "a1-24-transport-" + Guid.NewGuid().ToString("N"));
        var recording = RunRecording.Attach(
            host, game, seed: Seed, scenarioId: "a1-24-transport",
            options: new RecordingOptions { Mode = RecordingMode.Operational, BundleDirectory = bundle, DefaultCadence = TestCadence });
        var liveStates = new SortedDictionary<uint, byte[]>();
        recording.Sampler.FrameSampled += (summary, _) =>
        {
            liveStates[summary.Frame] = PersistedFrame(domain.CaptureState(summary.Frame), game);
        };
        host.Run(600, _ => domain.AllRoutesComplete);
        Assert.True(domain.AllRoutesComplete);
        var finalFrame = host.LogicFrame.Value;
        recording.Complete();
        recording.Dispose();

        var reconstructor = RunReconstructor.Open(bundle, CreateTransportBootstrap());

        foreach (var checkpoint in reconstructor.Manifest.Checkpoints!.Where(c => c.Frame > 0))
        {
            var restoreSink = new BufferedReconstructionSink();
            var restoredHost = reconstructor.Reconstruct(checkpoint.Frame, checkpoint.Frame, restoreSink);
            Assert.Equal(liveStates[checkpoint.Frame], PersistedFrame(restoreSink.DomainStates[0]!, (Game)restoredHost.Game));
        }

        // Rebuild the whole run: every boundary is delivered and vehicle conservation holds on
        // rebuilt frames, whether or not they match the recording run.
        var sink = new BufferedReconstructionSink();
        reconstructor.Reconstruct(0, finalFrame, sink);

        var expectedFrames = Enumerable.Range(1, (int)(finalFrame / 5)).Select(i => (uint)(i * 5));
        Assert.Equal(expectedFrames, sink.Frames);
        var baseline = domain.CaptureState(0);
        foreach (var domainState in sink.DomainStates)
        {
            Assert.NotNull(domainState);
            Assert.Empty(DomainConservation.Residuals(domain.StateSchema, baseline, domainState!));
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void CachedBoundary_ServesLaterScrubWithTheFirstReconstructionsState()
    {
        var (bundle, _) = RecordRtsBundle(injectOrder: false, frames: 45);

        var cache = new ReconstructionCache(4_000_000);
        var first = RunReconstructor.Open(bundle, CreateRtsBootstrap(), cache: cache);
        var firstSink = new BufferedReconstructionSink();
        var firstHost = first.Reconstruct(0, 45, firstSink);

        // Frame 20 is not a bundle checkpoint; the second scrub restores the cached derived
        // checkpoint there and delivers the first reconstruction's state without re-simulating.
        var second = RunReconstructor.Open(bundle, CreateRtsBootstrap(), cache: cache);
        var secondSink = new BufferedReconstructionSink();
        var secondHost = second.Reconstruct(20, 20, secondSink);

        Assert.Equal(new uint[] { 20 }, secondSink.Frames);
        var firstIndex = firstSink.Frames.IndexOf(20);
        Assert.Equal(
            PersistedSummary(firstSink.Summaries[firstIndex], (Game)firstHost.Game),
            PersistedSummary(secondSink.Summaries[0], (Game)secondHost.Game));
    }

    private (string Bundle, SortedDictionary<uint, byte[]> LiveSummaries) RecordRtsBundle(bool injectOrder, uint frames)
    {
        var game = BootGame();
        _bootedGames.Add(game);
        var host = new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));
        var bundle = Path.Combine(Path.GetTempPath(), "a1-24-bundle-" + Guid.NewGuid().ToString("N"));

        var recording = RunRecording.Attach(
            host, game, seed: Seed, scenarioId: "a1-24-conformance",
            options: new RecordingOptions { Mode = RecordingMode.Operational, BundleDirectory = bundle, DefaultCadence = TestCadence });
        var liveSummaries = new SortedDictionary<uint, byte[]>();
        recording.Sampler.FrameSampled += (summary, _) =>
        {
            liveSummaries[summary.Frame] = PersistedSummary(summary, game);
        };
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
        return (bundle, liveSummaries);
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

    private InstalledBootstrap CreateTransportBootstrap()
    {
        return new InstalledBootstrap(() =>
        {
            var game = BootGame();
            _bootedGames.Add(game);
            var domain = TransportGraphDomainModel.CreateDefault(game.GameEngine);
            domain.Cadence = TestCadence;
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
