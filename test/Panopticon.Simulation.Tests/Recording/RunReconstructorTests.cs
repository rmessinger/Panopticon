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
using Panopticon.Tests.Data;
using Xunit;

namespace Panopticon.Tests.Recording;

// WI-A1-22: reconstruction from an operational bundle. Identity refusal and structural
// failures run against a synthetic bundle without an install; checkpoint restore, input
// replay, boundary verification, divergence, and ExportSlice run against the installed
// skirmish boot (GameFact), because checkpoints can only be captured from a booted game.

[Collection("SerialGameTests")]
public sealed class RunReconstructorTests : MockedGameTest, IDisposable
{
    private readonly List<Game> _bootedGames = new();

    public new void Dispose()
    {
        foreach (var game in _bootedGames)
        {
            game.Dispose();
        }
    }

    [Fact]
    public void Open_RefusesIdentityMismatch_WithoutAllowFlag()
    {
        var bundle = WriteSyntheticBundle(manifestSeed: 99, identitySeed: 7);
        var bootstrap = new UnreachableBootstrap();

        var exception = Assert.Throws<InvalidOperationException>(
            () => RunReconstructor.Open(bundle, bootstrap));
        Assert.Contains("identity mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Seed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_WithAllowIdentityMismatch_OpensButReconstructNeedsACheckpointFile()
    {
        // The synthetic manifest lists a frame-15 checkpoint whose file does not exist; the
        // identity pass succeeds with the flag, and reconstruction fails on the missing file.
        var bundle = WriteSyntheticBundle(manifestSeed: 99, identitySeed: 7);
        var bootstrap = new UnreachableBootstrap();
        var reconstructor = RunReconstructor.Open(bundle, bootstrap, new ReconstructionOptions { AllowIdentityMismatch = true });

        Assert.Throws<InvalidOperationException>(() => reconstructor.Reconstruct(20, 30, new BufferedReconstructionSink()));
    }

    [GameFact(SageGame.CncGenerals)]
    public void Reconstruct_DeliversVerifiedBoundariesAndMatchesUninterruptedRun()
    {
        var (bundle, game, _) = RecordBundle(injectOrder: true, frames: 45);

        var reconstructor = RunReconstructor.Open(bundle, CreateInstalledBootstrap());
        var sink = new BufferedReconstructionSink();
        var reconstructedHost = reconstructor.Reconstruct(0, 45, sink);

        Assert.Equal(Enumerable.Range(1, 9).Select(i => (uint)(i * 5)), sink.Frames);
        Assert.All(sink.DomainStates, domainState => Assert.Null(domainState));

        // The uninterrupted run and the reconstruction land on identical per-object bytes, so
        // checkpoint restore + input replay reproduced the run exactly up to the last boundary.
        var liveObjects = PersistedObjects(game);
        var reconstructedObjects = PersistedObjects((Game)reconstructedHost.Game);
        Assert.Equal(liveObjects.Keys.OrderBy(k => k), reconstructedObjects.Keys.OrderBy(k => k));
        foreach (var (id, bytes) in liveObjects)
        {
            Assert.Equal(bytes, reconstructedObjects[id]);
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void Reconstruct_FromMidRunCheckpoint_ReplaysOnlyPostCheckpointInputs()
    {
        var (bundle, _, _) = RecordBundle(injectOrder: true, frames: 45);

        var reconstructor = RunReconstructor.Open(bundle, CreateInstalledBootstrap());
        var sink = new BufferedReconstructionSink();
        reconstructor.Reconstruct(32, 45, sink);

        // Checkpoint 30 carries the already-dispatched order schedule as consumed state; the
        // replay must not double-dispatch it (a double dispatch would diverge every later hash).
        Assert.Equal(new uint[] { 35, 40, 45 }, sink.Frames);
    }

    [GameFact(SageGame.CncGenerals)]
    public void Reconstruct_FirstDivergenceStopsAllDelivery()
    {
        var (bundle, _, _) = RecordBundle(injectOrder: false, frames: 30);

        var reconstructor = RunReconstructor.Open(bundle, CreateDivergingBootstrap());
        var sink = new BufferedReconstructionSink();
        var exception = Assert.Throws<ReconstructionDivergence>(() => reconstructor.Reconstruct(0, 30, sink));

        Assert.Equal(5u, exception.BoundaryFrame);
        Assert.NotEqual(exception.RecordedWorldHash, exception.ReconstructedWorldHash);
        Assert.NotEmpty(exception.DivergedBucketKeys);
        Assert.Empty(sink.Frames);
    }

    [GameFact(SageGame.CncGenerals)]
    public void ExportSlice_WritesArtifactLoadableByA1Readers()
    {
        var (bundle, _, _) = RecordBundle(injectOrder: false, frames: 46);

        var reconstructor = RunReconstructor.Open(bundle, CreateInstalledBootstrap());
        var slicePath = Path.Combine(Path.GetTempPath(), "a1-22-slice-" + Guid.NewGuid().ToString("N") + ".bin");
        reconstructor.ExportSlice(10, 40, slicePath);

        var (snapshots, deltas) = WorldStateArtifact.Load(slicePath, Generals);
        var boundaryFrames = new uint[] { 10, 15, 20, 25, 30, 35, 40 };
        Assert.Equal(new uint[] { 10 }, snapshots.Select(s => s.Frame).ToArray());
        Assert.Equal(boundaryFrames[1..], deltas.Select(d => d.Frame).ToArray());

        var atForty = WorldStateArtifact.Reconstruct(snapshots, deltas, 40);
        Assert.Equal(40u, atForty.Frame);
        Assert.NotEmpty(atForty.Objects);
    }

    private SimulationHost BootHost()
    {
        var game = BootGame();
        _bootedGames.Add(game);
        return new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));
    }

    private InstalledBootstrap CreateInstalledBootstrap() => new(BootHost);

    private InstalledBootstrap CreateDivergingBootstrap()
    {
        return new InstalledBootstrap(() =>
        {
            var game = BootGame();
            _bootedGames.Add(game);
            return new SimulationHost(game.GameEngine, new DivergingRtsDomain(new RtsSimulationDomainModel(game.GameEngine)));
        });
    }

    private (string Bundle, Game Game, SimulationHost Host) RecordBundle(bool injectOrder, uint frames)
    {
        var game = BootGame();
        _bootedGames.Add(game);
        var host = new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));
        var bundle = Path.Combine(Path.GetTempPath(), "a1-22-bundle-" + Guid.NewGuid().ToString("N"));
        var cadence = new RecordingCadence(frame => frame % 5 == 0, frame => frame % 15 == 0);

        var recording = RunRecording.Attach(
            host, game, seed: 41u, scenarioId: "a1-22-reconstruct",
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
        return (bundle, game, host);
    }

    private static string WriteSyntheticBundle(uint manifestSeed, uint identitySeed)
    {
        var bundle = Path.Combine(Path.GetTempPath(), "a1-22-synthetic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(bundle, "checkpoints"));
        var identity = OperationalRecorder.CaptureBundleIdentity(identitySeed, "a1-22-synthetic");
        var manifest = new RunOutput
        {
            RunId = "a1-22-synthetic",
            Seed = manifestSeed,
            ScenarioId = "a1-22-synthetic",
            RecordingMode = RecordingMode.Operational,
            Identity = identity,
            OperationalStreamPath = "operational.bin",
            Checkpoints = new List<RunCheckpointRecord>
            {
                new()
                {
                    Frame = 15,
                    Path = "checkpoints/000000015.ckpt",
                    Bytes = 1,
                    XxHash3 = "0000000000000000",
                },
            },
        };
        manifest.SaveToFile(Path.Combine(bundle, "run.json"));
        File.WriteAllBytes(Path.Combine(bundle, "operational.bin"), [1]);
        return bundle;
    }

    private static Dictionary<uint, byte[]> PersistedObjects(Game game)
    {
        var result = new Dictionary<uint, byte[]>();
        foreach (var gameObject in game.Scene3D.GameObjects.Objects)
        {
            using var stream = new MemoryStream();
            using (var writer = new StateWriter(stream, game))
            {
                writer.PersistObject(gameObject, "Object");
            }
            result[gameObject.Id.Index] = stream.ToArray();
        }

        return result;
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

    private sealed class UnreachableBootstrap : IReconstructionBootstrap
    {
        public SimulationHost Boot() => throw new InvalidOperationException("Boot must not be reached.");
    }

    private sealed class InstalledBootstrap(Func<SimulationHost> factory) : IReconstructionBootstrap
    {
        public SimulationHost Boot() => factory();
    }

    /// <summary>
    /// Reports every entity a thousand units east of where the recorded run saw it: the same
    /// game logic, but summaries (and therefore boundary hashes) that cannot match the bundle.
    /// </summary>
    private sealed class DivergingRtsDomain(RtsSimulationDomainModel inner) : ISimulationDomainModel
    {
        public string DomainId => inner.DomainId;

        public DomainFidelityPolicy Fidelity => inner.Fidelity;

        public void Initialize(SimulationHost host) => inner.Initialize(host);

        public void RunStarting() => inner.RunStarting();

        public void RunComplete(in SimulationHostRunResult result) => inner.RunComplete(in result);

        public IReadOnlyList<DomainEntitySummary> CaptureEntities() =>
            inner.CaptureEntities().Select(e => e with { Position = e.Position + new Vector3(1000f, 0, 0) }).ToList();

        public IReadOnlyList<uint> CapturePlayerMoneyVector() => inner.CapturePlayerMoneyVector();

        public string TopologyKey(in DomainEntitySummary entity) => inner.TopologyKey(in entity);

        public DomainTopologyDescription DescribeTopology() => inner.DescribeTopology();

        public DomainMetricsSnapshot SampleMetrics() => inner.SampleMetrics();

        public uint ResolveInteractionAuthority(in DomainInteraction interaction) => inner.ResolveInteractionAuthority(in interaction);
    }
}
