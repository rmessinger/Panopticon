#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Placed objects of a map. Objects are copied at construction.</summary>
public sealed record PlacedObjectSet
{
    public PlacedObjectSet(IReadOnlyList<PlacedObject> objects)
    {
        ArgumentNullException.ThrowIfNull(objects);
        Objects = objects.ToArray();
    }

    public IReadOnlyList<PlacedObject> Objects { get; }
}
