using OpenSage.Logic.Object;

namespace OpenSage.Logic;

internal abstract class VictoryConditionRule
{
    internal abstract bool IsPlayerDefeated(Player player, VictorySystem victorySystem);

    internal virtual bool IsQualifyingStructure(GameObject gameObject)
    {
        return false;
    }
}

internal sealed class DefaultVictoryConditionRule : VictoryConditionRule
{
    internal override bool IsPlayerDefeated(Player player, VictorySystem victorySystem)
    {
        if (player.BuildingCount > 0)
        {
            foreach (var ownedObject in player.OwnedObjects)
            {
                if (IsQualifyingStructure(ownedObject))
                {
                    return false;
                }
            }
        }

        if (HasBuilderUnit(player) && victorySystem.HasEnoughResourcesToRebuild(player))
        {
            return false;
        }

        return true;
    }

    internal override bool IsQualifyingStructure(GameObject gameObject)
    {
        if (gameObject == null || !gameObject.IsStructure || gameObject.Definition.KindOf.Get(ObjectKinds.IgnoreForVictory))
        {
            return false;
        }

        return gameObject.Definition.KindOf.Get(ObjectKinds.CommandCenter)
            || gameObject.Definition.KindOf.Get(ObjectKinds.FSFactory)
            || gameObject.ProductionUpdate != null;
    }

    private static bool HasBuilderUnit(Player player)
    {
        if (player.UnitCount <= 0)
        {
            return false;
        }

        foreach (var ownedObject in player.OwnedObjects)
        {
            if (ownedObject.IsStructure)
            {
                continue;
            }

            if (ownedObject.Definition.KindOf.Get(ObjectKinds.Dozer) || ownedObject.AIUpdate is IBuilderAIUpdate)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// A rule that never defeats anyone: the victory system goes inert and the run's ending
/// criteria live entirely with the host. For sandboxes (e.g. the Unit Validation Ground)
/// whose completion rules are foreign to the skirmish defeat logic.
/// </summary>
internal sealed class NeverDefeatedVictoryConditionRule : VictoryConditionRule
{
    internal override bool IsPlayerDefeated(Player player, VictorySystem victorySystem)
    {
        return false;
    }
}
