// WI-P6-03: Death cleanup.
// Verifies dying and destroyed objects leave no dangling references: both the OnDie path
// (combat death) and the Destroy path (Sell/CancelBuild/teardown) remove the object from the
// owner's selection set and OwnedObjects (and keep BuildingCount/UnitCount correct). Both
// paths reuse the existing lifecycle seams (GameObject.OnDie deselect+RemoveOwnedObject,
// GameLogic.DestroyObject deselect+RemoveOwnedObject) that share the GameObjectLifecycleChanged
// notification VictorySystem already subscribes to; no parallel death event is introduced.
//
// See docs/roadmap/deep-dives/p4-p6-economy-production-combat.md P6-3, and
// docs/roadmap/deep-dives/p3-victory-defeat.md §1.3 (shared death->defeat plumbing).
//
// Run with: dotnet test src --filter "FullyQualifiedName~DeathCleanupTests"

using System.Collections.Generic;
using System.Reflection;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using Xunit;

namespace OpenSage.Tests.Logic.Object;

public class DeathCleanupTests : MockedGameTest
{
    [Fact, Trait("Category", "Roadmap")]
    public void OnDie_DyingSelectedUnit_RemovedFromSelectionAndOwnership()
    {
        var owner = NewPlayer();
        var victim = NewUnit(owner, ObjectKinds.Infantry);
        var bystander = NewUnit(owner, ObjectKinds.Infantry);
        MarkSelected(owner, victim, bystander);

        Assert.Equal(2, owner.UnitCount);
        Assert.Contains(victim, owner.SelectedUnits);

        // Combat death path: Kill -> AttemptDamage -> OnDie.
        victim.Kill();

        Assert.DoesNotContain(victim, owner.SelectedUnits);
        Assert.DoesNotContain(victim, owner.OwnedObjects);
        Assert.False(victim.IsSelectable);
        // Bystander is untouched, counts dropped by exactly one.
        Assert.Contains(bystander, owner.SelectedUnits);
        Assert.Contains(bystander, owner.OwnedObjects);
        Assert.Equal(1, owner.UnitCount);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void Destroy_DestroyedSelectedUnit_RemovedFromSelectionAndOwnership()
    {
        var owner = NewPlayer();
        var victim = NewUnit(owner, ObjectKinds.Vehicle);
        var bystander = NewUnit(owner, ObjectKinds.Vehicle);
        MarkSelected(owner, victim, bystander);

        Assert.Equal(2, owner.UnitCount);
        Assert.Contains(victim, owner.SelectedUnits);

        // Teardown path: Destroy -> GameLogic.DestroyObject. This path does NOT go through OnDie
        // (e.g. Sell / CancelBuild / scenario cleanup), so cleanup must be wired here too.
        victim.Destroy();

        Assert.DoesNotContain(victim, owner.SelectedUnits);
        Assert.DoesNotContain(victim, owner.OwnedObjects);
        Assert.Contains(bystander, owner.SelectedUnits);
        Assert.Contains(bystander, owner.OwnedObjects);
        Assert.Equal(1, owner.UnitCount);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void Counts_RemainConsistentAndNeverNegative_AcrossBothPaths()
    {
        var owner = NewPlayer();

        // Two buildings + three units, removed through a mix of OnDie and Destroy.
        var buildingA = NewUnit(owner, ObjectKinds.Structure);
        var buildingB = NewUnit(owner, ObjectKinds.Structure);
        var unitA = NewUnit(owner, ObjectKinds.Infantry);
        var unitB = NewUnit(owner, ObjectKinds.Infantry);
        var unitC = NewUnit(owner, ObjectKinds.Infantry);

        Assert.Equal(2, owner.BuildingCount);
        Assert.Equal(3, owner.UnitCount);
        Assert.Equal(5, owner.OwnedObjects.Count);

        buildingA.Destroy();            // Destroy path
        unitA.Kill();                   // OnDie path
        unitB.Destroy();                // Destroy path

        AssertCountsConsistent(owner);
        Assert.Equal(1, owner.BuildingCount);
        Assert.Equal(1, owner.UnitCount);

        // Removing already-removed objects (idempotency) must not drive counts negative.
        buildingA.Destroy();
        unitA.Kill();

        AssertCountsConsistent(owner);
        Assert.Equal(1, owner.BuildingCount);
        Assert.Equal(1, owner.UnitCount);
        Assert.True(owner.BuildingCount >= 0);
        Assert.True(owner.UnitCount >= 0);
    }

    private static void AssertCountsConsistent(Player owner)
    {
        var buildings = 0;
        var units = 0;
        foreach (var obj in owner.OwnedObjects)
        {
            if (obj.IsStructure)
            {
                buildings++;
            }
            else
            {
                units++;
            }
        }

        Assert.Equal(buildings, owner.BuildingCount);
        Assert.Equal(units, owner.UnitCount);
    }

    private Player NewPlayer() => new(1, null, default, Generals);

    private GameObject NewUnit(Player owner, ObjectKinds kind)
    {
        var definition = new ObjectDefinition($"P6DeathCleanup_{kind}_{_nextObjectId}");
        definition.KindOf.Set(kind, true);
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = 100f, InitialHealth = 100f },
                ModuleInheritanceMode.Default, inherited: false));
        var gameObject = new GameObject(definition, Generals.GameEngine, owner);
        // Register in GameLogic._objects so Kill's damage SourceID resolves to null instead of
        // throwing (CreateObject cannot run headlessly). See CombatCoverageTests for the same seam.
        gameObject.Id = new ObjectId(_nextObjectId++);
        owner.AddOwnedObject(gameObject);
        return gameObject;
    }

    // Player.SelectUnits touches Drawable.TriggerSelection, which is null in the mocked host, so
    // inject directly into the backing set — the same approach as SpendDebitTests.MarkSelected.
    private static void MarkSelected(Player owner, params GameObject[] units)
    {
        var field = typeof(Player).GetField("_selectedUnits", BindingFlags.Instance | BindingFlags.NonPublic);
        var set = (HashSet<GameObject>)field!.GetValue(owner)!;
        set.UnionWith(units);
    }

    private uint _nextObjectId = 1;
}
