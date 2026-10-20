using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Tests;
using Xunit;

namespace Panopticon.Tests.Logic;

/// <summary>
/// WI-M5-05: kill and loss accounting per side and definition. A death credits the victim's
/// side one loss and the last damager's side one kill under the damager's definition;
/// unresolvable killers count under "unknown", projectile sources credit their creator,
/// structures are excluded, and each death counts once.
/// </summary>
public sealed class KillLossTrackerTests : MockedGameTest
{
    [Fact]
    public void Death_CreditsLossToVictimAndKillToShooterDefinition()
    {
        var (victimOwner, shooterOwner) = TwoOwners();
        var shooter = CreateUnit(shooterOwner, "KillLossShooter");
        var victim = CreateUnit(victimOwner, "KillLossVictim");
        using var tracker = new KillLossTracker(Generals.GameLogic);

        victim.BodyModule!.AttemptDamage(new DamageInfoInput(shooter) { Amount = 1000f });
        victim.OnDie(new DamageInfoInput(shooter));
        victim.Destroy();

        var loss = Assert.Single(tracker.Losses);
        Assert.Equal((victimOwner, "KillLossVictim"), loss.Key);
        Assert.Equal(1, loss.Value);
        var kill = Assert.Single(tracker.Kills);
        Assert.Equal((shooterOwner, "KillLossShooter"), kill.Key);
        Assert.Equal(1, kill.Value);
    }

    [Fact]
    public void DeathWithUnresolvedKiller_CountsUnderUnknown()
    {
        var (victimOwner, _) = TwoOwners();
        var victim = CreateUnit(victimOwner, "KillLossVictimNoKiller");
        using var tracker = new KillLossTracker(Generals.GameLogic);

        // No damage history: the last damage source is invalid, so the kill cannot resolve.
        victim.OnDie(new DamageInfoInput());

        var kill = Assert.Single(tracker.Kills);
        Assert.Equal((null, KillLossTracker.UnknownKillerDefinition), kill.Key);
        Assert.Equal(1, tracker.TotalKills);
        Assert.Equal(1, tracker.TotalLosses);
    }

    [Fact]
    public void ProjectileSource_CreditsItsCreator()
    {
        var (victimOwner, shooterOwner) = TwoOwners();
        var shooter = CreateUnit(shooterOwner, "KillLossProjectileShooter");
        var projectile = CreateUnit(shooterOwner, "KillLossProjectile", ObjectKinds.Projectile);
        projectile.CreatedByObjectID = shooter.Id;
        var victim = CreateUnit(victimOwner, "KillLossProjectileVictim");
        using var tracker = new KillLossTracker(Generals.GameLogic);

        victim.BodyModule!.AttemptDamage(new DamageInfoInput(projectile) { Amount = 1000f });
        victim.OnDie(new DamageInfoInput(projectile));

        var kill = Assert.Single(tracker.Kills);
        Assert.Equal((shooterOwner, "KillLossProjectileShooter"), kill.Key);
    }

    [Fact]
    public void StructuresAreExcluded_AndDeathsCountOnce()
    {
        var (victimOwner, shooterOwner) = TwoOwners();
        var shooter = CreateUnit(shooterOwner, "KillLossShooter");
        var structure = CreateUnit(victimOwner, "KillLossAirfield", ObjectKinds.Structure);
        var victim = CreateUnit(victimOwner, "KillLossVictim");
        using var tracker = new KillLossTracker(Generals.GameLogic);

        structure.OnDie(new DamageInfoInput(shooter));
        victim.BodyModule!.AttemptDamage(new DamageInfoInput(shooter) { Amount = 1000f });
        victim.OnDie(new DamageInfoInput(shooter));
        victim.OnDie(new DamageInfoInput(shooter));
        victim.Destroy();

        Assert.Equal(1, tracker.TotalLosses);
        Assert.Equal(1, tracker.TotalKills);
        Assert.All(tracker.Losses.Keys, key => Assert.Equal("KillLossVictim", key.Definition));
    }

    private (Player VictimOwner, Player ShooterOwner) TwoOwners()
    {
        var victimOwner = new Player(1, null, default, Generals);
        var shooterOwner = new Player(2, null, default, Generals);
        return (victimOwner, shooterOwner);
    }

    private GameObject CreateUnit(Player owner, string name, ObjectKinds kind = ObjectKinds.Vehicle)
    {
        var definition = new ObjectDefinition(name);
        definition.KindOf.Set(kind, true);
        definition.KindOf.Set(ObjectKinds.CanAttack, true);
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = 100f, InitialHealth = 100f },
                ModuleInheritanceMode.Default, inherited: false));

        var gameObject = Generals.GameLogic.CreateObject(definition, owner);
        gameObject.Owner = owner;
        return gameObject;
    }
}
