#nullable enable

using System.Collections.Generic;
using System.Linq;
using Panopticon.Logic.Object;

namespace Panopticon.Logic.AI.Hierarchy;

/// <summary>
/// One squad of the command hierarchy (A4-3a): a stable identity, its live members in
/// ascending object-id order, and its commander selected by the documented rule (highest
/// <see cref="ObjectDefinition.BuildCost"/>, ties to the lower object id).
/// </summary>
public sealed class Squad
{
    public uint Id { get; }

    public ObjectId? CommanderId { get; internal set; }

    public List<GameObject> Members { get; } = new();

    public Squad(uint id)
    {
        Id = id;
    }

    /// <summary>
    /// Promotes the highest-value surviving member to commander, returning the new
    /// commander (null when the squad is empty). The same ranking rule used for the
    /// initial assignment, so promotion is deterministic.
    /// </summary>
    public ObjectId? PromoteNextCommander()
    {
        var commander = RankMembers().FirstOrDefault();
        CommanderId = commander?.Id;
        return CommanderId;
    }

    // Highest build cost first, then lower object id: a stable total order.
    internal IOrderedEnumerable<GameObject> RankMembers() =>
        Members.OrderByDescending(m => m.Definition.BuildCost).ThenBy(m => m.Id.Index);
}
