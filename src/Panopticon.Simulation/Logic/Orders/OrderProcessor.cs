#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Gui.ControlBar;
using Panopticon.Logic.AI.Hierarchy;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders.SpecialPower;
using Panopticon.Navigation;
using NavigationClass = Panopticon.Navigation.Navigation;

namespace Panopticon.Logic.Orders;

public sealed class OrderProcessor
{
    private readonly IGame _game;

    public OrderProcessor(IGame game)
    {
        _game = game;
    }

    private static NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    // Scene3D.GameObjects and GameLogic are the same IGameObjectCollection instance;
    // fall back to GameLogic so the headless test host (no Scene3D) can process orders.
    private IGameObjectCollection GameObjects => _game.Scene3D?.GameObjects ?? _game.GameLogic;

    public void Process(IEnumerable<Order> orders)
    {
        foreach (var order in orders)
        {
            Player? player = null;

            if (_game.Scene3D != null)
            {
                if (order.PlayerIndex == -1)
                {
                    player = _game.Scene3D.LocalPlayer;
                }
                else
                {
                    player = _game.Scene3D.Players[(int)order.PlayerIndex];
                }
            }
            else
            {
                player = order.PlayerIndex == -1
                    ? _game.PlayerManager.LocalPlayer
                    : _game.PlayerManager.GetPlayerByIndex((uint)order.PlayerIndex);
            }

            var logLevel = order.OrderType == OrderType.SetCameraPosition ? NLog.LogLevel.Trace : NLog.LogLevel.Debug;
            Logger.Log(logLevel, $"Order for player {order.PlayerIndex}: {order.OrderType}");

            if (player == null)
            {
                continue;
            }

            switch (order.OrderType)
            {
                case OrderType.CreateGroup0:
                case OrderType.CreateGroup1:
                case OrderType.CreateGroup2:
                case OrderType.CreateGroup3:
                case OrderType.CreateGroup4:
                case OrderType.CreateGroup5:
                case OrderType.CreateGroup6:
                case OrderType.CreateGroup7:
                case OrderType.CreateGroup8:
                case OrderType.CreateGroup9:
                    {
                        player.CreateSelectionGroup(order.OrderType - OrderType.CreateGroup0);
                    }

                    break;
                case OrderType.SelectGroup0:
                case OrderType.SelectGroup1:
                case OrderType.SelectGroup2:
                case OrderType.SelectGroup3:
                case OrderType.SelectGroup4:
                case OrderType.SelectGroup5:
                case OrderType.SelectGroup6:
                case OrderType.SelectGroup7:
                case OrderType.SelectGroup8:
                case OrderType.SelectGroup9:
                    {
                        player.SelectGroup(order.OrderType - OrderType.SelectGroup0);
                    }

                    break;
                // TODO
                case OrderType.MoveTo:
                case OrderType.AttackMove:
                    {
                        var targetPosition = order.Arguments[0].Value.Position;
                        GameObject? lastUnit = null;
                        foreach (var unit in player.SelectedUnits)
                        {
                            var sound = unit.BodyModule?.DamageState.IsWorseThan(BodyDamageType.Damaged) ?? false
                                ? unit.Definition.SoundMoveStartDamaged?.Value
                                : null;
                            sound ??= unit.Definition.SoundMoveStart?.Value;

                            if (sound != null)
                            {
                                _game.Audio.PlayAudioEvent(unit, sound);
                            }

                            lastUnit = unit;
                        }

                        MoveUnits(_game.Scene3D?.Navigation, player, player.SelectedUnits, targetPosition);

                        foreach (var unit in player.SelectedUnits)
                        {
                            unit.AIUpdate?.ConfigureAttackMove(order.OrderType == OrderType.AttackMove);
                        }

                        lastUnit?.OnLocalMove(_game.Audio);
                    }

                    break;
                case OrderType.BuildObject:
                    {
                        var objectDefinitionId = (uint)order.Arguments[0].Value.Integer;
                        var objectDefinition = _game.AssetStore.ObjectDefinitions.GetByInstanceId(objectDefinitionId);
                        var position = order.Arguments[1].Value.Position;
                        var angle = order.Arguments[2].Value.Float;
                        if (objectDefinition == null)
                        {
                            Logger.Error($"BuildObject order references unknown object definition id {objectDefinitionId}");
                            continue;
                        }

                        if (!TryDebit(player.BankAccount, (uint)objectDefinition.BuildCost))
                        {
                            Logger.Warn($"BuildObject for player {order.PlayerIndex}: cannot afford {objectDefinition.Name} (cost {objectDefinition.BuildCost}, balance {player.BankAccount.Money})");
                            break;
                        }

                        var gameObject = GameObjects.CreateObject(objectDefinition, player);
                        gameObject.Owner = player;
                        gameObject.UpdateTransform(position, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle));

                        gameObject.PrepareConstruction();

                        var dozer = player.SelectedUnits.SingleOrDefault(u => u.Definition.KindOf.Get(ObjectKinds.Dozer));

                        // In synthetic/scripted replays no dozer is pre-selected; fall back to the closest
                        // idle dozer owned by this player so construction starts without delay.
                        dozer ??= GameObjects.Objects
                            .Where(u => u.Owner == player && u.Definition.KindOf.Get(ObjectKinds.Dozer))
                            .MinBy(u => Vector3.DistanceSquared(position, u.Transform.Translation));

                        (dozer?.AIUpdate as IBuilderAIUpdate)?.SetBuildTarget(gameObject); // todo: I don't love this cast; it would be nice to get rid of it

                        _game.Audio.PlayAudioEvent(dozer, dozer?.Definition.UnitSpecificSounds?.VoiceBuildResponse?.Value);
                    }

                    break;
                case OrderType.CancelBuild:
                    {
                        foreach (var unit in player.SelectedUnits)
                        {
                            // This probably shouldn't trigger a Die
                            unit.Kill();
                            player.BankAccount.Deposit((uint)unit.Definition.BuildCost);
                        }

                        player.DeselectUnits();
                    }

                    break;
                case OrderType.ResumeBuild:
                    {
                        var buildTargetId = order.Arguments[0].Value.ObjectId;
                        var buildTarget = GameObjects.GetObjectById(buildTargetId);

                        var dozer = player.SelectedUnits.SingleOrDefault(u => u.IsKindOf(ObjectKinds.Dozer));
                        if (buildTarget != null)
                        {
                            (dozer?.AIUpdate as IBuilderAIUpdate)?.SetBuildTarget(buildTarget); // todo: I don't love this cast; it would be nice to get rid of it
                        }

                        _game.Audio.PlayAudioEvent(dozer, dozer?.Definition.UnitSpecificSounds?.VoiceBuildResponse?.Value);
                    }

                    break;
                case OrderType.BeginUpgrade:
                    {
                        var objectDefinitionId = order.Arguments[0].Value.ObjectId;
                        var upgradeDefinitionId = (uint)order.Arguments[1].Value.Integer;

                        var upgradeDefinition = _game.AssetStore.Upgrades.GetByInstanceId(upgradeDefinitionId);
                        if (!TryDebit(player.BankAccount, (uint)upgradeDefinition.BuildCost))
                        {
                            Logger.Warn($"BeginUpgrade for player {order.PlayerIndex}: cannot afford {upgradeDefinition.Name} (cost {upgradeDefinition.BuildCost}, balance {player.BankAccount.Money})");
                            break;
                        }

                        var gameObject = GameObjects.GetObjectById(objectDefinitionId);
                        gameObject?.ProductionUpdate.QueueUpgrade(upgradeDefinition);
                    }

                    break;

                case OrderType.CancelUpgrade:
                    {
                        var upgradeDefinitionId = (uint)order.Arguments[0].Value.Integer;
                        var upgradeDefinition = _game.AssetStore.Upgrades.GetByInstanceId(upgradeDefinitionId);

                        player.BankAccount.Deposit((uint)upgradeDefinition.BuildCost);
                        // since this is a building and only one at a time can be selected
                        player.SelectedUnits.First().ProductionUpdate.CancelUpgrade(upgradeDefinition);
                    }

                    break;

                case OrderType.StopMoving:
                    foreach (var unit in player.SelectedUnits)
                    {
                        unit.AIUpdate.Stop();
                    }

                    break;

                case OrderType.CreateUnit:
                    {
                        var objectDefinitionId = (uint)order.Arguments[0].Value.Integer;
                        var objectDefinition = _game.AssetStore.ObjectDefinitions.GetByInstanceId(objectDefinitionId);
                        if (objectDefinition == null)
                        {
                            Logger.Error($"CreateUnit order references unknown object definition id {objectDefinitionId}");
                            continue;
                        }

                        if (!TryDebit(player.BankAccount, (uint)objectDefinition.BuildCost))
                        {
                            Logger.Warn($"CreateUnit for player {order.PlayerIndex}: cannot afford {objectDefinition.Name} (cost {objectDefinition.BuildCost}, balance {player.BankAccount.Money})");
                            break;
                        }

                        // Find a producer among the currently selected units that has a UnitBuild
                        // command button for the requested unit definition.  This guards against the
                        // command being routed to the wrong building (e.g. the Command Center instead
                        // of the Barracks) when an incorrect selection is active.
                        var producer = player.SelectedUnits
                            .FirstOrDefault(u => CanProduceUnit(u, objectDefinition));

                        // In synthetic/scripted replays no producer building is pre-selected; fall
                        // back to the first player-owned building that can produce this unit.
                        producer ??= GameObjects.Objects
                            .FirstOrDefault(u => u.Owner == player && CanProduceUnit(u, objectDefinition));

                        if (producer != null)
                        {
                            // Select the producer so subsequent orders (e.g. CancelUnit) target it.
                            _game.Selection?.SetSelectedObjects(player, [producer], playAudio: false);
                            producer.ProductionUpdate?.QueueProduction(objectDefinition);
                        }
                    }

                    break;

                case OrderType.CancelUnit:
                    {
                        var queueIndex = order.Arguments[0].Value.Integer;

                        foreach (var unit in player.SelectedUnits)
                        {
                            // Only units that can produce stuff should produce it
                            if (unit.ProductionUpdate == null)
                            {
                                continue;
                            }

                            var productionJob = unit.ProductionUpdate.ProductionQueue[queueIndex];
                            var objectDefinition = productionJob.ObjectDefinition;

                            player.BankAccount.Deposit((uint)objectDefinition.BuildCost);

                            unit.ProductionUpdate?.CancelProduction(queueIndex);
                        }
                    }

                    break;

                case OrderType.Sell:
                    foreach (var unit in player.SelectedUnits)
                    {
                        unit.Sell();
                        // todo: do not destroy or award money until sell teardown is complete
                        GameObjects.DestroyObject(unit);
                        // items which award free units, like a tunnel network or supply center, have refund value set which overrides SellPercentage
                        var sellAmount = unit.Definition.RefundValue;

                        // from the inis: With nothing (or zero) listed, we sell for half price.
                        if (sellAmount == 0)
                        {
                            sellAmount = (int)Math.Round(unit.Definition.BuildCost * _game.AssetStore.GameData.Current.SellPercentage);
                        }

                        player.BankAccount.Deposit((uint)sellAmount);
                    }

                    break;

                case OrderType.RepairStructure:
                    {
                        var repairer = player.SelectedUnits.SingleOrDefault(u => u.IsKindOf(ObjectKinds.Dozer));
                        var repairTargetId = order.Arguments[0].Value.ObjectId;
                        var repairTarget = GameObjects.GetObjectById(repairTargetId);

                        if (repairTarget != null)
                        {
                            (repairer?.AIUpdate as IBuilderAIUpdate)?.SetRepairTarget(repairTarget);
                        }

                        _game.Audio.PlayAudioEvent(repairer, repairer?.Definition.UnitSpecificSounds?.VoiceRepair?.Value);
                    }

                    break;

                case OrderType.SetCameraPosition:
                    // Ignore this message.
                    break;

                case OrderType.SetSelection:
                    try
                    {
                        var objectIds = order.Arguments.Skip(1)
                            .Select(x => x.Value.ObjectId)
                            .Select(GameObjects.GetObjectById)
                            .ToArray();

                        _game.Selection!.SetSelectedObjects(player, objectIds, order.Arguments[0].Value.Boolean);
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, "Error while setting selection");
                    }

                    break;

                case OrderType.ClearSelection:
                    _game.Selection!.ClearSelectedObjects(player);
                    break;

                case OrderType.AttackObject:
                case OrderType.ForceAttackObject:
                    {
                        var objectId = order.Arguments[0].Value.ObjectId;
                        var targetObject = GameObjects.GetObjectById(objectId);

                        List<GameObject>? attackers = null;
                        foreach (var unit in player.SelectedUnits)
                        {
                            if (!unit.CanAttack)
                            {
                                continue;
                            }

                            (attackers ??= []).Add(unit);
                            unit.CurrentWeapon?.SetTarget(new WeaponTarget(GameObjects, objectId));
                        }

                        // Move into weapon range of the target, then the weapon will stop the
                        // unit and fire once AIUpdate reports a valid target. WI-M4-04: squad
                        // selections share one leader path toward the target.
                        if (targetObject != null && attackers != null)
                        {
                            MoveUnits(_game.Scene3D?.Navigation, player, attackers, targetObject.Translation);
                        }

                        var firstUnit = player.SelectedUnits.FirstOrDefault();

                        // TODO: use the target to figure out which sound triggers
                        // TODO: handle hordes properly
                        var voiceAttack = firstUnit?.Definition.VoiceAttack?.Value;
                        if (voiceAttack != null)
                        {
                            // only the first unit says their attack line (and only for attacking objects, not ground)
                            _game.Audio.PlayAudioEvent(firstUnit, voiceAttack);
                        }
                    }

                    break;

                case OrderType.ForceAttackGround:
                    {
                        var targetPosition = order.Arguments[0].Value.Position;

                        // In synthetic/scripted replays no units may be pre-selected; fall back to
                        // all attackable units owned by this player so the attack order takes effect.
                        var units = player.SelectedUnits.Count > 0
                            ? (IEnumerable<GameObject>)player.SelectedUnits
                            : GameObjects.Objects
                                .Where(u => u.Owner == player && u.CanAttack);

                        // Find the nearest enemy game object at or near the target position.
                        // ForceAttackGround fires at a ground tile; if an enemy building is present
                        // we must target it as an object so that weapon damage is actually applied
                        // (position-targeted WeaponTarget.DoDamage currently deals no damage).
                        // Buildings are not inserted into the quadtree, so do a linear scan.
                        const float searchRadius = 300f;
                        var targetObject = GameObjects.Objects
                            .Where(o => o.Owner != null
                                && player.Enemies.Contains(o.Owner)
                                && Vector3.DistanceSquared(o.Translation, targetPosition) <= searchRadius * searchRadius)
                            .MinBy(o => Vector3.DistanceSquared(o.Translation, targetPosition));

                        foreach (var unit in units)
                        {
                            if (unit.CanAttack)
                            {
                                if (targetObject != null)
                                {
                                    unit.AIUpdate?.SetTargetPoint(targetObject.Translation, asyncEligible: true);
                                    unit.CurrentWeapon?.SetTarget(
                                        new WeaponTarget(GameObjects, targetObject.Id));
                                }
                                else
                                {
                                    // Move toward the target so the unit enters weapon range,
                                    // then the AI will auto-engage once in range.
                                    unit.AIUpdate?.SetTargetPoint(targetPosition, asyncEligible: true);
                                    unit.CurrentWeapon?.SetTarget(new WeaponTarget(targetPosition));
                                }
                            }
                        }
                    }

                    break;

                case OrderType.SetRallyPoint:
                    try
                    {
                        if (order.Arguments.Count == 2)
                        {
                            var objId = order.Arguments[0].Value.ObjectId;
                            var obj = GameObjects.GetObjectById(objId);

                            var rallyPoint = order.Arguments[1].Value.Position;
                            if (obj != null)
                            {
                                obj.RallyPoint = rallyPoint;
                            }
                        }
                        else
                        {
                            var objIds = order.Arguments.Skip(1)
                                .Select(x => x.Value.ObjectId)
                                .Select(GameObjects.GetObjectById)
                                .ToArray();
                            _game.Selection!.SetRallyPointForSelectedObjects(player, objIds, new Vector3());
                        }

                        _game.Audio.PlayAudioEvent("RallyPointSet");
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, "Error while setting rallypoint");
                    }

