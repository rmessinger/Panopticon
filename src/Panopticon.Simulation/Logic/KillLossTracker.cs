#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Logic.Object;

namespace Panopticon.Logic;

/// <summary>
/// Counts kills and losses per side and unit definition (WI-M5-05). Losses cover the tracked
/// army (the spawned roster): each non-structure attacker death counts one loss for the victim's
/// owner and definition. Kills credit the last damager's owner and definition when that object
/// still resolves (projectile sources credit their creator); every unresolvable source counts
/// under <see cref="UnknownKillerDefinition"/>. Objects outside the army are ignored as victims,
/// so death-spawned crew (pilot ejection) never distort the roster arithmetic. Passive:
/// subscribing never mutates game state, so attaching the tracker cannot change a seeded run's
/// outcome or frame count.
/// </summary>
internal sealed class KillLossTracker : IDisposable
{
    public const string UnknownKillerDefinition = "unknown";

    private readonly GameLogic _gameLogic;
    private readonly HashSet<ObjectId>? _trackedUnits;
    private readonly HashSet<ObjectId> _countedDeaths = new();
    private readonly Dictionary<(Player? Side, string Definition), int> _losses = new();
    private readonly Dictionary<(Player? Side, string Definition), int> _kills = new();

    public KillLossTracker(GameLogic gameLogic, IEnumerable<GameObject>? trackedUnits = null)
    {
        ArgumentNullException.ThrowIfNull(gameLogic);
        _gameLogic = gameLogic;
        _trackedUnits = trackedUnits is null ? null : [.. trackedUnits.Select(unit => unit.Id)];
        _gameLogic.ObjectLifecycleChanged += OnObjectLifecycleChanged;
    }

    public void Dispose() => _gameLogic.ObjectLifecycleChanged -= OnObjectLifecycleChanged;

    public IReadOnlyDictionary<(Player? Side, string Definition), int> Losses => _losses;

    public IReadOnlyDictionary<(Player? Side, string Definition), int> Kills => _kills;

    public int TotalLosses => _losses.Values.Sum();

    public int TotalKills => _kills.Values.Sum();

    private void OnObjectLifecycleChanged(object? sender, GameObjectLifecycleEventArgs e)
    {
        if (e.Reason is not (GameObjectLifecycleEventReason.Died or GameObjectLifecycleEventReason.Destroyed))
        {
            return;
        }

        // Died and Destroyed both fire for one combat death in some flows; each object counts once.
        if (!_countedDeaths.Add(e.GameObject.Id))
        {
            return;
        }

        if (_trackedUnits is { } army && !army.Contains(e.GameObject.Id))
        {
            return;
        }

        // Combat losses follow the outcome rule's army class (WI-M5-03): non-structure objects
        // that can attack. Death-spawned debris, hulks, tree stumps, and projectiles are all
        // inert and excluded.
        if (e.GameObject.IsStructure || !e.GameObject.CanAttack)
        {
            return;
        }

        var victim = e.GameObject;
        Add(_losses, (e.PreviousOwner ?? e.CurrentOwner, victim.Definition.Name));

        var killer = ResolveKiller(victim.LastDamageSourceId);
        Add(_kills, killer is null
            ? (null, UnknownKillerDefinition)
            : (killer.Owner, killer.Definition.Name));
    }

    private GameObject? ResolveKiller(ObjectId sourceId)
    {
        var source = sourceId.IsValid ? _gameLogic.GetObjectById(sourceId) : null;
        if (source is null)
        {
            return null;
        }

        // Projectile damage credits the shooter; other objects credit as themselves.
        return source.Definition.KindOf.Get(ObjectKinds.Projectile) && source.CreatedByObjectID.IsValid
            ? _gameLogic.GetObjectById(source.CreatedByObjectID)
            : source;
    }

    private static void Add(Dictionary<(Player? Side, string Definition), int> table, (Player? Side, string Definition) key)
    {
        table[key] = table.TryGetValue(key, out var count) ? count + 1 : 1;
    }
}
