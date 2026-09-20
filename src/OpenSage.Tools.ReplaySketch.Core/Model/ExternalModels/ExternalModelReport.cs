#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using OpenSage.Content.Importing;

namespace OpenSage.Tools.ReplaySketch.Model;

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
        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        return new ExternalSourceIdentity(
            displayName,
            virtualKey,
            content.Length,
            ComputeContentHash(content));
    }

    public static string ComputeContentHash(Stream content)
    {
        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

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

    public ExternalSourceIdentity? ModelSource { get; }

    public ExternalSourceIdentity? AnimationSource { get; }

    public ExternalAssetCatalog? Catalog { get; }

    public IReadOnlyList<ExternalModelDiagnostic> Diagnostics { get; }

    public PreviewEligibility Eligibility { get; }

    public ExternalModelReport(
        ExternalSourceIdentity? modelSource,
        ExternalSourceIdentity? animationSource,
        ExternalAssetCatalog? catalog,
        IReadOnlyList<ExternalModelDiagnostic>? diagnostics,
        PreviewEligibility? eligibility)
    {
        ModelSource = modelSource;
        AnimationSource = animationSource;
        Catalog = catalog;
        Diagnostics = ExternalModelDiagnosticMapper.SortAndCollapse(diagnostics ?? []);
        Eligibility = eligibility ?? PreviewEligibility.NotEvaluated;
    }
}
