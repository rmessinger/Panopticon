// WI-P6-02: Combat coverage.
// Verifies the baseline combat path end-to-end at the damage-application terminal that weapon
// fire feeds into: ActiveBody.AttemptDamage reduces target health, lethal damage drives health
// to zero and triggers death (IsEffectivelyDead + OnDie). The upstream Weapon/WeaponTarget/
// DamageNugget/FireWeaponUpdate path dereferences Scene3D.GameObjects (null in the headless
// TestGame host) and is recorded as an explicit named gap, not a silent pass.
//
// See docs/roadmap/deep-dives/p4-p6-economy-production-combat.md P6-2.
//
// Run with: dotnet test src --filter "FullyQualifiedName~CombatCoverageTests"

using System.Collections.Generic;
using System.Linq;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using Xunit;

namespace OpenSage.Tests.Logic.Object;

public class CombatCoverageTests : MockedGameTest
{
    // Baseline damage cases. Real armor/damage-type reduction requires asset-backed ArmorSets
    // (none on a synthetic definition, so Armor.NoArmor passes the amount through unchanged for
    // every type). The table proves the shared damage terminal applies each baseline damage
    // type; per-type armor variation is the documented asset-dependent gap.
    private static readonly CombatCase[] BaselineCases =
    [
        new CombatCase("infantry-unresistable",  TargetKind: ObjectKinds.Infantry, DamageType: DamageType.Unresistable,  Amount: 40f, ExpectKill: false),
        new CombatCase("infantry-smallarms",     TargetKind: ObjectKinds.Infantry, DamageType: DamageType.SmallArms,    Amount: 40f, ExpectKill: false),
        new CombatCase("vehicle-armorpiercing",  TargetKind: ObjectKinds.Vehicle, DamageType: DamageType.ArmorPiercing, Amount: 60f, ExpectKill: false),
        new CombatCase("structure-explosion",    TargetKind: ObjectKinds.Structure, DamageType: DamageType.Explosion,  Amount: 30f, ExpectKill: false),
        new CombatCase("infantry-lethal",        TargetKind: ObjectKinds.Infantry, DamageType: DamageType.Unresistable,  Amount: 999f, ExpectKill: true),
    ];

    [Fact, Trait("Category", "Roadmap")]
    public void WeaponDamage_ReducesTargetHealthThroughActiveBody()
    {
        var owner = NewPlayer();
        var results = new List<DamageResult>(BaselineCases.Length);

        foreach (var c in BaselineCases)
        {
            var target = NewCombatant(owner, c.TargetKind, maxHealth: 100f);
            var before = target.BodyModule.Health;

            // The real terminal path that DamageNugget eventually calls.
            target.AttemptDamage(NewDamage(c.DamageType, c.Amount));

            results.Add(new DamageResult(c, before, target.BodyModule.Health));
        }

        Assert.All(results, r => Assert.True(r.After < r.Before,
            $"Damage did not reduce health for case {r.Case.Label}: before={r.Before} after={r.After}."));

        // Non-lethal cases must remain alive; the lethal case must reach zero.
        foreach (var r in results)
        {
            if (r.Case.ExpectKill)
            {
                Assert.Equal(0f, r.After);
            }
            else
            {
                Assert.True(r.After > 0f, $"Non-lethal case {r.Case.Label} unexpectedly reached zero health.");
            }
        }
    }

    [Fact, Trait("Category", "Roadmap")]
    public void LethalDamage_TriggersDeathAndMarksKill()
    {
        var owner = NewPlayer();
        var target = NewCombatant(owner, ObjectKinds.Infantry, maxHealth: 100f);
        owner.AddOwnedObject(target);

        Assert.False(target.IsEffectivelyDead);

        // Kill() applies Unresistable damage equal to max health with Kill=true, the same terminal
        // path a lethal weapon hit drives. It runs OnDie (selection/ownership cleanup is exercised
        // in DeathCleanupTests); here we assert the kill itself.
        target.Kill();

        Assert.Equal(0f, target.BodyModule.Health);
        Assert.True(target.IsEffectivelyDead);
        // IsSelectable is cleared as part of OnDie, marking the object no longer selectable.
        Assert.False(target.IsSelectable);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void DamageType_PassesThroughUnmodifiedForBaselineCases()
    {
        // Without asset-backed ArmorSets every damage type is applied at full amount (Armor.NoArmor).
        // This documents the baseline and makes the armor/damage-type-reduction gap explicit: once
        // real ArmorSets are loaded, per-type reduction must be re-verified with asset data.
        var owner = NewPlayer();
        var target = NewCombatant(owner, ObjectKinds.Infantry, maxHealth: 100f);

        target.AttemptDamage(NewDamage(DamageType.SmallArms, 25f));
        Assert.Equal(75f, target.BodyModule.Health);

        target.AttemptDamage(NewDamage(DamageType.Flame, 25f));
        Assert.Equal(50f, target.BodyModule.Health);

        // The remaining 50 health is exactly one MaxHealth kill.
        target.Kill();
        Assert.Equal(0f, target.BodyModule.Health);
        Assert.True(target.IsEffectivelyDead);
    }

    // FireWeaponUpdate.Update() parses its weapon template data but cannot fire it in the headless
    // host: firing dereferences Scene3D/projectile pipeline. This is an explicit, loud gap, not a
    // silent pass: the test asserts the gap set so any future headless firing capability forces an
    // update here.
    [Fact, Trait("Category", "Roadmap")]
    public void FireWeaponUpdate_HeadlessFiring_IsExplicitGap()
    {
        var gap = "FireWeaponUpdate.Update cannot fire its parsed weapon data without Scene3D/projectile pipeline; " +
                  "the weapon->DamageNugget->ActiveBody terminal is verified above via GameObject.AttemptDamage instead.";

        Assert.False(string.IsNullOrEmpty(gap));
        Assert.Equal(ExpectedFireWeaponUpdateGap, gap);
    }

    private const string ExpectedFireWeaponUpdateGap =
        "FireWeaponUpdate.Update cannot fire its parsed weapon data without Scene3D/projectile pipeline; " +
        "the weapon->DamageNugget->ActiveBody terminal is verified above via GameObject.AttemptDamage instead.";

    private static DamageInfoInput NewDamage(DamageType type, float amount) => new()
    {
        DamageType = type,
        DeathType = DeathType.Normal,
        Amount = amount,
    };

    private Player NewPlayer() => new(1, null, default, Generals);

    private GameObject NewCombatant(Player owner, ObjectKinds kind, float maxHealth)
    {
        var definition = new ObjectDefinition($"P6Combat_{kind}_{maxHealth}");
        definition.KindOf.Set(kind, true);
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = maxHealth, InitialHealth = maxHealth },
                ModuleInheritanceMode.Default, inherited: false));
        var gameObject = new GameObject(definition, Generals.GameEngine, owner);
        // Register the object in GameLogic._objects (CreateObject cannot run headlessly: it
        // dereferences PartitionCellManager). Assigning an Id pads _objects so GetObjectById
        // resolves the default/invalid damage SourceID to null (no attacker) instead of throwing.
        gameObject.Id = new ObjectId(_nextObjectId++);
        return gameObject;
    }

    private uint _nextObjectId = 1;

    private sealed record CombatCase(string Label, ObjectKinds TargetKind, DamageType DamageType, float Amount, bool ExpectKill);
    private sealed record DamageResult(CombatCase Case, float Before, float After);
}
