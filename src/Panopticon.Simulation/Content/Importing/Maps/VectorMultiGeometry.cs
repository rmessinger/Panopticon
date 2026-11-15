#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>A heterogeneous collection of at least one child geometry. Children are copied at construction.</summary>
public sealed record VectorMultiGeometry : VectorGeometry
{
    public VectorMultiGeometry(IReadOnlyList<VectorGeometry> geometries)
    {
        ArgumentNullException.ThrowIfNull(geometries);
        Geometries = geometries.ToArray();
    }

    /// <summary>Defensive copy; the source list is not retained.</summary>
    public IReadOnlyList<VectorGeometry> Geometries { get; }
}
