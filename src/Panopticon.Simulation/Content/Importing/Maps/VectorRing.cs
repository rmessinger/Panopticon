#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>A closed ring of at least four positions whose first and last positions coincide. Positions are copied at construction.</summary>
public sealed record VectorRing
{
    public VectorRing(IReadOnlyList<VectorPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        Positions = positions.ToArray();
    }

    /// <summary>Defensive copy; the source list is not retained.</summary>
    public IReadOnlyList<VectorPosition> Positions { get; }
}
