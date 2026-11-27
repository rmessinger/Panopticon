#nullable enable

using System.Collections.Generic;
using System.Numerics;
using Panopticon.Content.Importing;

namespace Panopticon.Terrain;

/// <summary>
/// Authored placement of one generated bridge: the route it spans, its world-space raw faces in
/// authored piece order, the placement matrix and the native piece lengths in route units.
/// </summary>
internal sealed record GeneratedBridgeLayoutInput
{
    public required BridgeRoute Route { get; init; }

    public required double DatumZ { get; init; }

    public required float BridgeScale { get; init; }

    /// <summary>The bridge's existing world placement: scale, pitch, bearing and start anchor.</summary>
    public required Matrix4x4 AuthoredWorldMatrix { get; init; }

    public required IReadOnlyList<BridgeSurface.BridgeRawFace> RawFaces { get; init; }

    /// <summary>
    /// Piece matrix that posed each raw face (parallel to <see cref="RawFaces"/>): the vertex
    /// positions are the source positions transformed by it, and normals take its inverse transpose.
    /// </summary>
    public IReadOnlyList<Matrix4x4> PieceMatrices { get; init; } = [];

    /// <summary>
    /// CPU-imported source mesh of each piece, keyed by piece name. Not required: the attributes are
    /// resolved after the structural checks, so layouts that fail structurally need no source meshes.
    /// </summary>
    public IReadOnlyDictionary<string, ImportedMesh> SourceMeshes { get; init; } = new Dictionary<string, ImportedMesh>();

    public required string LeftPieceName { get; init; }

    public required string SpanPieceName { get; init; }

    public required string RightPieceName { get; init; }

    public required double LengthLeft { get; init; }

    public required double LengthSpan { get; init; }

    public required double LengthRight { get; init; }
}
