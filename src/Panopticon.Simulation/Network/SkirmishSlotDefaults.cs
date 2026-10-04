using System;
using System.Collections.Generic;
using System.Linq;
using Panopticon.Content;

namespace Panopticon.Network;

public static class SkirmishSlotDefaults
{
    public static void ApplyDefaults(IGame game, IList<SkirmishSlot> slots, string mapName, int seed)
    {
        ArgumentNullException.ThrowIfNull(game);

        ArgumentNullException.ThrowIfNull(slots);

        var map = string.IsNullOrEmpty(mapName)
            ? null
            : game.AssetStore.MapCaches.GetByName(mapName);

        var availablePositions = new List<byte>();
        var availablePlayerCount = Math.Max(1, map?.NumPlayers ?? slots.Count);
        for (byte i = 1; i <= availablePlayerCount; i++)
        {
            availablePositions.Add(i);
        }

        var random = new Random(seed);
        var playableSides = game.GetPlayableSides().ToArray();

        var activeSlots = slots
            .Where(slot => slot.State != SkirmishSlotState.Open && slot.State != SkirmishSlotState.Closed)
            .ToList();

        // Reserve positions already assigned to a slot before handing out the remaining
        // positions, so a manually-set StartPosition can't be re-assigned to another slot.
        foreach (var slot in activeSlots)
        {
            if (slot.StartPosition != 0)
            {
                availablePositions.Remove(slot.StartPosition);
            }
        }

        foreach (var slot in activeSlots)
        {
            if (slot.StartPosition == 0)
            {
                if (availablePositions.Count == 0)
                {
                    continue;
                }

                var startPosition = availablePositions[^1];
                availablePositions.RemoveAt(availablePositions.Count - 1);
                slot.StartPosition = startPosition;
            }

            if (slot.FactionIndex == 0)
            {
                if (playableSides.Length > 0)
                {
                    slot.FactionIndex = (byte)random.Next(1, playableSides.Length + 1);
                }
            }

            if (slot.ColorIndex == -1)
            {
                slot.ColorIndex = (sbyte)random.Next(0, game.AssetStore.MultiplayerColors.Count);
            }
        }
    }
}