                    break;
                case OrderType.SpecialPowerAtLocation:
                    {
                        var specialPowerDefinition = (SpecialPowerType)order.Arguments[0].Value.Integer;
                        var specialPowerLocation = order.Arguments[1].Value.Position;
                        var unknownObjectId = order.Arguments[2].Value.ObjectId;
                        var commandFlags = (SpecialPowerOrderFlags)order.Arguments[3].Value.Integer;
                        var commandCenterSource = order.Arguments[4].Value.ObjectId;

                        SpecialPowerAtLocationApplicator.Execute(specialPowerDefinition,
                            new LocationArguments(player, specialPowerLocation, commandFlags,
                                _game.GameLogic.GetObjectById(commandCenterSource)), player.SelectedUnits);
                    }

                    break;

                case OrderType.SpecialPower:
                    {
                        var specialPowerDefinition = (SpecialPowerType)order.Arguments[0].Value.Integer;
                        var commandFlags = (SpecialPowerOrderFlags)order.Arguments[1].Value.Integer;
                        var commandCenterSource = order.Arguments[2].Value.ObjectId; // todo: is this ever used for these commands?

                        SpecialPowerApplicator.Execute(specialPowerDefinition,
                            new SpecialPowerArguments(player, commandFlags), player.SelectedUnits);
                    }

