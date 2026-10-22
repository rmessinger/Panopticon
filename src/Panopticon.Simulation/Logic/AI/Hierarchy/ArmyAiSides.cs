#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Logic.AI;

namespace Panopticon.Logic.AI.Hierarchy;

/// <summary>One side's resolved army-AI wiring: the player, its AI player, and the policy.</summary>
public sealed record ArmyAiSide(string SideId, Player Player, AIPlayer Ai, HierarchicalAIDecisionPolicy Policy);

/// <summary>
/// Shared side wiring for army-AI battles (WI-M5-01): applies the per-side AI policy to the
/// skirmish settings before game start, then resolves each side's player and hierarchical policy
/// after it and disables AI on every non-side player. Scenarios call this instead of copying the
/// capacity probe's per-side setup.
/// </summary>
public static class ArmyAiSides
{
    // The skirmish host's player list leads with the neutral and civilian slots, so side i's
    // player sits i past this base (PlayerManager.OnNewGame maps setting i to player i + 2).
    internal const int SidePlayerBaseIndex = 2;

    private static readonly AIDecisionPolicyPayload HierarchicalPayload = new()
    {
        Type = AIDecisionPolicyType.Hierarchical,
    };

    /// <summary>Applies the hierarchical (or caller-supplied) policy to every side's setting.</summary>
    public static void ApplyPolicies(PlayerSetting[] playerSettings, AIDecisionPolicyPayload? policy = null)
    {
        ArgumentNullException.ThrowIfNull(playerSettings);

        policy ??= HierarchicalPayload;
        for (var i = 0; i < playerSettings.Length; i++)
        {
            playerSettings[i].AIPolicy = policy;
        }
    }

    /// <summary>The side players in side order, without touching any AI wiring.</summary>
    public static IReadOnlyList<Player> SidePlayers(IReadOnlyList<Player> players, int sideCount)
    {
        ArgumentNullException.ThrowIfNull(players);

        var sidePlayers = new List<Player>(sideCount);
        for (var i = 0; i < sideCount; i++)
        {
            sidePlayers.Add(PlayerAt(players, i));
        }

        return sidePlayers;
    }

    /// <summary>
    /// Resolves each side's player, AI player, and hierarchical policy, then disables AI on
    /// every non-side player so only the army sides think.
    /// </summary>
    public static IReadOnlyList<ArmyAiSide> Resolve(IReadOnlyList<Player> players, IReadOnlyList<string> sideIds)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(sideIds);

        var sides = new List<ArmyAiSide>(sideIds.Count);
        for (var i = 0; i < sideIds.Count; i++)
        {
            var player = PlayerAt(players, i);
            var ai = player.AIPlayer
                ?? throw new InvalidOperationException(
                    $"Army-AI mode: side player '{sideIds[i]}' has no AI player.");
            var policy = ai.DecisionPolicy as HierarchicalAIDecisionPolicy
                ?? throw new InvalidOperationException(
                    $"Army-AI mode: side player '{sideIds[i]}' does not run the hierarchical policy.");
            sides.Add(new ArmyAiSide(sideIds[i], player, ai, policy));
        }

        var sidePlayers = sides.Select(side => side.Player).ToHashSet();
        foreach (var player in players)
        {
            if (!sidePlayers.Contains(player))
            {
                player.DisableAI();
            }
        }

        return sides;
    }

    private static Player PlayerAt(IReadOnlyList<Player> players, int sideIndex)
    {
        var index = SidePlayerBaseIndex + sideIndex;
        if (index >= players.Count)
        {
            throw new InvalidOperationException(
                $"Side {sideIndex + 1} needs player slot {index}, but the game has {players.Count} players (skirmish adds two leading slots).");
        }

        return players[index];
    }
}
