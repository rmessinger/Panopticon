namespace Panopticon.Navigation;

/// <summary>
/// Behavior-tuning knobs for <see cref="Navigation"/> (WI-N2-01). Defaults are measured
/// choices; see the WI-N2 results in the Track N implementation plan.
/// </summary>
public sealed class NavigationOptions
{
    /// <summary>Measured default for <see cref="MaxLandingRadiusCells"/> (WI-N2-01).</summary>
    public const int DefaultMaxLandingRadiusCells = 16;

    /// <summary>
    /// Ring bound in cells for blocked-follower landing searches: the first passable cell at or
    /// within this Chebyshev radius of the blocked offset destination, inside the destination's
    /// connected region, wins and the follower retraces the leader path onto it. Zero disables
    /// landing; every blocked follower then pays for its own full search, the pre-N2 behavior.
    /// </summary>
    public int MaxLandingRadiusCells { get; set; } = DefaultMaxLandingRadiusCells;
}
