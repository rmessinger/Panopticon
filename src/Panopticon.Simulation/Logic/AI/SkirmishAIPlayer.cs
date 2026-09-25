#nullable enable

using Panopticon.Logic.AI.Hierarchy;
using Panopticon.Logic.Orders;

namespace Panopticon.Logic.AI;

/// <summary>
/// The skirmish AI player host: owns cadence, dispatch, and the skirmish save layout, while
/// strategic decisions come from an injected <see cref="IAIDecisionPolicy"/> (the factory-built
/// <see cref="RuleBasedAIDecisionPolicy"/> unless a scenario selects another policy).
/// </summary>
public sealed class SkirmishAIPlayer : AIPlayer
{
    internal const int DefaultTargetArmySize = RuleBasedAIDecisionPolicy.DefaultTargetArmySize;
    internal const int DefaultAttackForceThreshold = RuleBasedAIDecisionPolicy.DefaultAttackForceThreshold;

    private int _unknownInt1;
    private int _unknownInt2;
    private float _unknownFloat1;
    private float _unknownFloat2;

    /// <summary>
    /// The squad hierarchy when the selected policy is hierarchical (WI-A4-19 wires
    /// persistence through here; WI-A4-22 wires the policy). Null otherwise.
    /// </summary>
    internal SquadHierarchy? Hierarchy { get; set; }

    internal SkirmishAIPlayer(Player owner, IAIDecisionPolicy decisionPolicy)
        : base(owner, decisionPolicy)
    {
    }

    public override void Persist(StatePersister reader)
    {
        // Version 2 adds the version-gated squad-hierarchy block (WI-A4-19); version-1
        // saves have no block and load unchanged, rebuilding squads on demand.
        var version = reader.PersistVersion(2);

        reader.BeginObject("Base");
        base.Persist(reader);
        reader.EndObject();

        reader.PersistInt32(ref _unknownInt1);
        reader.PersistInt32(ref _unknownInt2);
        reader.PersistSingle(ref _unknownFloat1);
        reader.PersistSingle(ref _unknownFloat2);

        reader.SkipUnknownBytes(16);

        if (version >= 2)
        {
            var hasHierarchy = Hierarchy is not null;
            reader.PersistBoolean(ref hasHierarchy);
            if (hasHierarchy)
            {
                var state = Hierarchy?.CaptureState() ?? new SquadHierarchyState();
                reader.PersistObject(state, "Hierarchy");

                if (reader.Mode == StatePersistMode.Read)
                {
                    // Ids stay unresolved here; the hierarchy resolves them against live
                    // objects on its next Synchronize, after player and object state exists.
                    Hierarchy = new SquadHierarchy();
                    Hierarchy.Restore(state);
                }
            }
        }
    }
}
