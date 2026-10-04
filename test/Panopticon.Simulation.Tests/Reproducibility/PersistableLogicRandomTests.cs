using System;
using System.IO;
using Panopticon.Data.Sav;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Tests.Data;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Reproducibility;

// WI-A1-14: GameLogic.Random is a persistable SageRandom whose six-word state
// round-trips through logic persistence, so a restored run continues the exact
// draw stream instead of restarting it.

[Collection("SerialGameTests")]
public sealed class PersistableLogicRandomTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private Game? _game;

    public PersistableLogicRandomTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void SageRandom_StateRoundTripsThroughWords()
    {
        var advanced = new SageRandom();
        advanced.Initialize(42);
        ConsumeDraws(advanced, 100);

        var restored = new SageRandom();
        restored.Initialize(42);

        var words = new uint[SageRandom.StateWordCount];
        advanced.CopyState(words);
        restored.RestoreState(words);

        for (var i = 0; i < 1000; i++)
        {
            Assert.Equal(advanced.Next(0, 1_000_000), restored.Next(0, 1_000_000));
            Assert.Equal(advanced.NextSingle(0f, 1f), restored.NextSingle(0f, 1f));
        }

        // Restoring the advanced state must not be a no-op: a fresh generator
        // with the same seed is at a different draw position.
        var fresh = new SageRandom();
        fresh.Initialize(42);
        var differsFromFresh = false;
        for (var i = 0; i < 10; i++)
        {
            if (advanced.Next(0, 1_000_000) != fresh.Next(0, 1_000_000))
            {
                differsFromFresh = true;
            }
        }
        Assert.True(differsFromFresh);
    }

    [GameFact(SageGame.CncGenerals)]
    public void LogicCheckpoint_RestoresRandomDrawPosition()
    {
        using var gameA = _game = BootGame(seed: 12345);
        for (var i = 0; i < 30; i++)
        {
            gameA.Update([]);
        }

        // Explicit draws advance the stream deterministically regardless of
        // whether the idle scenario itself consumes randomness.
        var preCheckpointDraws = new int[10];
        for (var i = 0; i < preCheckpointDraws.Length; i++)
        {
            preCheckpointDraws[i] = gameA.GameLogic.Random.Next(0, 1_000_000);
        }

        byte[] checkpoint;
        using (var stream = new MemoryStream())
        {
            using (var writer = new StateWriter(stream, gameA))
            {
                SaveFile.Persist(writer, SaveFile.IsLogicChunk);
            }
            checkpoint = stream.ToArray();
        }
        _output.WriteLine($"logic checkpoint: {checkpoint.Length} bytes");

        using var gameB = BootGame(seed: 12345);
        using (var stream = new MemoryStream(checkpoint, writable: false))
        {
            SaveFile.LoadFromStream(stream, gameB);
        }

        var restoredRandom = Assert.IsType<SageRandom>(gameB.GameLogic.Random);

        var expectedDraws = new int[10];
        var restoredDraws = new int[10];
        for (var i = 0; i < 10; i++)
        {
            expectedDraws[i] = gameA.GameLogic.Random.Next(0, 1_000_000);
            restoredDraws[i] = gameB.GameLogic.Random.Next(0, 1_000_000);
        }

        Assert.Equal(expectedDraws, restoredDraws);

        // Without the restore, game B's fresh stream would still be at draw
        // one; the checkpoint must place it after the pre-checkpoint draws.
        var overlapsFreshStart = true;
        for (var i = 0; i < preCheckpointDraws.Length; i++)
        {
            if (restoredDraws[i] != preCheckpointDraws[i])
            {
                overlapsFreshStart = false;
            }
        }
        Assert.False(overlapsFreshStart);

        var postRestoreWords = new uint[SageRandom.StateWordCount];
        restoredRandom.CopyState(postRestoreWords);
        _output.WriteLine($"restored random state: {string.Join(", ", postRestoreWords)}");
    }

    private Game BootGame(int seed)
    {
        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);

        Platform.Start();
        var game = new Game(
            installation,
            null,
            new Configuration { NoAudio = true, SkipStartingBuildings = true },
            null);

        var playerSettings = new PlayerSetting[]
        {
            new(0, "FactionAmerica", new ColorRgb(255, 0, 0), 0, PlayerOwner.Player, "USA"),
            new(1, "FactionChina", new ColorRgb(0, 255, 0), 0, PlayerOwner.EasyAi, "China"),
        };
        game.StartSkirmishOrMultiPlayerGame(
            "maps/alpine assault/alpine assault.map",
            new EchoConnection(),
            playerSettings,
            seed,
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

    private static void ConsumeDraws(SageRandom random, int count)
    {
        for (var i = 0; i < count; i++)
        {
            random.Next(0, 1_000_000);
            random.NextSingle(0f, 1f);
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
