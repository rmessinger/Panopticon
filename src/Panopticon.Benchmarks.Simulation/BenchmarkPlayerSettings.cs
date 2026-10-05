using System.Linq;
using Panopticon.Logic;

namespace Panopticon.Benchmarks.Simulation;

/// <summary>
/// Player settings for benchmark scenarios: passive owners (no human, no AI) on the first two
/// playable sides. Team parameters make the two sides allied or hostile per scenario.
/// </summary>
public static class BenchmarkPlayerSettings
{
    public static PlayerSetting[] TwoSides(Game game, int teamA = 0, int teamB = 0)
    {
        var sides = game.GetPlayableSides().ToArray();

        return
        [
            new PlayerSetting(
                0,
                sides[0].Name,
                game.AssetStore.MultiplayerColors.GetByIndex(0).RgbColor,
                teamA,
                PlayerOwner.None,
                "bench-a"),
            new PlayerSetting(
                1,
                sides[sides.Length > 1 ? 1 : 0].Name,
                game.AssetStore.MultiplayerColors.GetByIndex(1).RgbColor,
                teamB,
                PlayerOwner.None,
                "bench-b"),
        ];
    }
}
