#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>An ordered sequence of at least two vector positions. Positions are copied at construction.</summary>
public sealed record VectorLineStringGeometry : VectorGeometry
{
    public VectorLineStringGeometry(IReadOnlyList<VectorPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        Positions = positions.ToArray();
    }

    /// <summary>Defensive copy; the source list is not retained.</summary>
    public IReadOnlyList<VectorPosition> Positions { get; }
}
