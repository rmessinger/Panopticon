using OpenSage.Logic.Object;
using Xunit;

namespace OpenSage.Tests.Logic;

// Generals' FLESHY_SNIPER and Zero Hour's SUBDUAL_MISSILE share enum value 31 (the original engine
// reused the slot when ZH replaced the sniper type). Value-based classification must therefore be
// game-aware: in Generals, value 31 is an ordinary health-damaging damage type; from Zero Hour
// onward it is subdual. Before this was fixed, every Generals sniper hit (Pathfinder, Jarmen Kell)
// was classified as subdual and silently dropped by bodies that cannot be subdued.

public class DamageTypeCollisionTests
{
    [Fact]
    public void FleshySniper_And_SubdualMissile_ShareValue31()
    {
        Assert.Equal((int)DamageType.FleshySniper, 31);
        Assert.Equal((int)DamageType.SubdualMissile, 31);
    }

    [Theory]
    [InlineData(SageGame.CncGenerals, false)]
    [InlineData(SageGame.CncGeneralsZeroHour, true)]
    [InlineData(SageGame.Bfme, true)]
    public void Value31_IsSubdual_OnlyFromZeroHourOnward(SageGame game, bool expected)
    {
        Assert.Equal(expected, DamageType.SubdualMissile.IsSubdualDamage(game));
        Assert.Equal(expected, DamageType.FleshySniper.IsSubdualDamage(game));
    }

    [Theory]
    [InlineData(SageGame.CncGenerals, true)]
    [InlineData(SageGame.CncGeneralsZeroHour, false)]
    public void Value31_DamagesHealth_OnlyInGenerals(SageGame game, bool expected)
    {
        Assert.Equal(expected, DamageType.FleshySniper.IsHealthDamagingDamage(game));
    }

    [Fact]
    public void OtherSubdualTypes_AreSubdualInAllGames()
    {
        foreach (var game in new[] { SageGame.CncGenerals, SageGame.CncGeneralsZeroHour, SageGame.Bfme })
        {
            Assert.True(DamageType.SubdualVehicle.IsSubdualDamage(game));
            Assert.True(DamageType.SubdualBuilding.IsSubdualDamage(game));
            Assert.True(DamageType.SubdualUnresistable.IsSubdualDamage(game));
        }
    }
}
