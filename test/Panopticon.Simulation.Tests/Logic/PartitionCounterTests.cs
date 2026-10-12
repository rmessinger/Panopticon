// WI-B4-01: partition update counters.
// Pins the semantics of PartitionCellManager's cumulative counters (dirty objects rebuilt,
// coarse candidate pairs, exact hits) on a game-free manager grid, including the duplicate
// candidate emissions the pre-WI-B4-02 update produces for pairs sharing more than one cell.
// WI-B4-02 updates the duplicate expectations when pair delivery becomes unique.
//
// Run with: dotnet test src --filter "FullyQualifiedName~PartitionCounterTests"

using System.Numerics;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Xunit;

namespace Panopticon.Tests.Logic;

public class PartitionCounterTests : MockedGameTest
{
    private const float CellSize = 10.0f;

    [Fact, Trait("Category", "Roadmap")]
    public void UpdateCountsDirtyObjectsCandidatesAndExactHits()
    {
        var manager = NewManager();
        // Two spheres spanning cells (0,0) and (1,0): each dirty object pairs with the other
        // once per shared cell, so the pre-pair-once update emits the pair twice per direction.
        var first = NewObject(ObjectKinds.Infantry, new Vector3(10, 5, 0), radius: 4);
        var second = NewObject(ObjectKinds.Infantry, new Vector3(11, 5, 0), radius: 4);

        var dirtyBefore = PartitionCellManager.DirtyObjectsProcessed;
        var candidatesBefore = PartitionCellManager.CandidatePairs;
        var hitsBefore = PartitionCellManager.ExactHits;

        manager.OnObjectAdded(first);
        manager.OnObjectAdded(second);
        manager.Update();

        Assert.Equal(2, PartitionCellManager.DirtyObjectsProcessed - dirtyBefore);
        Assert.Equal(4, PartitionCellManager.CandidatePairs - candidatesBefore);
        Assert.Equal(4, PartitionCellManager.ExactHits - hitsBefore);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void MovementMarksObjectsDirtyAndCountsAgain()
    {
        var manager = NewManager();
        var unit = NewObject(ObjectKinds.Infantry, new Vector3(5, 5, 0), radius: 2);

        manager.OnObjectAdded(unit);
        manager.Update();

        var dirtyBefore = PartitionCellManager.DirtyObjectsProcessed;
        var candidatesBefore = PartitionCellManager.CandidatePairs;

        unit.UpdateTransform(new Vector3(25, 5, 0));
        manager.Update();

        Assert.Equal(1, PartitionCellManager.DirtyObjectsProcessed - dirtyBefore);
        Assert.Equal(0, PartitionCellManager.CandidatePairs - candidatesBefore);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void ImmobileDirtyObjectsPairAsCellMatesOnly()
    {
        var manager = NewManager();
        // The immobile object never collects pairs itself; the mobile object's pass pairs them.
        var structure = NewObject(ObjectKinds.Immobile, new Vector3(5, 5, 0), radius: 4);
        var unit = NewObject(ObjectKinds.Infantry, new Vector3(6, 5, 0), radius: 4);

        var candidatesBefore = PartitionCellManager.CandidatePairs;
        var hitsBefore = PartitionCellManager.ExactHits;

        manager.OnObjectAdded(structure);
        manager.OnObjectAdded(unit);
        manager.Update();

        Assert.Equal(1, PartitionCellManager.CandidatePairs - candidatesBefore);
        Assert.Equal(1, PartitionCellManager.ExactHits - hitsBefore);
    }

    private PartitionCellManager NewManager() => new(CellSize, numCellsX: 4, numCellsY: 4);

    private GameObject NewObject(ObjectKinds kind, Vector3 position, float radius)
    {
        var definition = new ObjectDefinition($"PartitionCounter_{kind}_{radius}");
        definition.KindOf.Set(kind, true);
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = 100, InitialHealth = 100 },
                ModuleInheritanceMode.Default, inherited: false));
        var gameObject = new GameObject(definition, Generals.GameEngine, NewPlayer())
        {
            Id = new ObjectId(_nextObjectId++),
        };
        gameObject.Geometry.Shapes[0].MajorRadius = radius;
        gameObject.Geometry.Shapes[0].MinorRadius = radius;
        gameObject.UpdateTransform(position);
        return gameObject;
    }

    private Player NewPlayer() => new(1, null, default, Generals);

    private uint _nextObjectId = 1;
}
