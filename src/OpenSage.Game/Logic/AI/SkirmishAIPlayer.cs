using System;
using System.Collections.Generic;
using System.Linq;
using OpenSage.Gui.ControlBar;
using OpenSage.Logic.Map;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;

namespace OpenSage.Logic.AI;

public sealed class SkirmishAIPlayer : AIPlayer
{
    private const int MaxEconomyAssignmentsPerDecision = 2;
    internal const int DefaultTargetArmySize = 3;

    private int _unknownInt1;
    private int _unknownInt2;
    private float _unknownFloat1;
    private float _unknownFloat2;

    internal int DecisionTickCount { get; private set; }
    private bool _loggedMissingBuildListWarning;

    internal SkirmishAIPlayer(Player owner)
        : base(owner)
    {

    }

    protected override void OnDecisionTick()
    {
        DecisionTickCount++;

        RunEconomyAndBaseBuildout();
        RunUnitProduction();
    }

    private void RunEconomyAndBaseBuildout()
    {
        var aiData = Owner.Game.AssetStore.AIData.Current;
        var buildList = SelectBuildList(aiData, Owner.Side);

        if (buildList == null)
        {
            if (!_loggedMissingBuildListWarning)
            {
                _loggedMissingBuildListWarning = true;
                NLog.LogManager.GetCurrentClassLogger().Warn(
                    "Skirmish AI player '{0}' has no matching build list for side '{1}'.",
                    Owner.Name ?? $"Player{Owner.Id}",
                    Owner.Side ?? "<null>");
            }

            return;
        }

        if (TryIssueSupplyPriorityBuild(buildList))
        {
            return;
        }

        if (TryIssueBuildListStructure(buildList))
        {
            return;
        }

        IssueGatherOrders(buildList);
    }

