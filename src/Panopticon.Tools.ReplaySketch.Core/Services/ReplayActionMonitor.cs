using System;
using Panopticon;
using Panopticon.Content;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Tools.ReplaySketch.Model;
namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// Subscribes to low-level <see cref="IGame"/> events and re-raises them as semantic
/// <see cref="ActionType"/>-keyed notifications that the test harness can react to.
/// </summary>
/// <remarks>
/// "Dispatched" events fire when the replay order for an action is processed.
/// "Completed" events fire when the resulting game effect is observed (e.g. a unit exits production).
/// </remarks>
public sealed class ReplayActionMonitor : IDisposable
{
    private readonly IGame _game;

    /// <summary>
    /// Raised when a replay order corresponding to a <see cref="ActionType"/> is processed.
    /// </summary>
    public event EventHandler<ReplayActionEventArgs>? ActionDispatched;

    /// <summary>
    /// Raised when the game effect of a <see cref="ActionType"/> is observed in game state
    /// (e.g. a recruited unit exits the barracks).
    /// </summary>
    public event EventHandler<ReplayActionEventArgs>? ActionCompleted;

    public ReplayActionMonitor(IGame game)
    {
        _game = game;
        _game.OrderProcessed += OnOrderProcessed;
        _game.ObjectCreated += OnObjectCreated;
        _game.ObjectDestroyed += OnObjectDestroyed;
        _game.ConstructionCompleted += OnConstructionCompleted;
    }

    private void OnOrderProcessed(object? sender, OrderProcessedEventArgs e)
    {
        var actionType = e.Order.OrderType switch
        {
            OrderType.BuildObject => ActionType.Construct,
            OrderType.GatherDumpSupplies => ActionType.GatherResources,
            OrderType.CreateUnit => ActionType.RecruitBasicUnit,
            OrderType.AttackObject => ActionType.AttackEnemyBase,
            OrderType.ForceAttackObject => ActionType.AttackEnemyBase,
            OrderType.ForceAttackGround => ActionType.AttackEnemyBase,
            _ => (ActionType?)null,
        };

        if (actionType is not null)
        {
            if (actionType == ActionType.AttackEnemyBase)
            {
                ReplayActionMetrics.RecordAttackOrder(e.Player);
            }

            // Resolve the specific object definition this order targets (if any) so that
            // scenarios with several actions of the same ActionType (e.g. one Construct per
            // building in a full build-out) can be told apart at runtime — see DefinitionName
            // on ReplayActionEventArgs.
            var definitionName = ResolveDefinitionName(actionType.Value, e.Order);

            ActionDispatched?.Invoke(this, new ReplayActionEventArgs(actionType.Value, e.Player, definitionName));

            // For AttackEnemyBase the game effect — units begin moving toward the attack position —
            // is immediate: the order is routed to all attackable units on the same frame it is
            // processed. Fire ActionCompleted right away so the test harness can capture the state.
            if (actionType == ActionType.AttackEnemyBase)
            {
                ActionCompleted?.Invoke(this, new ReplayActionEventArgs(actionType.Value, e.Player, definitionName));
            }
        }
    }

    /// <summary>
    /// Reverse-looks-up the object/unit definition name targeted by a BuildObject/CreateUnit
    /// order, so deferred-action matching can disambiguate between multiple actions of the same
    /// <see cref="ActionType"/> (e.g. several Construct actions, one per building).
    /// </summary>
    private string? ResolveDefinitionName(ActionType actionType, Order order)
    {
        if (actionType != ActionType.Construct && actionType != ActionType.RecruitBasicUnit)
            return null;

        var definitionId = (uint)order.Arguments[0].Value.Integer;
        return _game.AssetStore.ObjectDefinitions.GetByInstanceId(definitionId)?.Name;
    }

    private void OnObjectCreated(object? sender, GameObjectCreatedEventArgs e)
    {
        if (!UnitCatalog.ContainsDefinition(e.GameObject.Definition.Name))
            return;

        ActionCompleted?.Invoke(this,
            new ReplayActionEventArgs(ActionType.RecruitBasicUnit, e.GameObject.Owner, e.GameObject.Definition.Name));
    }

    private void OnObjectDestroyed(object? sender, GameObjectDestroyedEventArgs e)
    {
        if (e.GameObject.IsKindOf(ObjectKinds.CommandCenter))
        {
            ActionCompleted?.Invoke(this,
                new ReplayActionEventArgs(ActionType.EnemyBaseDestroyed, e.GameObject.Owner, e.GameObject.Definition.Name));
        }
    }

    private void OnConstructionCompleted(object? sender, GameObjectConstructionCompletedEventArgs e)
    {
        ActionCompleted?.Invoke(this,
            new ReplayActionEventArgs(ActionType.Construct, e.GameObject.Owner, e.GameObject.Definition.Name));
    }

    public void Dispose()
    {
        _game.OrderProcessed -= OnOrderProcessed;
        _game.ObjectCreated -= OnObjectCreated;
        _game.ObjectDestroyed -= OnObjectDestroyed;
        _game.ConstructionCompleted -= OnConstructionCompleted;
    }
}

public sealed class ReplayActionEventArgs : EventArgs
{
    public ActionType ActionType { get; }
    public Player? Player { get; }

    /// <summary>
    /// The object/unit definition name this event pertains to (e.g. "AmericaBarracks" for a
    /// Construct/ConstructionCompleted event, "AmericaInfantryRanger" for a RecruitBasicUnit
    /// event). Null for action types that don't target a specific definition (GatherResources,
    /// AttackEnemyBase, EnemyBaseDestroyed).
    /// </summary>
    public string? DefinitionName { get; }

    public ReplayActionEventArgs(ActionType actionType, Player? player, string? definitionName = null)
    {
        ActionType = actionType;
        Player = player;
        DefinitionName = definitionName;
    }
}
