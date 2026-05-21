using System;
using OpenSage.Tools.ReplaySketch.Model;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

public class ReplaySketchTests
{
    [Fact]
    public void DefaultScenario_UsaActions_AreInExpectedOrder()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();
        var usa = scenario.Players[0];

        Assert.Equal(4, usa.Actions.Count);
        Assert.Equal(ActionType.BuildBarracks, usa.Actions[0].Type);
        Assert.Equal(ActionType.GatherResources, usa.Actions[1].Type);
        Assert.Equal(ActionType.RecruitBasicUnit, usa.Actions[2].Type);
        Assert.Equal(ActionType.AttackEnemyBase, usa.Actions[3].Type);
    }

    [Fact]
    public void DefaultScenario_GlaActions_AreInExpectedOrder()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();
        var gla = scenario.Players[1];

        Assert.Equal(4, gla.Actions.Count);
        Assert.Equal(ActionType.BuildBarracks, gla.Actions[0].Type);
        Assert.Equal(ActionType.GatherResources, gla.Actions[1].Type);
        Assert.Equal(ActionType.RecruitBasicUnit, gla.Actions[2].Type);
        Assert.Equal(ActionType.AttackEnemyBase, gla.Actions[3].Type);
    }

    [Fact]
    public void DefaultScenario_GatherResources_IsScheduledAfterBarracksConstruction()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();
        var rng = new Random(0);

        foreach (var player in scenario.Players)
        {
            var barracksFrame = player.Actions[0].Timing.Resolve(rng);
            var gatherOffset = player.Actions[1].Timing.Resolve(rng);

            Assert.Equal(ActionType.BuildBarracks, player.Actions[0].Type);
            Assert.Equal(ActionType.GatherResources, player.Actions[1].Type);
            Assert.True(barracksFrame > 0, $"{player.Name}: BuildBarracks must be scheduled at a non-zero frame.");
            Assert.True(gatherOffset > 0, $"{player.Name}: GatherResources must fire after BuildBarracks (positive offset).");
        }
    }

    [Fact]
    public void DefaultScenario_CumulativeFrames_AreMonotonicallyIncreasing()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();
        var rng = new Random(0);

        foreach (var player in scenario.Players)
        {
            uint cumulative = 0;
            uint previous = 0;
            for (var i = 0; i < player.Actions.Count; i++)
            {
                cumulative += player.Actions[i].Timing.Resolve(rng);
                Assert.True(cumulative > previous,
                    $"{player.Name}: action {i} ({player.Actions[i].Type}) must fire at a later frame than the previous action.");
                previous = cumulative;
            }
        }
    }
}