                    break;

                case OrderType.SpecialPowerAtObject:
                    {
                        var specialPowerDefinition = (SpecialPowerType)order.Arguments[0].Value.Integer;
                        var targetId = order.Arguments[1].Value.ObjectId;
                        var commandFlags = (SpecialPowerOrderFlags)order.Arguments[2].Value.Integer;
                        var commandCenterSource = order.Arguments[3].Value.ObjectId;

                        SpecialPowerAtObjectApplicator.Execute(specialPowerDefinition,
                            new ObjectArguments(player, _game.GameLogic.GetObjectById(targetId), commandFlags,
                                _game.GameLogic.GetObjectById(commandCenterSource)), player.SelectedUnits);
                    }

                    break;

                case OrderType.EndGame:
                    _game.VictorySystem.Surrender(player);
                    break;

                case OrderType.PurchaseScience:
                    var scienceDefinitionId = (uint)order.Arguments[0].Value.Integer;
                    var science = _game.AssetStore.Sciences.GetByInstanceId(scienceDefinitionId);
                    player.PurchaseScience(science);
                    //TODO: implement
                    break;

                case OrderType.ExitContainer:
                    var objectIdToExit = order.Arguments[0].Value.ObjectId;
                    foreach (var unit in player.SelectedUnits)
                    {
                        unit.FindBehavior<OpenContainModule>().Remove(objectIdToExit);
                    }

