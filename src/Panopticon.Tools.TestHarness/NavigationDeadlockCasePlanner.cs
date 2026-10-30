using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Tools.TestHarness;

/// <summary>One planned case of a <c>NavigationDeadlock</c> run (WI-N6-02).</summary>
/// <param name="Name">Report key, e.g. <c>head-on-pair</c>.</param>
/// <param name="Mechanism">Deadlock mechanism the case reproduces, per the deep dive case table.</param>
public sealed record NavigationDeadlockCasePlan(string Name, string Mechanism);

internal static class NavigationDeadlockCasePlanner
{
    public const string HeadOnPair = "head-on-pair";
    public const string CorridorOpposed20 = "corridor-opposed-20";
    public const string CrowdGoal100 = "crowd-goal-100";
    public const string QueueBehindIdle = "queue-behind-idle";
    public const string AsyncNoRoute = "async-no-route";
    public const string SquadOffsetRetrace = "squad-offset-retrace";
    public const string PassageColumn40 = "passage-column-40";
    public const string PassageConverge2x20 = "passage-converge-2x20";
    public const string PassageMixed30 = "passage-mixed-30";
    public const string PassageOpposed1Lane = "passage-opposed-1lane";
    public const string PassageSquad12 = "passage-squad-12";

    public static IReadOnlyList<NavigationDeadlockCasePlan> BuildCases() =>
    [
        new(HeadOnPair, "Two tanks ordered to each other's start mutually block with no separation rule."),
        new(CorridorOpposed20, "Two 20-tank columns ordered through one wall gap in opposite directions."),
        new(CrowdGoal100, "100 squad-less tanks ordered to one shared point; held-off units keep unreachable waypoints."),
        new(QueueBehindIdle, "A column ordered through a gap held by idle friendly units."),
        new(AsyncNoRoute, "Async order paths on, goal sealed inside a structure ring; the empty async result settles NoRoute."),
        new(SquadOffsetRetrace, "A squad march whose follower offsets retrace across impassable cells."),
        new(PassageColumn40, "Forty tanks through one 120-unit wall gap as a single selection."),
        new(PassageConverge2x20, "Two 20-tank groups at 45 degrees converge on one shared goal through the gap."),
        new(PassageMixed30, "Twenty infantry and ten tanks in one selection through the gap."),
        new(PassageOpposed1Lane, "A one-tank-lane gap with two ten-tank columns ordered through in opposite directions."),
        new(PassageSquad12, "A twelve-unit squad march through the gap in spread formation."),
    ];

    public static IReadOnlyList<NavigationDeadlockCasePlan> FilterCases(IReadOnlyList<NavigationDeadlockCasePlan> cases, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return cases;
        }

        var wanted = filter.Split(',').Select(name => name.Trim()).Where(name => name.Length > 0).ToHashSet();
        return [.. cases.Where(plan => wanted.Contains(plan.Name))];
    }
}
