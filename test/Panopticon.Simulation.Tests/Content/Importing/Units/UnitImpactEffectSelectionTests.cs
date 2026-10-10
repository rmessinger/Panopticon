using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Content;
using Panopticon.Content.Importing.Units;
using Panopticon.Content.Importing.Units.Materialization;
using Panopticon.FX;
using Panopticon.Logic.Object;
using Xunit;
using static Panopticon.Tests.Content.Importing.Units.MaterializationFixtures;
using static Panopticon.Tests.Content.Importing.Units.UnitRecordFixtures;

namespace Panopticon.Tests.Content.Importing.Units;

public class UnitImpactEffectSelectionTests : MockedGameTest
{
    private sealed class EffectRecorder : FXNugget
    {
        public int Count { get; private set; }

        internal override void Execute(FXListExecutionContext context) => Count++;
    }

    private static MaterializedUnitContent Prepare(UnitContentSet set, string subject, string damageType)
    {
        var resolution = UnitImportResolution.Resolve(set, Registry);
        Assert.True(resolution.Succeeded);
        return UnitRuntimeMaterializer.Materialize(resolution, new UnitMaterializationContext(
            Policy, impactEffectDamageTypes: new Dictionary<ContentId, string> { [Id(ContentKind.Unit, subject)] = damageType }));
    }

    private static DamageNugget RifleNugget(MaterializedUnitContent content)
    {
        return Assert.Single(content.Get<WeaponTemplate>(Id(ContentKind.Weapon, "Rifle")).Nuggets.OfType<DamageNugget>());
    }

    [Fact]
    public void OptInStampsOverrideWithoutChangingDamage()
    {
        var content = Prepare(ValidSoldier(), "Soldier", "ARMOR_PIERCING");
        var nugget = RifleNugget(content);

        Assert.True(content.Succeeded);
        Assert.Equal(DamageType.ArmorPiercing, nugget.DamageFXOverride);
        Assert.Equal(DamageType.Explosion, nugget.DamageType);
        Assert.Equal(10f, nugget.Damage);
    }

    [Fact]
    public void AbsenceKeepsNoOverrideSentinel()
    {
        Assert.Equal(DamageType.Unresistable, RifleNugget(Materialize(ValidSoldier())).DamageFXOverride);
        Assert.Equal(DamageType.Unresistable, new DamageNugget().DamageFXOverride);
    }

    [Theory]
    [InlineData("Soldier", "UNRESISTABLE")]
    [InlineData("Soldier", "PIERCE_MAGIC")]
    [InlineData("Unknown", "ARMOR_PIERCING")]
    public void InvalidMappingPreventsStaging(string subject, string damageType)
    {
        var content = Prepare(ValidSoldier(), subject, damageType);

        Assert.False(content.Succeeded);
        Assert.Empty(content.Assets);
        Assert.Contains(content.Diagnostics, d => d.IsError && d.Message.Contains(subject) && d.Message.Contains(damageType));
    }

    [Fact]
    public void UnitWithoutWeaponCannotOptIn()
    {
        var content = Prepare(UnitDamageEffectPolicyTests.TankContent(), "Tank", "ARMOR_PIERCING");

        Assert.False(content.Succeeded);
        Assert.Contains(content.Diagnostics, d => d.Message.Contains("Tank") && d.Message.Contains("weapon"));
    }

    [Fact]
    public void SharedWeaponIsRejected()
    {
        var other = Unit("Sniper", "Soldier");
        var set = ValidSoldier(other);
        var shared = new UnitContentSet(Source, set.Records, [Id(ContentKind.Unit, "Soldier"), other.Id]);

        var content = Prepare(shared, "Soldier", "ARMOR_PIERCING");

        Assert.False(content.Succeeded);
        Assert.Contains(content.Diagnostics, d => d.Message.Contains("Rifle") && d.Message.Contains("shared"));
    }

    [Fact]
    public void OverrideSelectsTargetEffectWithoutChangingCombatResults()
    {
        var overridden = Run(DamageType.ArmorPiercing);
        var plain = Run(DamageType.Unresistable);

        Assert.Equal(plain.Health, overridden.Health);
        Assert.True(overridden.Dead);
        Assert.Equal(plain.Dead, overridden.Dead);
        Assert.True(overridden.Effects > 0);
        Assert.Equal(0, plain.Effects);
    }

    private static (List<float> Health, bool Dead, int Effects) Run(DamageType damageFXOverride)
    {
        var game = new TestGame(SageGame.CncGenerals);
        var recorder = new EffectRecorder();
        var fxList = new FXList();
        fxList.Nuggets.Add(recorder);

        // Mirrors InfantryDamageFX: UNRESISTABLE plays nothing, the overridden type plays an effect.
        var effect = new DamageFX();
        var group = effect.Groups[(int)DamageType.ArmorPiercing, (int)VeterancyLevel.Regular];
        group.MajorFX = new LazyAssetReference<FXList>(fxList);
        group.MinorFX = group.MajorFX;

        var resolution = UnitImportResolution.Resolve(UnitDamageEffectPolicyTests.TankContent(), Registry);
        var content = UnitRuntimeMaterializer.Materialize(resolution, new UnitMaterializationContext(
            Policy, damageEffects: new Dictionary<ContentId, string> { [Id(ContentKind.Unit, "Tank")] = "InfantryLikeDamageFX" },
            damageEffectLookup: _ => effect));
        Assert.True(content.Succeeded);

        var victim = game.GameLogic.CreateObject(content.Get<ObjectDefinition>(Id(ContentKind.Unit, "Tank")), null!);
        victim.UpdateTransform(new Vector3(60, 20, 0), Quaternion.Identity);
        var attacker = game.GameLogic.CreateObject(new ObjectDefinition("Shooter"), null!);
        attacker.UpdateTransform(new Vector3(10, 20, 0), Quaternion.Identity);
        var template = new WeaponTemplate { AttackRange = 100 };
        var nugget = new DamageNugget { Damage = 30, DamageType = DamageType.Unresistable, DeathType = DeathType.Normal, DamageFXOverride = damageFXOverride };
        template.Nuggets.Add(nugget);
        var weapon = new Weapon(attacker, template, WeaponSlot.Primary, game.GameEngine);
        weapon.SetTarget(new WeaponTarget(game.GameLogic, victim.Id));

        var health = new List<float>();
        for (var frame = 0; frame < 30 && victim.BodyModule.Health > 0; frame++)
        {
            game.GameLogic.Update();
            nugget.Execute(new WeaponEffectExecutionContext(weapon, game.GameEngine));
            health.Add(victim.BodyModule.Health);
        }

        return (health, victim.IsEffectivelyDead, recorder.Count);
    }
}
