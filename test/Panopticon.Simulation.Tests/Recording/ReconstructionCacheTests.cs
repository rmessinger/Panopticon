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

// WI-A1-23: derived-checkpoint cache. LRU budgeting, identity purge, boundary-hash discard,
// and nearest-entry selection run without an install; the cache-hit path runs against the
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
        cache.Store("run", 5, "identity", 100, new byte[] { 1 });
        cache.Store("run", 10, "identity", 200, new byte[] { 2 });
        cache.Store("run", 20, "identity", 300, new byte[] { 3 });

        var hit = Assert.IsType<CachedCheckpoint>(cache.TryGetNearest("run", "identity", 17));

        Assert.Equal(10u, hit.Frame);
        Assert.Equal(200ul, hit.RecordedWorldHash);
        Assert.Equal(new byte[] { 2 }, hit.Checkpoint);

        Assert.Null(cache.TryGetNearest("run", "identity", 4));
        Assert.Null(cache.TryGetNearest("other-run", "identity", 20));
    }

    [Fact]
    public void LruEviction_KeepsTotalBytesWithinBudget()
    {
        var cache = new ReconstructionCache(budgetBytes: 1_300);
        var payload = new byte[400];

        cache.Store("run", 5, "identity", 1, payload);
        cache.Store("run", 10, "identity", 2, payload);
        cache.Store("run", 15, "identity", 3, payload);
        cache.Store("run", 20, "identity", 4, payload);

        // 4 x 400 bytes exceed the 1300-byte budget: frame 5 was evicted.
        Assert.Equal(3, cache.Count);
        Assert.Equal(1_200, cache.TotalBytes);
        Assert.Null(cache.TryGetNearest("run", "identity", 5));
        Assert.NotNull(cache.TryGetNearest("run", "identity", 10));

        // Touching frame 10 makes 15 the least recently used entry, so the next store evicts it;
        // the nearest entry at or before 15 is then 10.
        cache.TryGetNearest("run", "identity", 10);
        cache.Store("run", 25, "identity", 5, payload);
        var afterEviction = Assert.IsType<CachedCheckpoint>(cache.TryGetNearest("run", "identity", 15));
        Assert.Equal(10u, afterEviction.Frame);
        Assert.NotNull(cache.TryGetNearest("run", "identity", 25));
    }

    [Fact]
    public void IdentityMismatch_PurgesEveryEntryOfThatRun()
    {
        var cache = new ReconstructionCache(budgetBytes: 1_000_000);
        cache.Store("run", 5, "identity-a", 100, new byte[] { 1 });
        cache.Store("run", 10, "identity-a", 200, new byte[] { 2 });
        cache.Store("other-run", 5, "identity-a", 300, new byte[] { 3 });

        Assert.Null(cache.TryGetNearest("run", "identity-b", 10));

        // The purge is scoped to the mismatched run; other runs keep their entries.
        Assert.Equal(1, cache.Count);
        var survivor = Assert.IsType<CachedCheckpoint>(cache.TryGetNearest("other-run", "identity-a", 5));
        Assert.Equal(300ul, survivor.RecordedWorldHash);
    }

    [Fact]
    public void BoundaryHashMismatch_DiscardsEntryBeforeUse()
    {
        var cache = new ReconstructionCache(budgetBytes: 1_000_000);
        cache.Store("run", 5, "identity", 100, new byte[] { 1 });
        cache.Store("run", 10, "identity", 200, new byte[] { 2 });

        // A stored hash that differs from the bundle's recorded hash discards the entry and
        // the search continues downward to the nearest still-valid boundary.
        var wrongHash = new Dictionary<uint, ulong> { [5] = 100, [10] = 999 };
        var entry = Assert.IsType<CachedCheckpoint>(cache.TryGetNearest("run", "identity", 10, wrongHash));
        Assert.Equal(5u, entry.Frame);
        Assert.Equal(1, cache.Count);

        // A boundary absent from the bundle's hashes is equally invalid: every entry is
        // discarded and nothing is returned.
        var emptyHashes = new Dictionary<uint, ulong>();
        Assert.Null(cache.TryGetNearest("run", "identity", 10, emptyHashes));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void OversizedCheckpoint_IsNotStored()
    {
        var cache = new ReconstructionCache(budgetBytes: 8);

        cache.Store("run", 5, "identity", 100, new byte[9]);

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

        // Boundaries 5..25 (inclusive) were verified and cached; the frame-0 origin checkpoint
        // is not re-stored.
        Assert.Equal(5, reconstructionCache.Count);
        var cached = Assert.IsType<CachedCheckpoint>(
            reconstructionCache.TryGetNearest(reconstructor.Manifest.RunId, reconstructor.Manifest.Identity!.StableHash(), 20));
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
