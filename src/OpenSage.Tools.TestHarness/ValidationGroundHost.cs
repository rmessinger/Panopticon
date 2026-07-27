using System.Collections.Generic;
using System.Numerics;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;
using OpenSage.Tools.ReplaySketch.Model;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tools.TestHarness;

/// <summary>
/// Applies a <see cref="SpawnPlan"/> to a live <see cref="IGame"/>: resolves each definition and
/// player, samples terrain height, and creates every unit/target/supply-source through
/// <see cref="GameLogic.CreateObject"/>. Orders are dispatched as real engine <see cref="Order"/>s
/// through <see cref="OrderProcessor"/> (only spawning is a shortcut). See deep dive P9 §2.2, §3.
/// </summary>
public sealed class ValidationGroundHost
{
    private readonly List<SpawnedUnit> _units = [];
    private readonly List<UnitGap> _gaps = [];
    private readonly List<GameObject> _targets = [];

    public IReadOnlyList<SpawnedUnit> Units => _units;
    public IReadOnlyList<UnitGap> Gaps => _gaps;
    public IReadOnlyList<GameObject> Targets => _targets;

    public void Spawn(SpawnPlan plan, IGame game)
    {
        SpawnTargets(plan, game);
        SpawnUnits(plan, game);
        SpawnSupplySources(plan, game);
    }

    private void SpawnTargets(SpawnPlan plan, IGame game)
    {
        // Targets are neutral (civilian) so any faction can legally attack them.
        var civilian = game.Scene3D.Players[1];
        foreach (var placement in plan.TargetPlacements)
        {
            var obj = CreateAt(game, placement.DefinitionName, placement.Position, civilian);
            if (obj != null)
            {
                _targets.Add(obj);
            }
        }
    }

    private void SpawnSupplySources(SpawnPlan plan, IGame game)
    {
        var civilian = game.Scene3D.Players[1];
        foreach (var placement in plan.SupplySourcePlacements)
        {
            CreateAt(game, placement.DefinitionName, placement.Position, civilian);
        }
    }

    private void SpawnUnits(SpawnPlan plan, IGame game)
    {
        for (var i = 0; i < plan.UnitPlacements.Count; i++)
        {
            var placement = plan.UnitPlacements[i];
            var player = ResolvePlayer(game, placement.PlayerIndex);
            if (player == null)
            {
                _gaps.Add(new UnitGap(placement.DefinitionName, placement.FactionIndex,
                    placement.Action.Kind, $"no player for index {placement.PlayerIndex}"));
                continue;
            }

            var obj = CreateAt(game, placement.DefinitionName, placement.Position, player);
            if (obj == null)
            {
                _gaps.Add(new UnitGap(placement.DefinitionName, placement.FactionIndex,
                    placement.Action.Kind, $"definition '{placement.DefinitionName}' not found"));
                continue;
            }

            _units.Add(new SpawnedUnit(placement, obj.Id));
        }
    }

    /// <summary>
    /// Dispatches each unit's primary action as a real engine order on a single frame and records
    /// the dispatch with the timer. Call only after the settle stage has elapsed.
    /// </summary>
    public void IssueOrders(IGame game, UnitActionTimer timer)
    {
        var processor = new OrderProcessor(game);
        var currentFrame = CurrentLogicFrame(game);

        foreach (var unit in _units)
        {
            var player = ResolvePlayer(game, unit.Placement.PlayerIndex)!;
            var playerIndex = game.Scene3D.GetPlayerIndex(player);
            var order = BuildOrder(unit, playerIndex, game);
            if (order == null)
            {
                timer.RecordGap(unit.Placement.DefinitionName, unit.Placement.FactionIndex,
                    unit.Placement.Action.Kind, $"no order builder for {unit.Placement.Action.Kind}");
                continue;
            }

            processor.Process([order]);
            timer.OnDispatched(
                unitKey: unit.ObjectId.Index,
                unit.Placement.DefinitionName,
                unit.Placement.FactionIndex,
                unit.Placement.Action,
                frame: currentFrame,
                targetKey: unit.TargetObjectId?.Index);
        }
    }

