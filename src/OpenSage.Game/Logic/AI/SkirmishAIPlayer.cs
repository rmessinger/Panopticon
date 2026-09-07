#nullable enable

using OpenSage.Logic.Orders;

namespace OpenSage.Logic.AI;

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

    internal SkirmishAIPlayer(Player owner, IAIDecisionPolicy decisionPolicy)
        : base(owner, decisionPolicy)
    {
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
