using System;
using System.Collections.Generic;
using Panopticon.Logic.AI;
using Panopticon.Tests.Logic.AI.Hierarchy;
using Xunit;

namespace Panopticon.Tests;

/// <summary>
/// WI-M4-02: the tick-scoped AI decision span. Decision time accumulates across all AI
/// players, LogicStepper emits it as the "ai" span right after gameLogic, and the total is
/// consumed once per tick on both the measured and the unmeasured path.
/// </summary>
public class AiDecisionSpanTests : MockedGameTest
{
    [Fact]
    public void MeasuredTick_EmitsAiSpanAfterGameLogic_WithTheAccumulatedValue()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var stepper = new LogicStepper(game);
        var spans = new List<(string Name, TimeSpan Span)>();
        stepper.SystemSpan += (name, span) => spans.Add((name, span));

        var known = TimeSpan.FromMilliseconds(5);
        game.AddAiDecisionSpan(known);
        stepper.AdvanceLogic(TimeSpan.FromMilliseconds(game.GameEngine.MsPerLogicFrame));

        var gameLogicIndex = spans.FindIndex(s => s.Name == "gameLogic");
        var aiIndex = spans.FindIndex(s => s.Name == "ai");
        Assert.True(gameLogicIndex >= 0);
        Assert.Equal(gameLogicIndex + 1, aiIndex);
        Assert.Equal(known, spans[aiIndex].Span);
    }

    [Fact]
    public void MeasuredTick_ConsumesTheSpan_ItDoesNotLeakToTheNextTick()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var stepper = new LogicStepper(game);
        var spans = new List<(string Name, TimeSpan Span)>();
        stepper.SystemSpan += (name, span) => spans.Add((name, span));

        var interval = TimeSpan.FromMilliseconds(game.GameEngine.MsPerLogicFrame);
        game.AddAiDecisionSpan(TimeSpan.FromMilliseconds(5));
        stepper.AdvanceLogic(interval);
        stepper.AdvanceLogic(interval * 2);

        var aiSpans = spans.FindAll(s => s.Name == "ai");
        Assert.Equal(2, aiSpans.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(5), aiSpans[0].Span);
        Assert.Equal(TimeSpan.Zero, aiSpans[1].Span);
    }

    [Fact]
    public void AIPlayerUpdate_AccumulatesDecisionTimeIntoTheGame()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var player = HierarchyTestPlayers.CreateAiPlayer(game);
        var aiPlayer = new AIPlayer(player);

        // The countdown reaches the decision tick after 60 updates.
        for (var i = 0; i < AIPlayer.DefaultDecisionCadenceFrames; i++)
        {
            aiPlayer.Update();
        }

        Assert.True(game.ConsumeAiDecisionSpan() > TimeSpan.Zero);
    }
}
