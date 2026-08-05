using System;

namespace OpenSage.Logic;

public enum VictoryScoreMetric
{
    BuildingCount,
    UnitCount,
    TotalStructuresAndUnits,
}

public sealed record VictoryOptions(int FrameLimit, VictoryScoreMetric ScoreMetric)
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
