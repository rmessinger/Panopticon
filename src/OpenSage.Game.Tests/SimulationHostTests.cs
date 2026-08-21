using System;
using System.Linq;
using OpenSage.Logic.Orders;
using Xunit;

namespace OpenSage.Tests;

/// <summary>
/// A1-4 SimulationHost: runs logic frames against the mocked game with no Render call, no
/// window, honors a stop predicate, and routes injected orders through the shared
/// IGame.InjectOrders pipeline (WI-A1-06).
/// </summary>
public class SimulationHostTests : MockedGameTest
{
    private static (MockedGameTest.TestGame Game, SimulationHost Host) CreateHostGame()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var host = new SimulationHost(game.GameEngine);
        return (game, host);
    }

    [Fact]
    public void Run_AdvancesFrameCount_ByFrameBudget()
    {
        var (game, host) = CreateHostGame();

        var startFrame = game.GameLogic.CurrentFrame.Value;
        const int budget = 25;

        var result = host.Run(budget);

        Assert.Equal(budget, result.FramesCompleted);
        Assert.Equal((uint)(startFrame + budget), result.FinalLogicFrame.Value);
        Assert.Equal((uint)budget, host.Frame);
        Assert.False(result.StopPredicateTriggered);
    }

    [Fact]
    public void Run_StopPredicate_TerminatesEarly_AndReportsFrameReached()
    {
        var (game, host) = CreateHostGame();

        var startFrame = game.GameLogic.CurrentFrame.Value;
        var ticksObserved = 0;
        host.TickComplete += (_, _) => ticksObserved++;

        var result = host.Run(100, h => h.Frame >= 7);

        Assert.Equal(7, result.FramesCompleted);
        Assert.Equal((uint)(startFrame + 7), result.FinalLogicFrame.Value);
        Assert.True(result.StopPredicateTriggered);
        Assert.Equal(7, ticksObserved);
    }

    [Fact]
    public void InjectOrders_DispatchesThroughSharedPipeline_AtScheduledFrame()
    {
        var (game, host) = CreateHostGame();

        var order = new Order(1, OrderType.MoveTo);
        host.InjectOrders(3, new[] { order });

        // Before the scheduled frame the order is still pending.
        host.Run(2);
        Assert.Empty(game.InjectedOrders);

        // Reaching the frame dispatches it through IGame.InjectOrders.
        host.Run(1);
        Assert.Single(game.InjectedOrders);
        Assert.Same(order, game.InjectedOrders[0]);
    }
}
