#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Typed vector feature layer of a map interchange. Features are copied at construction.</summary>
public sealed record VectorLayerSet
{
    public VectorLayerSet(IReadOnlyList<VectorFeature> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        Features = features.ToArray();
    }

    /// <summary>Defensive copy; the source list is not retained.</summary>
    public IReadOnlyList<VectorFeature> Features { get; }
}
