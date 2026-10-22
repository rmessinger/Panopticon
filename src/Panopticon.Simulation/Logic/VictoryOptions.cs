using System;

namespace Panopticon.Logic;

public enum VictoryScoreMetric
{
    BuildingCount,
    UnitCount,
    TotalStructuresAndUnits,
}

/// <summary>Which defeat rule the victory system runs (WI-M5-03).</summary>
public enum VictoryConditionKind
{
    /// <summary>Skirmish default: qualifying structures or a builder with rebuild resources keep a side alive.</summary>
    Default,

    /// <summary>The victory system goes inert; the host owns the ending criteria.</summary>
    NeverDefeated,

    /// <summary>A side survives while it owns a living non-structure unit that can attack.</summary>
    ArmyEliminated,
}

public sealed record VictoryOptions(int FrameLimit, VictoryScoreMetric ScoreMetric, VictoryConditionKind Condition = VictoryConditionKind.Default)
{
    public static readonly VictoryOptions Default = new(VictorySystem.NoFrameLimit, VictoryScoreMetric.BuildingCount);

    public static VictoryOptions DefaultOptions => Default;
}

internal static class VictoryScoreMetrics
{
    internal static Func<Player, int> ToSelector(VictoryScoreMetric metric)
    {
        return metric switch
        {
            VictoryScoreMetric.UnitCount => player => player.UnitCount,
            VictoryScoreMetric.TotalStructuresAndUnits => player => player.BuildingCount + player.UnitCount,
            _ => player => player.BuildingCount,
        };
    }
}