                    break;

                case OrderType.Evacuate:
                    foreach (var unit in player.SelectedUnits)
                    {
                        unit.FindBehavior<OpenContainModule>().Evacuate();
                    }

                    break;

                case OrderType.Enter:
                    {
                        if (order.Arguments[0].ArgumentType != OrderArgumentType.ObjectId ||
                            order.Arguments[0].Value.ObjectId.IsValid)
                        {
                            throw new InvalidOperationException();
                        }

                        var objectDefinitionId = order.Arguments[1].Value.ObjectId;
                        var gameObject = GameObjects.GetObjectById(objectDefinitionId);

                        if (gameObject != null)
                        {
                            var container = gameObject.FindBehavior<OpenContainModule>();
                            foreach (var unit in player.SelectedUnits)
                            {
                                if (!container.CanAddUnit(unit))
                                {
                                    continue; // this unit can't enter the container (kindof doesn't match, or there aren't enough slots)
                                }

                                // TODO: Don't put it in container right now. Tell it to move towards container.
                                //  use AIStateMachine EnterContainerState?
                                //  deselect unit upon entry
                                //unit.AIUpdate.SetTargetObject(gameObject);
                                container.Add(unit);
                            }
                        }

                        break;
                    }

                case OrderType.GatherDumpSupplies:
                    var supplyPointId = order.Arguments[0].Value.ObjectId;
                    var supplyPoint = GameObjects.GetObjectById(supplyPointId);

