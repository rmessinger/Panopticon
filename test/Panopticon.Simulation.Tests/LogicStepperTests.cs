using System;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests;

/// <summary>
/// A1-3 logic-tick extraction: the stepper advances the logic frame counter with no
/// <c>Game.Render</c> call and no presentation systems (WI-A1-05).
/// </summary>
public class LogicStepperTests : MockedGameTest
{
    private static (MockedGameTest.TestGame Game, LogicStepper Stepper) CreateStepperGame()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var stepper = new LogicStepper(game);
        return (game, stepper);
    }

    [Fact]
    public void AdvanceLogic_AdvancesFrameCount_ByNumberOfSteps()
    {
        var (game, stepper) = CreateStepperGame();

        var startFrame = game.GameLogic.CurrentFrame.Value;
        const int steps = 10;

        // Fixed-timestep map time: one MsPerLogicFrame per step, matching
        // Game.EnableFixedLogicTimestep.
        var interval = TimeSpan.FromMilliseconds(game.GameEngine.MsPerLogicFrame);
        for (var i = 1; i <= steps; i++)
        {
            stepper.AdvanceLogic(interval * i);
        }

        Assert.Equal(startFrame + steps, game.GameLogic.CurrentFrame.Value);
    }

    [Fact]
    public void AdvanceLogic_DoesNotTick_WhenPaused()
    {
        var (game, stepper) = CreateStepperGame();

        var startFrame = game.GameLogic.CurrentFrame.Value;
        game.IsLogicRunning = false;

        var interval = TimeSpan.FromMilliseconds(game.GameEngine.MsPerLogicFrame);
        for (var i = 1; i <= 5; i++)
        {
            stepper.AdvanceLogic(interval * i);
        }

        Assert.Equal(startFrame, game.GameLogic.CurrentFrame.Value);
    }

    [Fact]
    public void Reset_RestoresInitialSchedule()
    {
        var (game, stepper) = CreateStepperGame();

        var interval = TimeSpan.FromMilliseconds(game.GameEngine.MsPerLogicFrame);
        stepper.AdvanceLogic(interval);

        stepper.Reset();

        // After Reset, time zero is due immediately: one tick fires for map time zero.
        var startFrame = game.GameLogic.CurrentFrame.Value;
        stepper.AdvanceLogic(TimeSpan.Zero);
        Assert.Equal(startFrame + 1, game.GameLogic.CurrentFrame.Value);
    }
}
