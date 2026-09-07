#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using OpenSage.Gui.ControlBar;
using OpenSage.Logic.Map;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;

namespace OpenSage.Logic.AI;

/// <summary>
/// The reference <see cref="IAIDecisionPolicy"/>: the rule-based skirmish state machine
/// extracted from <see cref="SkirmishAIPlayer"/> (P2), unchanged in behavior. Each decision
/// tick runs economy/base build-out, unit production, and military behavior, in that order.
/// </summary>
public sealed class RuleBasedAIDecisionPolicy : IAIDecisionPolicy
{
    internal const int MaxEconomyAssignmentsPerDecision = 2;
    internal const int DefaultTargetArmySize = 3;
    internal const int DefaultAttackForceThreshold = 3;

    private bool _loggedMissingBuildListWarning;

    public IReadOnlyList<Order> DecideNextOrders(AIDecisionContext context)
    {
        var orders = new List<Order>();

        RunEconomyAndBaseBuildout(context, orders);
        RunUnitProduction(context, orders);
        RunMilitaryBehavior(context, orders);

        return orders;
    }

    private void RunEconomyAndBaseBuildout(AIDecisionContext context, List<Order> orders)
    {
        var aiData = context.Owner.Game.AssetStore.AIData.Current;
        var buildList = SelectBuildList(aiData, context.Owner.Side);

        if (buildList == null)
        {
            if (!_loggedMissingBuildListWarning)
            {
                _loggedMissingBuildListWarning = true;
                NLog.LogManager.GetCurrentClassLogger().Warn(
                    "Skirmish AI player '{0}' has no matching build list for side '{1}'.",
                    context.Owner.Name ?? $"Player{context.Owner.Id}",
                    context.Owner.Side ?? "<null>");
            }

            return;
        }

        if (TryIssueSupplyPriorityBuild(context, buildList, orders))
        {
            return;
        }

        if (TryIssueBuildListStructure(context, buildList, orders))
        {
            return;
        }

        IssueGatherOrders(context, buildList, orders);
    }

    internal static AISkirmishBuildList? SelectBuildList(AIData aiData, string? sideName)
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

    private static bool TryIssueSupplyPriorityBuild(AIDecisionContext context, AISkirmishBuildList buildList, List<Order> orders)
    {
        foreach (var structure in OrderBuildListStructures(buildList.Structures))
        {
            if (!structure.IsSupplyBuilding)
            {
                continue;
            }

            if (HasStructureOrConstruction(context.Owner, structure))
            {
                return false;
            }

            var order = TryCreateBuildOrder(context, structure);
            if (order != null)
            {
                orders.Add(order);
                return true;
            }
        }

        return false;
    }

    private static bool TryIssueBuildListStructure(AIDecisionContext context, AISkirmishBuildList buildList, List<Order> orders)
    {
        foreach (var structure in OrderBuildListStructures(buildList.Structures))
        {
            if (HasStructureOrConstruction(context.Owner, structure))
            {
                continue;
            }

            var order = TryCreateBuildOrder(context, structure);
            if (order != null)
            {
                orders.Add(order);
                return true;
            }
        }

        return false;
    }

    internal static IEnumerable<BuildListInfo> OrderBuildListStructures(IReadOnlyList<BuildListInfo> structures)
    {
        return structures
            .Select((value, index) => (value, index))
            .Where(item => item.value.IsBuildable)
            .OrderByDescending(item => item.value.PriorityBuild)
            .ThenBy(item => item.index)
            .Select(item => item.value);
    }

    internal static bool HasStructureOrConstruction(Player owner, BuildListInfo structure)
    {
        var definition = ResolveObjectDefinition(owner.Game, structure);
        if (definition == null)
        {
            return false;
        }

        foreach (var ownedObject in owner.OwnedObjects)
        {
            if (ownedObject.Definition != definition)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    internal static Order? TryCreateBuildOrder(AIDecisionContext context, BuildListInfo structure)
    {
        var owner = context.Owner;
        var definition = ResolveObjectDefinition(owner.Game, structure);
        if (definition == null)
        {
            return null;
        }

        if (!definition.KindOf.Get(ObjectKinds.Structure))
        {
            return null;
        }

        if (!owner.CanProduceObject(definition))
        {
            return null;
        }

        if (owner.BankAccount.Money < definition.BuildCost)
        {
            return null;
        }

        return Order.CreateBuildObject(
            context.OwnerPlayerIndex,
            (int)definition.InstanceId,
            structure.Location,
            structure.Angle);
    }

    private static void IssueGatherOrders(AIDecisionContext context, AISkirmishBuildList buildList, List<Order> orders)
    {
        var owner = context.Owner;
        var supplyPoint = FindPreferredSupplyPoint(owner, buildList);
        if (supplyPoint == null)
        {
            return;
        }

        var assignments = 0;

        foreach (var unit in owner.OwnedObjects)
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

            orders.Add(Order.CreateSetSelection(context.OwnerPlayerIndex, unit.Id));
            orders.Add(Order.CreateSupplyGatherDump(context.OwnerPlayerIndex, supplyPoint.Id));

            assignments++;
            if (assignments >= MaxEconomyAssignmentsPerDecision)
            {
                return;
            }
        }
    }

    private static GameObject? FindPreferredSupplyPoint(Player owner, AISkirmishBuildList buildList)
    {
        foreach (var objectInWorld in owner.Game.GameLogic.Objects)
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

            var definition = ResolveObjectDefinition(owner.Game, structure);
            if (definition == null)
            {
                continue;
            }

            var ownedSupply = owner.OwnedObjects.FirstOrDefault(x => x.Definition == definition);
            if (ownedSupply != null)
            {
                return ownedSupply;
            }
        }

        return owner.OwnedObjects.FirstOrDefault(
            x => x.IsStructure && x.Definition.KindOf.Get(ObjectKinds.SupplyGatheringCenter));
    }

