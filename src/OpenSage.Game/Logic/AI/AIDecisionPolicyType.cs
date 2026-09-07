namespace OpenSage.Logic.AI;

/// <summary>
/// Config identifier for the <see cref="IAIDecisionPolicy"/> implementation a player's AI runs.
/// Members must correspond to real, working implementations resolvable through
/// <see cref="AIDecisionPolicyFactory"/>.
/// </summary>
public enum AIDecisionPolicyType
{
    /// <summary>The rule-based skirmish state machine (the default, reference policy).</summary>
    RuleBased = 0,

    /// <summary>
    /// Fixed-weight feed-forward network policy; requires a weight vector at creation
    /// (weights are evolved outside the engine and supplied by the experiment harness).
    /// </summary>
    NeuralNetwork = 1,
}