    private static AISkirmishBuildList? SelectBuildList(AIData aiData, string? sideName)
    {
        if (aiData.SkirmishBuildLists.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(sideName))
        {
            return aiData.SkirmishBuildLists[0];
        }

        var side = sideName.Trim();
        var sideWithoutPrefix = side.StartsWith("Faction", StringComparison.OrdinalIgnoreCase)
            ? side["Faction".Length..]
            : side;

        foreach (var candidate in aiData.SkirmishBuildLists)
        {
            if (string.Equals(candidate.Name, side, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        foreach (var candidate in aiData.SkirmishBuildLists)
        {
            if (candidate.Name.Contains(side, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        foreach (var candidate in aiData.SkirmishBuildLists)
        {
            if (candidate.Name.Contains(sideWithoutPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private bool TryIssueSupplyPriorityBuild(AISkirmishBuildList buildList)
    {
        foreach (var structure in OrderBuildListStructures(buildList.Structures))
        {
            if (!structure.IsSupplyBuilding)
            {
                continue;
            }

            if (HasStructureOrConstruction(structure))
            {
                return false;
            }

            if (TryIssueBuildOrder(structure))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryIssueBuildListStructure(AISkirmishBuildList buildList)
    {
        foreach (var structure in OrderBuildListStructures(buildList.Structures))
        {
            if (HasStructureOrConstruction(structure))
            {
                continue;
            }

            if (TryIssueBuildOrder(structure))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<BuildListInfo> OrderBuildListStructures(IReadOnlyList<BuildListInfo> structures)
    {
        return structures
            .Select((value, index) => (value, index))
            .Where(item => item.value.IsBuildable)
            .OrderByDescending(item => item.value.PriorityBuild)
            .ThenBy(item => item.index)
            .Select(item => item.value);
    }

    private bool HasStructureOrConstruction(BuildListInfo structure)
    {
        var definition = ResolveObjectDefinition(structure);
        if (definition == null)
        {
            return false;
        }

        foreach (var ownedObject in Owner.OwnedObjects)
        {
            if (ownedObject.Definition != definition)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private bool TryIssueBuildOrder(BuildListInfo structure)
    {
        var definition = ResolveObjectDefinition(structure);
        if (definition == null)
        {
            return false;
        }

        if (!definition.KindOf.Get(ObjectKinds.Structure))
        {
            return false;
        }

        if (!Owner.CanProduceObject(definition))
        {
            return false;
        }

        if (Owner.BankAccount.Money < definition.BuildCost)
        {
            return false;
        }

        var order = Order.CreateBuildObject(
            OwnerPlayerIndex,
            (int)definition.InstanceId,
            structure.Location,
            structure.Angle);

        EnqueueOrder(order);
        return true;
    }

    private void IssueGatherOrders(AISkirmishBuildList buildList)
    {
        var supplyPoint = FindPreferredSupplyPoint(buildList);
        if (supplyPoint == null)
        {
            return;
        }

        var assignments = 0;

        foreach (var unit in Owner.OwnedObjects)
        {
            if (unit.IsStructure || !unit.Definition.KindOf.Get(ObjectKinds.Harvester))
            {
                continue;
            }

            var supplyAIUpdate = unit.FindBehavior<SupplyAIUpdate>();
            if (supplyAIUpdate == null)
            {
                continue;
            }

            var isAlreadyAssignedToSupplyWork =
                supplyAIUpdate.CurrentSupplySource != null
                || supplyAIUpdate.CurrentSupplyTarget != null;
            if (isAlreadyAssignedToSupplyWork)
            {
                continue;
            }

            EnqueueOrders(
            [
                Order.CreateSetSelection(OwnerPlayerIndex, unit.Id),
                Order.CreateSupplyGatherDump(OwnerPlayerIndex, supplyPoint.Id),
            ]);

            assignments++;
            if (assignments >= MaxEconomyAssignmentsPerDecision)
            {
                return;
            }
        }
    }

    private GameObject? FindPreferredSupplyPoint(AISkirmishBuildList buildList)
    {
        foreach (var objectInWorld in Owner.Game.GameLogic.Objects)
        {
            if (objectInWorld.Definition.KindOf.Get(ObjectKinds.SupplySource))
            {
                return objectInWorld;
            }
        }

        foreach (var structure in buildList.Structures)
        {
            if (!structure.IsSupplyBuilding)
            {
                continue;
            }

            var definition = ResolveObjectDefinition(structure);
            if (definition == null)
            {
                continue;
            }

            var ownedSupply = Owner.OwnedObjects.FirstOrDefault(x => x.Definition == definition);
            if (ownedSupply != null)
            {
                return ownedSupply;
            }
        }

        return Owner.OwnedObjects.FirstOrDefault(
            x => x.IsStructure && x.Definition.KindOf.Get(ObjectKinds.SupplyGatheringCenter));
    }

    private ObjectDefinition? ResolveObjectDefinition(BuildListInfo structure)
    {
        if (!string.IsNullOrWhiteSpace(structure.TemplateName))
        {
            var byTemplateName = Owner.Game.AssetStore.ObjectDefinitions.GetByName(structure.TemplateName);
            if (byTemplateName != null)
            {
                return byTemplateName;
            }
        }

        if (!string.IsNullOrWhiteSpace(structure.BuildingName))
        {
            var byBuildingName = Owner.Game.AssetStore.ObjectDefinitions.GetByName(structure.BuildingName);
            if (byBuildingName != null)
            {
                return byBuildingName;
            }
        }

        return null;
    }

    private void RunUnitProduction()
    {
        var attackCapableArmyCount = Owner.OwnedObjects.Count(
            x =>
                !x.IsStructure
                && x.CanAttack
                && !x.Definition.KindOf.Get(ObjectKinds.Harvester)
                && !x.Definition.KindOf.Get(ObjectKinds.Dozer));
        if (attackCapableArmyCount >= DefaultTargetArmySize)
        {
            return;
        }

        foreach (var producer in Owner.OwnedObjects)
        {
            if (!producer.IsStructure)
            {
                continue;
            }

            var commandSet = producer.Definition.CommandSet?.Value;
            if (commandSet == null)
            {
                continue;
            }

            var candidateUnit = commandSet.Buttons.Values
                .Select(x => x?.Value)
                .Where(x => x?.Command == CommandType.UnitBuild && x.Object?.Value != null)
                .Select(x => x!.Object!.Value)
                .OrderBy(x => x.Name, StringComparer.Ordinal)
                .FirstOrDefault(CanQueueUnitProduction);
            if (candidateUnit == null)
            {
                continue;
            }

            var createUnitOrder = new Order(OwnerPlayerIndex, OrderType.CreateUnit);
            createUnitOrder.AddIntegerArgument((int)candidateUnit.InstanceId);
            EnqueueOrder(createUnitOrder);
            return;
        }
    }

    private bool CanQueueUnitProduction(ObjectDefinition unitDefinition)
    {
        if (unitDefinition.KindOf.Get(ObjectKinds.Structure))
        {
            return false;
        }

        if (unitDefinition.KindOf.Get(ObjectKinds.Harvester)
            || unitDefinition.KindOf.Get(ObjectKinds.Dozer))
        {
            return false;
        }

        if (!unitDefinition.KindOf.Get(ObjectKinds.CanAttack))
        {
            return false;
        }

        if (Owner.BankAccount.Money < unitDefinition.BuildCost)
        {
            return false;
        }

        if (!Owner.CanProduceObject(unitDefinition))
        {
            return false;
        }

        if (CountOwnedAndQueuedByDefinition(unitDefinition) >= DefaultTargetArmySize)
        {
            return false;
        }

        return true;
    }

    private int CountOwnedAndQueuedByDefinition(ObjectDefinition unitDefinition)
    {
        var ownedCount = Owner.OwnedObjects.Count(
            x => !x.IsStructure && x.Definition == unitDefinition);
        var queuedCount = Owner.OwnedObjects
            .Where(x => x.ProductionUpdate != null)
            .SelectMany(x => x.ProductionUpdate!.ProductionQueue)
            .Count(x => x.ObjectDefinition == unitDefinition);

        return ownedCount + queuedCount;
    }

    public override void Persist(StatePersister reader)
    {
        reader.PersistVersion(1);

        reader.BeginObject("Base");
        base.Persist(reader);
        reader.EndObject();

        reader.PersistInt32(ref _unknownInt1);
        reader.PersistInt32(ref _unknownInt2);
        reader.PersistSingle(ref _unknownFloat1);
        reader.PersistSingle(ref _unknownFloat2);

        reader.SkipUnknownBytes(16);
    }
}
