using System;
using System.IO;
using System.Linq;
using OpenSage.Logic;
using Xunit;

namespace OpenSage.Tests;

/// <summary>
/// A1-5 T0 run output (WI-A1-07): a headless host run writes a JSON run output whose seed,
/// frame count, and terminal GameOutcome round-trip through a reload; the metric series is
/// bounded by the sampling interval.
/// </summary>
public class RunOutputTests : MockedGameTest
{
    private static void ConfigureTwoCompetitivePlayers(TestGame game)
    {
        var mapPlayers = new[]
        {
            OpenSage.Data.Map.Player.CreateNeutralPlayer(),
            OpenSage.Data.Map.Player.CreateCivilianPlayer(),
            new OpenSage.Data.Map.Player
            {
                Name = "player2",
                Faction = "FactionAmerica",
                DisplayName = "Player 2",
                IsHuman = true,
            },
            new OpenSage.Data.Map.Player
            {
                Name = "player3",
                Faction = "FactionChina",
                DisplayName = "Player 3",
                IsHuman = true,
            },
        };
        game.PlayerManager.OnNewGame(mapPlayers, GameType.Skirmish);
    }

    [Fact]
    public void HeadlessRun_WritesReloadableRunOutput()
    {
        var game = new TestGame(SageGame.CncGenerals);
        ConfigureTwoCompetitivePlayers(game);
        game.GameLogic.Random.Initialize(777u);

        var host = new SimulationHost(game.GameEngine);
        using var recorder = new RunOutputRecorder(host, game, seed: 777u, scenarioId: "a1-runoutput-test");

        // Drive a terminal outcome and let a tick decide it.
        host.Run(90);
        game.VictorySystem.Surrender(game.PlayerManager.Players[2]);
        host.Run(10);

        var output = recorder.Complete();
        // The surrender decides the match at frame 90 and the engine pauses logic, so the run
        // ends there; the output must reflect the frames actually simulated.
        Assert.Equal(90u, output.TotalLogicFrames);
        Assert.Equal(game.GameLogic.CurrentFrame.Value, output.TotalLogicFrames);
        Assert.Equal(777u, output.Seed);
        Assert.Equal("a1-runoutput-test", output.ScenarioId);
        Assert.Equal(OutcomeStatus.Decided, output.Outcome!.Status);
        Assert.Equal(OutcomeReason.Surrendered, output.Outcome.Reason);
        Assert.NotEmpty(output.MetricSamples);
        Assert.All(output.MetricSamples, s => Assert.Equal(4, s.Players.Count));

        var path = Path.Combine(Path.GetTempPath(), $"opensage_runoutput_{Guid.NewGuid():N}.json");
        try
        {
            output.SaveToFile(path);
            var reloaded = RunOutput.LoadFromFile(path);

            Assert.NotNull(reloaded);
            Assert.Equal(output.RunId, reloaded!.RunId);
            Assert.Equal(output.Seed, reloaded.Seed);
            Assert.Equal(output.ScenarioId, reloaded.ScenarioId);
            Assert.Equal(output.TotalLogicFrames, reloaded.TotalLogicFrames);
            Assert.Equal(output.Outcome.Status, reloaded.Outcome!.Status);
            Assert.Equal(output.Outcome.Reason, reloaded.Outcome.Reason);
            Assert.Equal(
                output.MetricSamples.Select(s => (s.Frame, s.Players.Select(p => (p.Player, p.Money, p.OwnedObjects)))),
                reloaded.MetricSamples.Select(s => (s.Frame, s.Players.Select(p => (p.Player, p.Money, p.OwnedObjects)))));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MetricSeries_IsBoundedBySampleInterval()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var host = new SimulationHost(game.GameEngine);
        using var recorder = new RunOutputRecorder(host, game, seed: 1u, sampleInterval: 25);

        host.Run(100);

        var output = recorder.Complete();
        Assert.Equal(4, output.MetricSamples.Count); // frames 25, 50, 75, 100
    }
}
