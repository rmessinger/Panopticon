using Panopticon.Logic;
using Panopticon.Logic.Object;
using Xunit;

namespace Panopticon.Tests.Logic.Victory;

public sealed class GameOutcomeTests : MockedGameTest
{
    [Fact]
    public void Outcome_IsQueryableFromGameAndGameLogic()
    {
        var gameOutcome = Generals.CurrentOutcome;
        var logicOutcome = Generals.GameLogic.CurrentOutcome;

        Assert.Same(gameOutcome, logicOutcome);
        Assert.Equal(OutcomeStatus.InProgress, gameOutcome.Status);
        Assert.Equal(OutcomeReason.None, gameOutcome.Reason);
        Assert.NotEqual(OutcomeReason.Eliminated, gameOutcome.Reason);
        Assert.NotEqual(OutcomeReason.Surrendered, gameOutcome.Reason);
        Assert.NotEqual(OutcomeReason.ScoreDecision, gameOutcome.Reason);
    }

    [Fact]
    public void VictorySystem_TicksAtNamedCadence()
    {
        for (var i = 0u; i < VictorySystem.DefaultCheckCadence.Value * 2u; i++)
        {
            Generals.GameLogic.Update();
        }

        Assert.Equal(2, Generals.VictorySystem.CheckTickCount);
        Assert.Equal(new LogicFrame(VictorySystem.DefaultCheckCadence.Value * 2u), Generals.VictorySystem.LastCheckFrame);
        Assert.Equal(OutcomeStatus.InProgress, Generals.CurrentOutcome.Status);
    }
}