    private Order? BuildOrder(SpawnedUnit unit, int playerIndex, IGame game)
    {
        var placement = unit.Placement;
        var targetIndex = placement.TargetIndex;
        var target = targetIndex >= 0 && targetIndex < _targets.Count ? _targets[targetIndex] : null;

        return placement.Action.Kind switch
        {
            PrimaryActionKind.Attack when target != null =>
                AttackObjectOrder(playerIndex, target.Id),
            PrimaryActionKind.Move =>
                MoveToOrder(playerIndex, target?.Translation ?? placement.Position),
            PrimaryActionKind.Build =>
                Order.CreateBuildObject(playerIndex, (int)ResolveDefinitionId(game, placement.Action.TargetDefinitionName),
                    placement.Position, 0f),
            PrimaryActionKind.Gather when target != null =>
                new Order(playerIndex, OrderType.GatherDumpSupplies),
            PrimaryActionKind.SpecialPower when target != null =>
                new Order(playerIndex, OrderType.SpecialPowerAtObject),
            _ => MoveToOrder(playerIndex, placement.Position),
        };
    }

    private static Order AttackObjectOrder(int playerIndex, ObjectId targetId)
    {
        var order = new Order(playerIndex, OrderType.AttackObject);
        order.AddObjectIdArgument(targetId);
        return order;
    }

    private static Order MoveToOrder(int playerIndex, Vector3 position)
    {
        var order = new Order(playerIndex, OrderType.MoveTo);
        order.AddPositionArgument(position);
        return order;
    }

    private static GameObject? CreateAt(IGame game, string definitionName, Vector3 planarPosition, Player owner)
    {
        var definition = game.AssetStore.ObjectDefinitions.GetByName(definitionName);
        if (definition == null)
        {
            return null;
        }

        var height = game.Scene3D.Terrain.GetGroundHeight(new Vector2(planarPosition.X, planarPosition.Y)) ?? 0f;
        var position = new Vector3(planarPosition.X, planarPosition.Y, height);
        var gameObject = game.Scene3D.GameObjects.CreateObject(definition, owner);
        gameObject.UpdateTransform(position, gameObject.Rotation);
        return gameObject;
    }

    private static Player? ResolvePlayer(IGame game, int playerIndex)
    {
        var players = game.Scene3D.Players;
        return playerIndex >= 0 && playerIndex < players.Count ? players[playerIndex] : null;
    }

    private static uint ResolveDefinitionId(IGame game, string? definitionName)
    {
        if (string.IsNullOrEmpty(definitionName))
        {
            return 0;
        }

        return game.AssetStore.ObjectDefinitions.GetByName(definitionName)?.InstanceId ?? 0;
    }

    private static uint CurrentLogicFrame(IGame game) =>
        unchecked((uint)(game.MapTime.TotalTime.TotalMilliseconds / game.GameEngine.MsPerLogicFrame));

    /// <summary>
    /// Builds the per-frame observation seam the timer polls: for each tracked unit, reads the
    /// live-game signals that map to its action's completion predicate.
    /// </summary>
    public System.Func<uint, UnitObservation> BuildObservationSeam(IGame game)
    {
        return unitKey =>
        {
            var unit = _units.Find(u => u.ObjectId.Index == unitKey);
            if (unit == null)
            {
                return default;
            }

            var obj = game.Scene3D.GameObjects.GetObjectById(unit.ObjectId);
            if (obj == null)
            {
                return default;
            }

            var target = unit.TargetObjectId.HasValue
                ? game.Scene3D.GameObjects.GetObjectById(unit.TargetObjectId.Value)
                : null;

            return new UnitObservation
            {
                TargetPointsEmpty = obj.AIUpdate?.TargetPoints.Count == 0,
                TargetHealth = target?.BodyModule?.Health,
                BuildComplete = obj.IsEffectivelyDead == false && obj.BuildProgress >= 1.0f,
            };
        };
    }

    public sealed record SpawnedUnit(UnitPlacement Placement, ObjectId ObjectId)
    {
        public ObjectId? TargetObjectId { get; set; }
    }
}
