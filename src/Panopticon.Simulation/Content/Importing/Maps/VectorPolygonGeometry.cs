#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>An outer ring with optional inner rings (holes).</summary>
public sealed record VectorPolygonGeometry : VectorGeometry
{
    public VectorPolygonGeometry(VectorRing outerRing, IReadOnlyList<VectorRing> innerRings)
    {
        ArgumentNullException.ThrowIfNull(outerRing);
        ArgumentNullException.ThrowIfNull(innerRings);
        OuterRing = outerRing;
        InnerRings = innerRings.ToArray();
    }

    public VectorRing OuterRing { get; }

    /// <summary>Defensive copy; the source list is not retained.</summary>
    public IReadOnlyList<VectorRing> InnerRings { get; }
}
