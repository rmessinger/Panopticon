#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Panopticon.Content.Importing;

namespace Panopticon.Tools.ReplaySketch.Model;

/// <summary>
/// Normalized identity of one external source: display name, virtual key, length, and
/// SHA-256 content hash. Never carries a machine-specific absolute path.
/// </summary>
public sealed record ExternalSourceIdentity(
    string DisplayName,
    string VirtualKey,
    long ByteLength,
    string ContentHash)
{
    public static ExternalSourceIdentity Create(
        string displayName,
        string virtualKey,
        Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new ExternalSourceIdentity(
            displayName,
            virtualKey,
            content.Length,
            ComputeContentHash(content));
    }

    public static string ComputeContentHash(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var position = content.Position;

        try
        {
            content.Position = 0;
            return Convert.ToHexString(SHA256.HashData(content));
        }
        finally
        {
            content.Position = position;
        }
    }
}

/// <summary>Render-stage metadata; no staging or installation paths.</summary>
public sealed record ExternalRenderMetadata(
    int ViewportWidth,
    int ViewportHeight,
    string? AnimationKey,
    float? AnimationSampleTimeSeconds,
    string? PngContentHash,
    long? PngByteLength);

/// <summary>
/// Versioned external model report envelope. Pairing, importer options, corrections, and
/// render metadata belong to their owning later cards. Contains only normalized identities,
/// engine-owned catalog values, structured diagnostics, and eligibility; no parser objects,
/// temporary paths, stack traces, or machine-specific paths.
/// </summary>
public sealed class ExternalModelReport
{
    public const int SchemaVersion = 1;

    public int ReportSchemaVersion => SchemaVersion;

    /// <summary>Request generation this report answers; null for pre-request failure reports.</summary>
    public int? RequestGeneration { get; }

    public ExternalSourceIdentity? ModelSource { get; }

    public ExternalSourceIdentity? AnimationSource { get; }

    public ExternalAssetCatalog? Catalog { get; }

    public ExternalAnimationPairing? AnimationPairing { get; }

    public ExternalModelImportOptions? ImportOptions { get; }

    public ExternalModelPreviewOptions? PreviewOptions { get; }

    public ExternalRenderMetadata? RenderMetadata { get; }

    public IReadOnlyList<ExternalModelDiagnostic> Diagnostics { get; }

    public PreviewEligibility Eligibility { get; }

    public ExternalModelReport(
        ExternalSourceIdentity? modelSource,
        ExternalSourceIdentity? animationSource,
        ExternalAssetCatalog? catalog,
        IReadOnlyList<ExternalModelDiagnostic>? diagnostics,
        PreviewEligibility? eligibility,
        ExternalAnimationPairing? animationPairing = null,
        ExternalModelImportOptions? importOptions = null,
        ExternalModelPreviewOptions? previewOptions = null,
        ExternalRenderMetadata? renderMetadata = null,
        int? requestGeneration = null)
    {
        RequestGeneration = requestGeneration;
        ModelSource = modelSource;
        AnimationSource = animationSource;
        Catalog = catalog;
        AnimationPairing = animationPairing;
        ImportOptions = importOptions;
        PreviewOptions = previewOptions;
        RenderMetadata = renderMetadata;
        Diagnostics = ExternalModelDiagnosticMapper.SortAndCollapse(diagnostics ?? []);
        Eligibility = eligibility ?? PreviewEligibility.NotEvaluated;
    }
}
