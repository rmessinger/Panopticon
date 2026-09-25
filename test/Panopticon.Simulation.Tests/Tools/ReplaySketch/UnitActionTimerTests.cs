using System.Collections.Generic;
using System.Numerics;
using Panopticon.Logic.Orders;
using Panopticon.Tools.ReplaySketch.Model;
using Panopticon.Tools.ReplaySketch.Services;
using Xunit;

namespace Panopticon.Tests.Tools.ReplaySketch;

public class UnitActionTimerTests
{
    private static PrimaryAction AttackAction => new(PrimaryActionKind.Attack, OrderType.AttackObject, null, 100);

    [Fact, Trait("Category", "Roadmap")]
    public void UnitCompletingOnKnownFrame_ReportsExactlyThatDelta()
    {
        var timer = new UnitActionTimer();
        timer.OnDispatched(unitKey: 1, "Unit", 2, PrimaryAction.Move(120), frame: 10);

        var observations = new Dictionary<uint, UnitObservation>
        {
            [1] = new() { TargetPointsEmpty = false },
        };

        timer.Poll(15, k => observations[1]); // 5 frames in, not complete
        timer.Poll(24, k => observations[1]); // 14 frames in, not complete
        observations[1] = new() { TargetPointsEmpty = true };
        timer.Poll(30, k => observations[1]); // 20 frames in, complete

        var report = timer.BuildReport();
        var timing = Assert.Single(report.Timings);
        Assert.Equal(20u, timing.Frames);
        Assert.Empty(report.Timeouts);
        Assert.Empty(report.Gaps);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void AttackCompletes_WhenTargetDestroyed()
    {
        var timer = new UnitActionTimer();
        timer.OnDispatched(2, "Attacker", 2, AttackAction, frame: 0, targetKey: 9);

        // Attack completes only when the target is destroyed, not on the first damaging shot.
        UnitObservation Obs(bool destroyed) => new() { TargetHealth = destroyed ? (float?)null : 100f, TargetDestroyed = destroyed };

        timer.Poll(5, _ => Obs(false));   // alive, taking damage
        timer.Poll(10, _ => Obs(false));  // still alive
        timer.Poll(12, _ => Obs(true));   // destroyed -> complete

        var report = timer.BuildReport();
        var timing = Assert.Single(report.Timings);
        Assert.Equal(PrimaryActionKind.Attack, timing.ActionKind);
        Assert.Equal(12u, timing.Frames);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void UnitThatNeverCompletes_ReportsTimeoutAtBudget_WithNoDuration()
    {
        var timer = new UnitActionTimer();
        timer.OnDispatched(3, "Stuck", 2, PrimaryAction.Move(50), frame: 0);

        timer.Poll(49, _ => new UnitObservation { TargetPointsEmpty = false });
        timer.Poll(50, _ => new UnitObservation { TargetPointsEmpty = false });

        var report = timer.BuildReport();
        var timeout = Assert.Single(report.Timeouts);
        Assert.Equal(PrimaryActionKind.Move, timeout.ActionKind);
        Assert.Equal(50, timeout.FrameBudget);
        Assert.Empty(report.Timings);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void Gaps_Timeouts_Timings_AreDistinctCollections()
    {
        var timer = new UnitActionTimer();
        timer.RecordGap("GapUnit", 3, PrimaryActionKind.SpecialPower, "unimplemented handler");
        timer.OnDispatched(10, "DoneUnit", 2, PrimaryAction.Move(100), frame: 0);
        timer.OnDispatched(11, "SlowUnit", 4, PrimaryAction.Move(5), frame: 0);

        timer.Poll(4, k => new UnitObservation { TargetPointsEmpty = k == 10 }); // 10 completes; 11 not yet
        timer.Poll(6, k => new UnitObservation { TargetPointsEmpty = false });   // 11 times out at budget 5

        var report = timer.BuildReport();
        Assert.Single(report.Timings);
        Assert.Single(report.Timeouts);
        Assert.Single(report.Gaps);
        Assert.Equal(3, report.TotalUnitCount);
    }
}
