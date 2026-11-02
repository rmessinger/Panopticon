using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Geospatial.Interchange;

internal static class InterchangeCopy
{
    public static T[] CopyItems<T>(IEnumerable<T> items, string paramName)
    {
        ArgumentNullException.ThrowIfNull(items);

        var copy = items.ToArray();

        for (var index = 0; index < copy.Length; index++)
        {
            if (copy[index] is null)
            {
                throw new ArgumentException("Sequence items must not be null.", paramName);
            }
        }

        return copy;
    }
}