                    foreach (var unit in player.SelectedUnits)
                    {
                        var behavior = unit.FindBehavior<SupplyAIUpdate>();

                        if (behavior is null)
                        {
                            continue;
                        }

                        if (supplyPoint == null)
                        {
                            // TODO(Port): It's probably not correct to reset both source and target if
                            // the supply point has been destroyed.
                            behavior.CurrentSupplySource = null;
                            behavior.CurrentSupplyTarget = null;
                            behavior.SupplyGatherState = SupplyAIUpdate.SupplyGatherStates.Default;
                        }
                        else if (supplyPoint.IsKindOf(ObjectKinds.SupplySource))
                        {
                            behavior.CurrentSupplySource = supplyPoint;
                            behavior.SupplyGatherState = SupplyAIUpdate.SupplyGatherStates.SearchingForSupplySource;
                        }
                        else // if it's not a supply source, it's a supply center
                        {
                            behavior.CurrentSupplyTarget = supplyPoint;
                            behavior.SupplyGatherState = SupplyAIUpdate.SupplyGatherStates.SearchingForSupplyTarget;
                        }
                    }

                    break;

                case OrderType.HackInternet:
                    {
                        GameObject? lastUnit = null;
                        foreach (var unit in player.SelectedUnits)
                        {
                            if (unit.AIUpdate is not HackInternetAIUpdate hackInternetAIUpdate)
                            {
                                Logger.Warn("Attempting to hack internet with unit without {HackInternetAIUpdate}",
                                    nameof(HackInternetAIUpdate));
                                continue;
                            }

                            hackInternetAIUpdate.StartHackingInternet();
                            lastUnit = unit;
                        }

                        _game.Audio.PlayAudioEvent(lastUnit, lastUnit?.Definition.UnitSpecificSounds.VoiceHackInternet?.Value);
                    }

