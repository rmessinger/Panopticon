#nullable enable

using System.Collections.Generic;
using OpenSage.Content.Importing;

namespace OpenSage.Tools.ReplaySketch.Model;

/// <summary>Metadata for the latest preview artifact; no filesystem paths.</summary>
public sealed record ExternalPreviewArtifactMetadata(
    string ArtifactId,
    long ByteLength);

/// <summary>
/// Immutable, complete session state. Published only as a whole, so consumers never
/// observe partially updated state. A ready session may carry warnings.
/// </summary>
public sealed record ExternalModelSnapshot(
    int Generation,
    ExternalModelSessionState State,
    string? SourceDisplayName,
    ExternalAssetCatalog? Catalog,
    IReadOnlyList<ExternalModelDiagnostic> Diagnostics,
    PreviewEligibility Eligibility,
    ExternalPreviewArtifactMetadata? PreviewArtifact)
{
    public static ExternalModelSnapshot CreateEmpty(int generation)
    {
        return new ExternalModelSnapshot(
            generation,
            ExternalModelSessionState.Empty,
            null,
            null,
            [],
            PreviewEligibility.NotEvaluated,
            null);
    }
}
