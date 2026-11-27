#nullable enable
using System.Collections.Generic;

namespace Panopticon.Terrain;

/// <summary>
/// Cap triangles closing one trimmed piece at its cut plane, with the diagnostics of the cut.
/// </summary>
internal sealed record GeneratedBridgeCaps
{
    public required int Loops { get; init; }

    public required int OpenChains { get; init; }

    public required int NonManifoldNodes { get; init; }

    public required double Area { get; init; }

    public required IReadOnlyList<GeneratedBridgeCapTriangle> Triangles { get; init; }
}
