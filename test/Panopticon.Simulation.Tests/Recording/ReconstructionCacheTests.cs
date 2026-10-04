using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Recording;
using Panopticon.Simulation;
using Panopticon.Tests.Data;
using Xunit;

namespace Panopticon.Tests.Recording;

// WI-A1-23: in-memory derived-checkpoint cache. LRU budgeting and nearest-entry selection run
// without an install; the cache-hit path runs against the
// installed skirmish boot (GameFact), where hiding the bundle checkpoints proves the second
// reconstruction restored from the cached boundary.

[Collection("SerialGameTests")]
public sealed class ReconstructionCacheTests : MockedGameTest, IDisposable
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
    public void StoreAndTryGetNearest_ReturnsNearestFrameAtOrBeforeRequest()
    {
        var cache = new ReconstructionCache(budgetBytes: 1_000_000);
        cache.Store("run", 5, new byte[] { 1 });
        cache.Store("run", 10, new byte[] { 2 });
        cache.Store("run", 20, new byte[] { 3 });

        var hit = Assert.IsType<CachedCheckpoint>(cache.TryGetNearest("run", 17));

        Assert.Equal(10u, hit.Frame);
        Assert.Equal(new byte[] { 2 }, hit.Checkpoint);

        Assert.Null(cache.TryGetNearest("run", 4));
        Assert.Null(cache.TryGetNearest("other-run", 20));
    }

    [Fact]
    public void LruEviction_KeepsTotalBytesWithinBudget()
    {
        var cache = new ReconstructionCache(budgetBytes: 1_300);
        var payload = new byte[400];

        cache.Store("run", 5, payload);
        cache.Store("run", 10, payload);
        cache.Store("run", 15, payload);
        cache.Store("run", 20, payload);

        // 4 x 400 bytes exceed the 1300-byte budget: frame 5 was evicted.
        Assert.Equal(3, cache.Count);
        Assert.Equal(1_200, cache.TotalBytes);
        Assert.Null(cache.TryGetNearest("run", 5));
        Assert.NotNull(cache.TryGetNearest("run", 10));

        // Touching frame 10 makes 15 the least recently used entry, so the next store evicts it;
        // the nearest entry at or before 15 is then 10.
        cache.TryGetNearest("run", 10);
        cache.Store("run", 25, payload);
        var afterEviction = Assert.IsType<CachedCheckpoint>(cache.TryGetNearest("run", 15));
        Assert.Equal(10u, afterEviction.Frame);
        Assert.NotNull(cache.TryGetNearest("run", 25));
    }

    [Fact]
    public void OversizedCheckpoint_IsNotStored()
    {
        var cache = new ReconstructionCache(budgetBytes: 8);

        cache.Store("run", 5, new byte[9]);

        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.TotalBytes);
    }

    [GameFact(SageGame.CncGenerals)]
    public void SecondReconstruction_StartsFromCachedBoundaryCheckpoint()
    {
        var (bundle, _, _) = RecordBundle(frames: 45);
        var reconstructionCache = new ReconstructionCache(budgetBytes: 128L * 1024 * 1024);
        var reconstructor = RunReconstructor.Open(bundle, CreateInstalledBootstrap(), cache: reconstructionCache);

        var firstSink = new BufferedReconstructionSink();
        reconstructor.Reconstruct(0, 25, firstSink);
        Assert.Equal(Enumerable.Range(1, 5).Select(i => (uint)(i * 5)), firstSink.Frames);

        // Boundaries 5..25 (inclusive) were rebuilt and cached; the frame-0 origin checkpoint
        // is not re-stored.
        Assert.Equal(5, reconstructionCache.Count);
        var cached = Assert.IsType<CachedCheckpoint>(
            reconstructionCache.TryGetNearest(reconstructor.Manifest.RunId, 20));
        Assert.Equal(20u, cached.Frame);

        // Hiding the bundle checkpoints proves the second reconstruction restored from the
        // cache: the bundle fallback would throw FileNotFoundException for checkpoint 0.
        var checkpointsDirectory = Path.Combine(bundle, "checkpoints");
        var hiddenDirectory = checkpointsDirectory + "-hidden";
        Directory.Move(checkpointsDirectory, hiddenDirectory);
        try
        {
            var secondSink = new BufferedReconstructionSink();
            reconstructor.Reconstruct(20, 45, secondSink);
            Assert.Equal(new uint[] { 20, 25, 30, 35, 40, 45 }, secondSink.Frames);
        }
        finally
        {
            Directory.Move(hiddenDirectory, checkpointsDirectory);
        }
    }

    private (string Bundle, Game Game, SimulationHost Host) RecordBundle(uint frames)
    {
        var game = BootGame();
        _bootedGames.Add(game);
        var host = new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));
        var bundle = Path.Combine(Path.GetTempPath(), "a1-23-bundle-" + Guid.NewGuid().ToString("N"));
        var cadence = new RecordingCadence(frame => frame % 5 == 0, frame => frame % 15 == 0);

        var recording = RunRecording.Attach(
            host, game, seed: 41u, scenarioId: "a1-23-cache",
            options: new RecordingOptions { Mode = RecordingMode.Operational, BundleDirectory = bundle, DefaultCadence = cadence });
        host.Run((int)frames);
        recording.Complete();
        recording.Dispose();
        return (bundle, game, host);
    }

    private SimulationHost BootHost()
    {
        var game = BootGame();
        _bootedGames.Add(game);
        return new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));
    }

    private InstalledBootstrap CreateInstalledBootstrap() => new(BootHost);

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
