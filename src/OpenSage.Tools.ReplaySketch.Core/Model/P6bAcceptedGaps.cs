using System;
using System.Collections.Generic;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>
/// Classification of a P6b gap surfaced by the six archetype contract scenarios.
/// </summary>
public enum GapClassification
{
    /// <summary>Small handler/wiring issue fixed within P6b.</summary>
    FixNow,

    /// <summary>Assertion was wrong and has been corrected.</summary>
    AssertionWrong,

    /// <summary>Deep, cross-cutting behaviour deferred to a follow-up item; the scenario failure
    /// stays explicit until the follow-up is implemented.</summary>
    OutOfScopeFollowUp,
}

/// <summary>
/// One accepted P6b gap: the unimplemented special power, why it is out of scope, and the
/// follow-up that will close it.
/// </summary>
public sealed record P6bAcceptedGap(
    string SpecialPowerName,
    GapClassification Classification,
    string Reason,
    string FollowUp);

/// <summary>
/// Registry of every special-power gap surfaced by the P6b archetype scenarios that is accepted as
/// out of scope for P6b, with its follow-up. Keeps the gap list explicit so no unimplemented handler
/// can silently pass: a gap not present here would fail the gap-closure test.
///
/// Each entry is a superweapon or area-effect ability whose applicator arm throws
/// <see cref="NotImplementedException"/>. Implementing them requires OCL creation, area damage,
/// and dedicated visuals/beam systems, which is out of scope for the contract-validation phase and
/// tracked as a follow-up.
/// </summary>
public static class P6bAcceptedGaps
{
    private const string FollowUpTrack = "special-power/area-effect handler track (P6b follow-up)";

    private static readonly P6bAcceptedGap[] Accepted =
    [
        new("CleanupArea", GapClassification.OutOfScopeFollowUp,
            "Ambulance detox/cleanup area needs OCL + radius area-effect behaviour.",
            FollowUpTrack),
        new("ParticleUplinkCannon", GapClassification.OutOfScopeFollowUp,
            "USA particle cannon fire needs beam OCL + sustained area damage + visuals.",
            FollowUpTrack),
        new("NuclearMissile", GapClassification.OutOfScopeFollowUp,
            "China nuclear missile launch needs ballistic OCL + large area damage + FX.",
            FollowUpTrack),
        new("ScudStorm", GapClassification.OutOfScopeFollowUp,
            "GLA SCUD storm needs multi-missile OCL + area damage + FX.",
            FollowUpTrack),
    ];

    private static readonly Dictionary<string, P6bAcceptedGap> ByName = BuildIndex();

    /// <summary>True when <paramref name="specialPowerName"/> is an accepted out-of-scope gap.</summary>
    public static bool IsAccepted(string specialPowerName) =>
        ByName.ContainsKey(specialPowerName);

    /// <summary>Returns the registered gap, or null if the power is not an accepted gap.</summary>
    public static P6bAcceptedGap? Get(string specialPowerName) =>
        ByName.TryGetValue(specialPowerName, out var gap) ? gap : null;

    public static IReadOnlyList<P6bAcceptedGap> All => Accepted;

    private static Dictionary<string, P6bAcceptedGap> BuildIndex()
    {
        var index = new Dictionary<string, P6bAcceptedGap>(StringComparer.Ordinal);
        foreach (var gap in Accepted)
        {
            index[gap.SpecialPowerName] = gap;
        }
        return index;
    }
}
