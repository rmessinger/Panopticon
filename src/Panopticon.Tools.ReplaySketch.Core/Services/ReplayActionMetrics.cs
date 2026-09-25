using System.Collections.Concurrent;
using Panopticon.Logic;

namespace Panopticon.Tools.ReplaySketch.Services;

public static class ReplayActionMetrics
{
    private static readonly ConcurrentDictionary<uint, int> AttackOrdersByPlayerId = new();

    public static void Reset()
    {
        AttackOrdersByPlayerId.Clear();
    }

    public static void RecordAttackOrder(Player? player)
    {
        if (player == null)
        {
            return;
        }

        AttackOrdersByPlayerId.AddOrUpdate(player.Id, 1, static (_, count) => count + 1);
    }

    public static int GetAttackOrderCount(uint playerId)
    {
        return AttackOrdersByPlayerId.TryGetValue(playerId, out var count)
            ? count
            : 0;
    }
}
