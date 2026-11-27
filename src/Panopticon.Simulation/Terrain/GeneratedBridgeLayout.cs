#nullable enable

using System.Collections.Generic;

namespace Panopticon.Terrain;

/// <summary>
/// Level deck of one generated bridge: world-space triangles that the contact surface and the rendered
/// model both consume, with the drivable roadway subset and the route-frame extents of that roadway.
/// </summary>
internal sealed record GeneratedBridgeLayout
{
    public required BridgeRoute Route { get; init; }

    public required double DatumZ { get; init; }

    public required IReadOnlyList<BridgeSurface.BridgeRawFace> Faces { get; init; }

    /// <summary>One kind per entry of <see cref="Faces"/>, in the same order.</summary>
    public required IReadOnlyList<GeneratedBridgeFaceKind> FaceKinds { get; init; }

    /// <summary>Render attributes, three per face in face order (index <c>3 * face + vertex</c>).</summary>
    public required IReadOnlyList<GeneratedBridgeVertexAttributes> VertexAttributes { get; init; }

    public required IReadOnlyList<BridgeSurface.BridgeRawFace> Roadway { get; init; }

    public required double RoadwayStartS { get; init; }

    public required double RoadwayEndS { get; init; }

    public required double RoadwayMinN { get; init; }

    public required double RoadwayMaxN { get; init; }

    public required GeneratedBridgeTrimFacts Trim { get; init; }
}
