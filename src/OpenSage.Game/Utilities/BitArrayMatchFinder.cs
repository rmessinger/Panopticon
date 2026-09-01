using System;
using OpenSage.Logic.Object;
using OpenSage.Mathematics;

namespace OpenSage.Utilities;

// TODO(Port): Check if the logic here matches SparseMatchFinder.h.

internal static class BitArrayMatchFinder
{
    public static T FindBest<T, TFlag>(Span<T> set, BitArray<TFlag> flags)
        where T : IConditionState<TFlag>
        where TFlag : Enum
    {
        // Prefer a state whose flag set contains all current flags (ideally an exact match),
        // then the state intersecting the most current flags. Among equally-intersecting
        // partial matches, the state defined later wins: INIs conventionally define generic
        // states (e.g. MOVING) before specific ones (e.g. USING_WEAPON_A), so ATTACKING plus
        // USING_WEAPON_A must resolve to the firing state, not the MOVING+ATTACKING alias.
        T best = default;
        var bestIntersections = 0;
        T bestContained = default;
        var leastMissing = int.MaxValue;
        T defaultItem = default;

        foreach (var item in set)
        {
            foreach (var flagSet in item.ConditionFlags)
            {
                // the default condition state has no bits set
                if (!flagSet.AnyBitSet)
                {
                    defaultItem = item;
                }

                var intersections = flagSet.CountIntersectionBits(flags);
                // we've found a state that matches everything we have, but it may have extra things we don't have that we don't want
                if (intersections == flags.NumBitsSet)
                {
                    var missing = flagSet.NumBitsSet - intersections;
                    if (missing == 0)
                    {
                        // if we have an exact match, great! doesn't matter what else we've found
                        return item;
                    }

                    // otherwise, this state may have extra things we don't have. Cool, but less than ideal... let's keep searching?
                    if (missing < leastMissing)
                    {
                        bestContained = item;
                        leastMissing = missing;
                    }
                }
                else if (intersections > 0 && intersections >= bestIntersections)
                {
                    // not an exact match, but save this if we can't find an exact match
                    bestIntersections = intersections;
                    best = item;
                }
            }
        }

        return bestContained ?? best ?? defaultItem;
    }
}
