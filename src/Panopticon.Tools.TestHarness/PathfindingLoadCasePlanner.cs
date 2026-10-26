using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Tools.TestHarness;

/// <summary>One planned measurement case of a <c>PathfindingLoad</c> run (WI-N0-02).</summary>
/// <param name="Name">Report key, e.g. <c>squadless-10</c>.</param>
/// <param name="Mode">Squad order mode: squadless or armyai-squads.</param>
/// <param name="Size">Units in the selection.</param>
/// <param name="DestinationKind">Passable cross-map or unreachable impassable-pocket.</param>
public sealed record PathfindingLoadCasePlan(string Name, string Mode, int Size, string DestinationKind);

internal static class PathfindingLoadCasePlanner
{
    public const string SquadlessMode = "squadless";
    public const string ArmyAiSquadsMode = "armyai-squads";
    public const string CrossMapDestination = "cross-map";
    public const string ImpassablePocketDestination = "impassable-pocket";
    public const string OffMapFallbackDestination = "off-map-fallback";

    public static IReadOnlyList<PathfindingLoadCasePlan> BuildCases(IReadOnlyList<int> sizes, int impassableCaseSize = 10) =>
    [
        .. sizes.Select(size => new PathfindingLoadCasePlan($"squadless-{size}", SquadlessMode, size, CrossMapDestination)),
        .. sizes.Select(size => new PathfindingLoadCasePlan($"armyai-squads-{size}", ArmyAiSquadsMode, size, CrossMapDestination)),
        new($"squadless-{impassableCaseSize}-impassable", SquadlessMode, impassableCaseSize, ImpassablePocketDestination),
    ];
}