    internal static ObjectDefinition? ResolveObjectDefinition(IGame game, BuildListInfo structure)
    {
        if (!string.IsNullOrWhiteSpace(structure.TemplateName))
        {
            var byTemplateName = game.AssetStore.ObjectDefinitions.GetByName(structure.TemplateName);
            if (byTemplateName != null)
            {
                return byTemplateName;
            }
        }

        if (!string.IsNullOrWhiteSpace(structure.BuildingName))
        {
            var byBuildingName = game.AssetStore.ObjectDefinitions.GetByName(structure.BuildingName);
            if (byBuildingName != null)
            {
                return byBuildingName;
            }
        }

        return null;
    }

    private static void RunUnitProduction(AIDecisionContext context, List<Order> orders)
    {
        var owner = context.Owner;
        var attackCapableArmyCount = owner.OwnedObjects.Count(
            x =>
                !x.IsStructure
                && x.CanAttack
                && !x.Definition.KindOf.Get(ObjectKinds.Harvester)
                && !x.Definition.KindOf.Get(ObjectKinds.Dozer));
        if (attackCapableArmyCount >= DefaultTargetArmySize)
        {
            return;
        }

        var order = TryCreateUnitProductionOrder(owner, context.OwnerPlayerIndex);
        if (order != null)
        {
            orders.Add(order);
        }
    }

    internal static Order? TryCreateUnitProductionOrder(Player owner, int ownerPlayerIndex)
    {
        foreach (var producer in owner.OwnedObjects)
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
                .FirstOrDefault(x => CanQueueUnitProduction(owner, x));
            if (candidateUnit == null)
            {
                continue;
            }

            var createUnitOrder = new Order(ownerPlayerIndex, OrderType.CreateUnit);
            createUnitOrder.AddIntegerArgument((int)candidateUnit.InstanceId);
            return createUnitOrder;
        }

        return null;
    }

    private static bool CanQueueUnitProduction(Player owner, ObjectDefinition unitDefinition)
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

        if (owner.BankAccount.Money < unitDefinition.BuildCost)
        {
            return false;
        }

        if (!owner.CanProduceObject(unitDefinition))
        {
            return false;
        }

        if (CountOwnedAndQueuedByDefinition(owner, unitDefinition) >= DefaultTargetArmySize)
        {
            return false;
        }

        return true;
    }

    private static int CountOwnedAndQueuedByDefinition(Player owner, ObjectDefinition unitDefinition)
    {
        var ownedCount = owner.OwnedObjects.Count(
            x => !x.IsStructure && x.Definition == unitDefinition);
        var queuedCount = owner.OwnedObjects
            .Where(x => x.ProductionUpdate != null)
            .SelectMany(x => x.ProductionUpdate!.ProductionQueue)
            .Count(x => x.ObjectDefinition == unitDefinition);

        return ownedCount + queuedCount;
    }

    private static void RunMilitaryBehavior(AIDecisionContext context, List<Order> orders)
    {
        var owner = context.Owner;
        var force = TrySelectAttackForce(owner);
        if (force == null)
        {
            return;
        }

        var target = FindDefensiveTarget(owner) ?? FindOffensiveTargetStructure(owner);
        if (target != null)
        {
            orders.AddRange(CreateAttackOrders(context.OwnerPlayerIndex, force, target));
        }
    }

    internal static List<GameObject>? TrySelectAttackForce(Player owner)
    {
        var force = owner.OwnedObjects
            .Where(
                x =>
                    !x.IsStructure
                    && x.CanAttack
                    && !x.Definition.KindOf.Get(ObjectKinds.Harvester)
                    && !x.Definition.KindOf.Get(ObjectKinds.Dozer))
            .ToList();
        if (force.Count == 0)
        {
            force = owner.OwnedObjects
                .Where(x => !x.IsStructure && x.CanAttack)
                .ToList();
        }

        return force.Count < DefaultAttackForceThreshold ? null : force;
    }

    internal static GameObject? FindDefensiveTarget(Player owner)
    {
        var hasAnyOwnedStructure = owner.OwnedObjects.Any(x => x.IsStructure);
        if (!hasAnyOwnedStructure)
        {
            return null;
        }

        foreach (var enemy in owner.Enemies)
        {
            var attacker = enemy.OwnedObjects.FirstOrDefault(x => !x.IsStructure && x.CanAttack);
            if (attacker != null)
            {
                return attacker;
            }
        }

        return null;
    }

    internal static GameObject? FindOffensiveTargetStructure(Player owner)
    {
        foreach (var enemy in owner.Enemies)
        {
            var targetStructure = enemy.OwnedObjects.FirstOrDefault(x => x.IsStructure);
            if (targetStructure != null)
            {
                return targetStructure;
            }
        }

        return null;
    }

    internal static List<Order> CreateAttackOrders(int ownerPlayerIndex, IReadOnlyList<GameObject> force, GameObject target)
    {
        var objectIds = force
            .Select(x => x.Id)
            .ToList();

        if (objectIds.Count == 0)
        {
            return [];
        }

        return
        [
            Order.CreateSetSelection(ownerPlayerIndex, objectIds),
            Order.CreateAttackObject(ownerPlayerIndex, target.Id, force: true),
        ];
    }
}
