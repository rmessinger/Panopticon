using System;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;

namespace Panopticon.Utilities;

// TODO(Port): Check if the logic here matches SparseMatchFinder.h.

internal static class BitArrayMatchFinder
{
    public static T FindBest<T, TFlag>(Span<T> set, BitArray<TFlag> flags)
        where T : IConditionState<TFlag>
        where TFlag : Enum
    {
        // Prefer a state whose flag set contains all current flags (ideally an exact match),
        // Then prefer the most intersecting flags, breaking partial-match ties by the
        // fewest extra flags. Equal scores retain the earlier definition.
        T best = default;
        var bestIntersections = 0;
        var bestExtraBits = int.MaxValue;
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
                else if (intersections > 0
                    && (intersections > bestIntersections
                        // Among equally-intersecting partial matches prefer the state requiring
                        // the fewest flags we do not have: a pristine jet with JETEXHAUST+MOVING
                        // must resolve to the JETEXHAUST state, not the later-defined
                        // RUBBLE+JETEXHAUST+JETAFTERBURNER wreck.
                        || (intersections == bestIntersections && flagSet.NumBitsSet - intersections < bestExtraBits)))
                {
                    bestIntersections = intersections;
                    bestExtraBits = flagSet.NumBitsSet - intersections;
                    best = item;
                }
            }
        }

        return bestContained ?? best ?? defaultItem;
    }
}
