using Panopticon.Network;
using Xunit;

namespace Panopticon.Tests.Network;

public class SkirmishSlotTests
{
    [Fact]
    public void DefaultTeam_IsNoTeamSentinel()
    {
        // -1 is the "no team" sentinel that PlayerManager.OnNewGame checks for. A freshly created
        // slot that nobody has assigned a team to must default to it, so that two unedited AI
        // opponents are wired as enemies rather than allies.
        var slot = new SkirmishSlot(0);

        Assert.Equal(-1, slot.Team);
    }
}
