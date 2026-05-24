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
    public void DefaultScenario_GatherResources_TriggersOnBuildBarracksDispatched()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();

        foreach (var player in scenario.Players)
        {
            var gather = player.Actions[1];
            Assert.Equal(ActionType.GatherResources, gather.Type);

            var timing = Assert.IsType<AfterActionTiming>(gather.Timing);
            Assert.Equal(ActionType.BuildBarracks, timing.TriggerAction);
            Assert.Equal(ActionPhase.Dispatched, timing.TriggerPhase);
        }
    }

    [Fact]
    public void DefaultScenario_RecruitBasicUnit_TriggersOnBuildBarracksCompleted()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();

        foreach (var player in scenario.Players)
        {
            var recruit = player.Actions[2];
            Assert.Equal(ActionType.RecruitBasicUnit, recruit.Type);

            var timing = Assert.IsType<AfterActionTiming>(recruit.Timing);
            Assert.Equal(ActionType.BuildBarracks, timing.TriggerAction);
            Assert.Equal(ActionPhase.Completed, timing.TriggerPhase);
        }
    }

    [Fact]
    public void DefaultScenario_FixedTimedActions_HavePositiveFrameOffsets()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();
        var rng = new Random(0);

        foreach (var player in scenario.Players)
        {
            foreach (var action in player.Actions)
            {
                if (action.Timing is FixedTiming ft)
                {
                    Assert.True(ft.Frames > 0,
                        $"{player.Name}: action {action.Type} must have a positive fixed frame offset.");
                }
            }
        }
    }

    [Fact]
    public void DefaultScenario_PrebakedActions_HaveMonotonicallyIncreasingFrames()
    {
        var scenario = ReplayScenario.CreateAlpineAssaultUSAvGLA();
        var rng = new Random(0);

        foreach (var player in scenario.Players)
        {
            uint cumulative = 0;
            uint previous = 0;
            foreach (var action in player.Actions)
            {
                // Deferred actions (AfterActionTiming) return 0 from Resolve and are injected
                // at runtime rather than baked into the replay — skip them in this check.
                if (action.Timing is AfterActionTiming)
                {
                    continue;
                }

                cumulative += action.Timing.Resolve(rng);
                Assert.True(cumulative > previous,
                    $"{player.Name}: action {action.Type} must fire at a later frame than the previous pre-baked action.");
                previous = cumulative;
            }
        }
    }
}

