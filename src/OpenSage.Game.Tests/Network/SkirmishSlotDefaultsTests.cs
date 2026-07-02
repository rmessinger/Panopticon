using System.Linq;
using OpenSage.Network;
using Xunit;

namespace OpenSage.Tests.Network;

public class SkirmishSlotDefaultsTests : MockedGameTest
{
    [Fact]
    public void ApplyDefaults_DoesNotReassignAlreadyTakenStartPositions()
    {
        var slots = new[]
        {
            new SkirmishSlot(0) { State = SkirmishSlotState.Human, StartPosition = 2 },
            new SkirmishSlot(1) { State = SkirmishSlotState.EasyArmy },
            new SkirmishSlot(2) { State = SkirmishSlotState.EasyArmy },
            new SkirmishSlot(3) { State = SkirmishSlotState.EasyArmy },
        };

        SkirmishSlotDefaults.ApplyDefaults(Generals, slots, mapName: null, seed: 1);

        // The manually-assigned start position must survive untouched.
        Assert.Equal(2, slots[0].StartPosition);

        // No two slots may end up sharing the same non-zero start position.
        var assignedPositions = slots.Select(s => s.StartPosition).Where(p => p != 0).ToList();
        Assert.Equal(assignedPositions.Count, assignedPositions.Distinct().Count());

        // Every slot that needed a position got one (4 slots, 4 available positions).
        Assert.All(slots, slot => Assert.NotEqual(0, slot.StartPosition));
    }

    [Fact]
    public void ApplyDefaults_SkipsOpenAndClosedSlots()
    {
        var slots = new[]
        {
            new SkirmishSlot(0) { State = SkirmishSlotState.Open },
            new SkirmishSlot(1) { State = SkirmishSlotState.Closed },
            new SkirmishSlot(2) { State = SkirmishSlotState.EasyArmy },
        };

        SkirmishSlotDefaults.ApplyDefaults(Generals, slots, mapName: null, seed: 1);

        Assert.Equal(0, slots[0].StartPosition);
        Assert.Equal(0, slots[1].StartPosition);
        Assert.NotEqual(0, slots[2].StartPosition);
    }
}
