#nullable enable

namespace Panopticon.Terrain;

/// <summary>
/// Diagnostics of one generated bridge's level deck: the native piece lengths, the trim that fits the
/// route, and the counts of the trimmed piece and its cut.
/// </summary>
internal sealed record GeneratedBridgeTrimFacts
{
    public required double LengthLeft { get; init; }

    public required double LengthSpan { get; init; }

    public required double LengthRight { get; init; }

    public required double TrimmedLength { get; init; }

    /// <summary>The span length removed from the copied span: <c>LengthSpan - TrimmedLength</c>.</summary>
    public required double TrimAmount { get; init; }

    public required double CapArea { get; init; }

    public required int FullSpans { get; init; }

    public required int TrimmedPieceFaces { get; init; }

    public required int KeptIntact { get; init; }

    public required int ClippedFaces { get; init; }

    public required int ClippedOutputTriangles { get; init; }

    public required int DroppedFaces { get; init; }

    public required int CapLoops { get; init; }

    public required int CapOpenChains { get; init; }

    public required int CapNonManifoldNodes { get; init; }

    public required int CapTriangles { get; init; }
}