                    break;

                case OrderType.ToggleOvercharge:
                    foreach (var unit in player.SelectedUnits)
                    {
                        foreach (var overchargeBehavior in unit.FindBehaviors<OverchargeBehavior>())
                        {
                            if (overchargeBehavior.Enabled)
                            {
                                overchargeBehavior.Deactivate();
                            }
                            else
                            {
                                overchargeBehavior.Activate();
                            }
                        }
                    }

                    break;
                case OrderType.Checksum:
                    break;

                default:
                    var args = string.Join(", ", order.Arguments.Select(argument => argument.ToString()));

                    Logger.Info($"Unimplemented order type: {order.OrderType} ({args})");
                    break;
            }

            if (_game is Game concreteGame)
            {
                concreteGame.RaiseOrderProcessed(new OrderProcessedEventArgs(order, player));
            }
        }
    }

    /// <summary>
    /// Card WI-M4-04: executes the movement half of a move order for a selection. Members of one
    /// AI squad share a single leader path through <see cref="GroupMovement.MoveSquad"/> (plus one
    /// request per blocked follower), so path requests scale with squads; units outside any squad
    /// keep per-unit <see cref="AIUpdate.SetTargetPoint"/>, which keeps player selections and
    /// replays on the pre-existing behavior.
    /// </summary>
    internal static void MoveUnits(
        NavigationClass? navigation,
        Player player,
        IReadOnlyCollection<GameObject> selectedUnits,
        Vector3 targetPosition)
    {
        foreach (var unit in selectedUnits)
        {
            unit.AIUpdate?.ResetMovementIntent();
        }

        List<List<GameObject>>? squads = null;
        List<GameObject>? unaccompanied = null;

        if (navigation != null && selectedUnits.Count > 1)
        {
            var hierarchy = (player.AIPlayer?.DecisionPolicy as HierarchicalAIDecisionPolicy)?.Hierarchy;
            if (hierarchy != null)
            {
                var buckets = new Dictionary<uint, List<GameObject>>();
                unaccompanied = [];
                foreach (var unit in selectedUnits)
                {
                    var squad = hierarchy.SquadOf(unit);
                    if (squad is null)
                    {
                        unaccompanied.Add(unit);
                        continue;
                    }

                    if (buckets.TryGetValue(squad.Id, out var bucket))
                    {
                        bucket.Add(unit);
                    }
                    else
                    {
                        buckets[squad.Id] = [unit];
                    }
                }

                if (buckets.Count > 0)
                {
                    squads = [.. buckets.Values];
                }
            }
        }

        if (squads != null)
        {
            foreach (var squad in squads)
            {
                GroupMovement.MoveSquad(navigation, squad, targetPosition);
            }
        }

        var remainder = squads == null ? selectedUnits : unaccompanied!;
        if (navigation != null && navigation.Options.GroupSelectionMoves && remainder.Count > 1)
        {
            MoveUnitsByPositionCluster(navigation, remainder, targetPosition);
            return;
        }

        foreach (var unit in remainder)
        {
            unit.AIUpdate?.SetTargetPoint(targetPosition, asyncEligible: true);
        }
    }

    /// <summary>
    /// Card WI-N2-02: squad-less ground units move as position clusters, one shared leader path
    /// per cell bucket (cell size <see cref="NavigationOptions.SelectionClusterCells"/), members
    /// visited in object id order for determinism. Aircraft and single-unit buckets keep per-unit
    /// <see cref="AIUpdate.SetTargetPoint"/>.
    /// </summary>
    private static void MoveUnitsByPositionCluster(
        NavigationClass navigation,
        IReadOnlyCollection<GameObject> units,
        Vector3 targetPosition)
    {
        var cellSize = navigation.Options.SelectionClusterCells;
        var buckets = new Dictionary<(int CellX, int CellY), List<GameObject>>();
        var singles = new List<GameObject>();
        foreach (var unit in units.OrderBy(unit => unit.Id.Index))
        {
            if (unit.AIUpdate == null || unit.Definition.KindOf?.Get(ObjectKinds.Aircraft) == true)
            {
                singles.Add(unit);
                continue;
            }

            var position = unit.Translation;
            var key = ((int)Math.Floor(position.X / cellSize), (int)Math.Floor(position.Y / cellSize));
            if (buckets.TryGetValue(key, out var bucket))
            {
                bucket.Add(unit);
            }
            else
            {
                buckets[key] = [unit];
            }
        }

        foreach (var bucket in buckets.Values)
        {
            if (bucket.Count > 1)
            {
                GroupMovement.MoveSquad(navigation, bucket, targetPosition);
            }
            else
            {
                singles.Add(bucket[0]);
            }
        }

        foreach (var unit in singles)
        {
            unit.AIUpdate?.SetTargetPoint(targetPosition, asyncEligible: true);
        }
    }

    /// <summary>
    /// Returns true if <paramref name="building"/> has a <see cref="CommandType.UnitBuild"/>
    /// button for <paramref name="objectDefinition"/> in its command set, meaning it is the
    /// correct building to produce that unit.
    /// </summary>
    private static bool CanProduceUnit(GameObject building, ObjectDefinition objectDefinition)
    {
        var commandSet = building.Definition.CommandSet?.Value;
        if (commandSet == null)
        {
            return false;
        }

        foreach (var buttonRef in commandSet.Buttons.Values)
        {
            var button = buttonRef?.Value;
            if (button?.Command == CommandType.UnitBuild
                && button.Object?.Value == objectDefinition)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Affordability guard for purchase paths (BuildObject, CreateUnit, BeginUpgrade).
    /// Returns false without debiting when the balance is below cost, so the caller
    /// can skip the side effect (object creation, queue entry, upgrade start) and keep
    /// the BankAccount non-negative invariant. Returns true and debits exactly cost
    /// when the balance covers it.
    /// </summary>
    internal static bool TryDebit(BankAccount account, uint cost)
    {
        if (account.Money < cost)
        {
            return false;
        }

        account.Withdraw(cost);
        return true;
    }
}
