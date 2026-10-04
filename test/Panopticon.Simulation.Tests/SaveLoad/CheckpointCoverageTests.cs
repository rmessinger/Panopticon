using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Data.Sav;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Tests.Data;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.SaveLoad;

// WI-A1-13: operational checkpoints persist the logic chunks only, and a restored
// game continues producing the same per-object state as an uninterrupted run.

[Collection("SerialGameTests")]
public sealed class CheckpointCoverageTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private Game? _game;

    public CheckpointCoverageTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void LogicChunkPartition_MatchesSaveFileChunkList()
    {
        var logic = SaveFile.LogicChunkNames;
        var persisted = SaveFile.PersistedChunkNames;

        Assert.All(logic, name => Assert.Contains(name, persisted));
        Assert.Equal(logic.Count, logic.Distinct().Count());

        var presentation = persisted.Where(name => !SaveFile.IsLogicChunk(name)).ToArray();
        Assert.Equal(
            new[]
            {
                "CHUNK_ParticleSystem",
                "CHUNK_Radar",
                "CHUNK_TacticalView",
                "CHUNK_GameClient",
                "CHUNK_InGameUI",
                "CHUNK_TerrainVisual",
            },
            presentation);
    }

    [GameFact(SageGame.CncGenerals)]
    public void LogicCheckpoint_RestoresAndMatchesUninterruptedRun()
    {
        using var gameA = _game = BootGame(seed: 12345);
        Step(gameA, 90);

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
        _output.WriteLine($"restored at frame {gameB.GameLogic.CurrentFrame.Value}");

        Step(gameA, 30);
        Step(gameB, 30);

        Assert.Equal(
            gameA.GameLogic.CurrentFrame.Value,
            gameB.GameLogic.CurrentFrame.Value);

        var expected = PersistedObjects(gameA);
        var actual = PersistedObjects(gameB);
        Assert.Equal(
            expected.Keys.OrderBy(key => key),
            actual.Keys.OrderBy(key => key));
        foreach (var (key, bytes) in expected)
        {
            Assert.True(actual.TryGetValue(key, out var restored));
            Assert.Equal(bytes, restored);
        }
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

    private static void Step(Game game, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            game.Update([]);
        }
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

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
