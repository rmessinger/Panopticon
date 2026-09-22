using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenSage.Logic;
using OpenSage.Mathematics;
using OpenSage.Network;
using OpenSage.Tests.Data;
using Xunit;
using Xunit.Abstractions;

namespace OpenSage.Tests.Reproducibility;

// A2 seeded-source reproducibility (WI-A2-03): two same-seed boots of one fixed-timestep
// scenario agree on integer key metrics and on the random stream itself (T-exact tier,
// deep dive a2-reproducibility §2.2); a different seed produces a different stream.

[Collection("SerialGameTests")]
public sealed class SeededRunTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private Game? _game;

    public SeededRunTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private sealed record RunMetrics(
        uint LogicFrame,
        int ObjectCount,
        IReadOnlyList<uint> PlayerMoney,
        IReadOnlyList<int> RandomProbe)
    {
        public static RunMetrics Capture(Game game)
        {
            var money = game.Scene3D.Players
                .Select(p => p.BankAccount.Money)
                .ToArray();
            var probe = new[]
            {
                game.GameLogic.Random.Next(0, 1_000_000),
                game.GameLogic.Random.Next(0, 1_000_000),
                game.GameLogic.Random.Next(0, 1_000_000),
                game.GameLogic.Random.Next(0, 1_000_000),
            };
            return new RunMetrics(
                game.GameLogic.CurrentFrame.Value,
                game.Scene3D.GameObjects.Objects.Count(),
                money,
                probe);
        }
    }

    private RunMetrics RunScenario(int seed)
    {
        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);

        Platform.Start();
        using var game = _game = new Game(
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

        for (var i = 0; i < 90; i++)
        {
            game.Update([]);
        }

        var metrics = RunMetrics.Capture(game);
        _output.WriteLine($"seed={seed} frame={metrics.LogicFrame} objects={metrics.ObjectCount} probe=[{string.Join(",", metrics.RandomProbe)}]");
        return metrics;
    }

    [GameFact(SageGame.CncGenerals)]
    public void SameSeed_ProducesIdenticalMetrics_AndStream()
    {
        var first = RunScenario(seed: 12345);
        var second = RunScenario(seed: 12345);

        Assert.Equal(first.LogicFrame, second.LogicFrame);
        Assert.Equal(first.ObjectCount, second.ObjectCount);
        Assert.Equal(first.PlayerMoney, second.PlayerMoney);
        Assert.Equal(first.RandomProbe, second.RandomProbe);
    }

    [GameFact(SageGame.CncGenerals)]
    public void DifferentSeed_ProducesDifferentStream()
    {
        var first = RunScenario(seed: 12345);
        var second = RunScenario(seed: 54321);

        Assert.NotEqual(first.RandomProbe, second.RandomProbe);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
