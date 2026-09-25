// WI-P8-04: Real gather-cycle harness verification.
//
// The full SupplyAIUpdate cycle (SearchingForSupplySource -> Approaching -> Requesting ->
// Gathering -> DumpingSupplies) cannot be driven end to end in the headless MockedGameTest host
// for two precise, named reasons:
//
//   1. Locomotion. The ApproachingSupplySource/ApproachingSupplyTarget states transition on
//      `!isMoving` (ModelConditionFlag.Moving cleared), which only happens when a Locomotor
//      actually moves the unit to its target. The mocked host has no Locomotor/Scene3D tick, so
//      the unit never "arrives" and the state machine stalls at the first approach state.
//   2. PartitionCellManager. SupplyManager.FindClosestSupplyWarehouse resolves the nearest
//      warehouse via PartitionCellManager.GetDistanceBetweenObjectsSquared, which is null in the
//      mocked host, so even the search transition cannot complete.
//
// Both dependencies are real-Scene3D concerns (the install-gated [GameFact] path in P9 exercises
// them live). This test makes the gap explicit and loud rather than silent, following the project's
// disclosed-gap convention. The verified terminals remain: the dump transition via
// SupplyAIUpdate.TickSupplyState (SupplyCycleGapTests) and the BankAccount deposit/debit path
// (the P4 scenario matrix).
//
// See docs/roadmap/deep-dives/p4-p6-economy-production-combat.md P4 and
// docs/roadmap/deep-dives/p8-implementation-delta-remediation.md P8-4.

using Panopticon.Logic;
using Panopticon.Logic.Object;
using Xunit;

namespace Panopticon.Tests.Logic.Economy;

public class RealGatherCycleTests : MockedGameTest
{
    [Fact, Trait("Category", "Roadmap")]
    public void FullGatherCycle_HeadlessHost_IsExplicitLocomotionAndPartitionGap()
    {
        // The mocked host exposes no PartitionCellManager and no Locomotor tick. Driving the full
        // gather cycle through SupplyAIUpdate.TickSupplyState therefore cannot progress past the
        // search/approach transitions. This is a named gap, not a silent pass: it surfaces the
        // exact technical blocker so a future headless Scene3D/PartitionCellManager seam forces an
        // update here, and so the live half (P9's install-gated run) is the authority for the
        // full cycle.
        var partitionManagerPresent = Generals.PartitionCellManager != null;

        var gap = partitionManagerPresent
            ? "PartitionCellManager present; re-evaluate the locomotion-only blocker."
            : "Full SupplyAIUpdate gather cycle requires a Locomotor tick (approach arrival) and a "
              + "PartitionCellManager (FindClosestSupplyWarehouse distance), neither of which the "
              + "headless MockedGameTest host provides. Verified terminals: "
              + "SupplyCycleGapTests (dump transition via TickSupplyState) and the P4 matrix "
              + "(BankAccount deposit/debit). The live P9 run exercises the full cycle.";

        Assert.False(string.IsNullOrEmpty(gap));
        Assert.False(partitionManagerPresent,
            "PartitionCellManager is now present in the mocked host: the P8-4 gather-cycle gap " +
            "should be re-evaluated and this test updated to drive the real cycle.");
        Assert.Equal(ExpectedGap, gap);
    }

    private const string ExpectedGap =
        "Full SupplyAIUpdate gather cycle requires a Locomotor tick (approach arrival) and a "
        + "PartitionCellManager (FindClosestSupplyWarehouse distance), neither of which the "
        + "headless MockedGameTest host provides. Verified terminals: "
        + "SupplyCycleGapTests (dump transition via TickSupplyState) and the P4 matrix "
        + "(BankAccount deposit/debit). The live P9 run exercises the full cycle.";
}
