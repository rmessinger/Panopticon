#nullable enable

using System.Collections.Generic;
using System.Linq;
using OpenSage.Logic.Object;

namespace OpenSage.Logic.AI.Hierarchy;

/// <summary>
/// Owns the player's squads (A4-3a, WI-A4-18): assigns spawned units to squads, promotes on
/// commander death, and removes empty squads. Squads derive from simulation-owned
/// <see cref="GameObject.Team"/> where assigned; unassigned eligible units auto-group in
/// ascending object-id order into squads of at most <see cref="MaxSquadSize"/>. All rules are
/// the documented ones (a4-decentralized-hierarchical-ai.md §6) and deterministic.
/// </summary>
public sealed class SquadHierarchy
{
    public const int MaxSquadSize = 8;

    private readonly Dictionary<uint, Squad> _squads = new();
    private readonly Dictionary<ObjectId, uint> _memberSquad = new();
    private uint _nextSquadId = 1;

    public IReadOnlyList<Squad> Squads { get; }

    public SquadHierarchy()
    {
        Squads = new List<Squad>();
    }

    /// <summary>
    /// Brings the hierarchy up to date with the owner's live objects: removes dead members,
    /// promotes when a commander died, deletes empty squads, and assigns units that are not
    /// in any squad yet (team-mates together, then open squads, then new squads).
    /// </summary>
    public void Synchronize(Player owner)
    {
        RemoveDeadMembers();
        AssignNewUnits(owner);

        var list = (List<Squad>)Squads;
        list.Clear();
        list.AddRange(_squads.Values.OrderBy(s => s.Id));
    }

    public Squad? SquadOf(GameObject unit) =>
        _memberSquad.TryGetValue(unit.Id, out var squadId)
            ? _squads.TryGetValue(squadId, out var squad) ? squad : null
            : null;

    private void RemoveDeadMembers()
    {
        foreach (var squad in _squads.Values)
        {
            var dead = squad.Members.Where(m => m.IsDestroyed).ToList();
            foreach (var member in dead)
            {
                squad.Members.Remove(member);
                _memberSquad.Remove(member.Id);
            }

            if (squad.Members.Count == 0)
            {
                continue;
            }

            var commanderGone = squad.CommanderId is { } id
                && squad.Members.All(m => m.Id != id);
            if (commanderGone)
            {
                squad.PromoteNextCommander();
            }
        }

        foreach (var empty in _squads.Where(kv => kv.Value.Members.Count == 0).Select(kv => kv.Key).ToList())
        {
            _squads.Remove(empty);
        }
    }

    private void AssignNewUnits(Player owner)
    {
        foreach (var unit in EligibleUnits(owner).Where(u => !u.IsDestroyed && !_memberSquad.ContainsKey(u.Id)))
        {
            var squad = unit.Team != null ? SquadForTeam(unit, owner) : OpenSquad();
            if (squad is null || squad.Members.Count >= MaxSquadSize)
            {
                squad = CreateSquad();
            }

            squad.Members.Add(unit);
            squad.Members.Sort((a, b) => a.Id.Index.CompareTo(b.Id.Index));
            _memberSquad[unit.Id] = squad.Id;
            squad.PromoteNextCommander();
        }
    }

    // Team-mates group together: fill the squad already holding this unit's team-mates if it
    // has room, otherwise a new squad (teams may exceed MaxSquadSize, splitting in id order).
    private Squad? SquadForTeam(GameObject unit, Player owner)
    {
        return _squads.Values
            .OrderBy(s => s.Id)
            .FirstOrDefault(s => s.Members.Count < MaxSquadSize
                && s.Members.Any(m => m.Team == unit.Team));
    }

    private Squad? OpenSquad() =>
        _squads.Values.OrderBy(s => s.Id).FirstOrDefault(s => s.Members.Count < MaxSquadSize);

    private Squad CreateSquad()
    {
        var squad = new Squad(_nextSquadId++);
        _squads[squad.Id] = squad;
        return squad;
    }

    // Same eligibility predicate as the rule-based attack force: attack-capable, non-support,
    // non-structure units. Commanders and soldiers are drawn from the same pool.
    internal static IEnumerable<GameObject> EligibleUnits(Player owner) =>
        owner.OwnedObjects.Where(u =>
            !u.IsStructure
            && u.CanAttack
            && !u.Definition.KindOf.Get(ObjectKinds.Harvester)
            && !u.Definition.KindOf.Get(ObjectKinds.Dozer));
}
