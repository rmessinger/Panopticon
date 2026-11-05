using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Panopticon.Geospatial.Payload.Models;

/// <summary>
/// Root of <c>report.json</c>: serialized diagnostics, generation summaries, ordered
/// validation checks, and the exact SHA-256 of the committed <c>payload.json</c> bytes.
/// Carries no wall-clock value, output path, exception detail, or self-hash.
/// </summary>
public sealed record GeneratedMapPayloadReport
{
    public const string ReportKind = "panopticon-geospatial-payload-report";

    public const int CurrentSchemaVersion = 1;

    public GeneratedMapPayloadReport(
        IReadOnlyList<PayloadDiagnostic> diagnostics,
        PayloadTerrainGenerationSummary terrain,
        PayloadMaskGenerationSummary masks,
        PayloadFeatureGenerationSummary features,
        IReadOnlyList<PayloadValidationCheck> validationChecks,
        string payloadSha256Hex,
        PayloadImageryGenerationSummary? imagery = null,
        PayloadSamplingSummary? classificationSampling = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(masks);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(validationChecks);
        PayloadValidationRules.Sha256Hex(payloadSha256Hex, nameof(payloadSha256Hex));

        Diagnostics = new ReadOnlyCollection<PayloadDiagnostic>(CopyItems(diagnostics, nameof(diagnostics)));
        Terrain = terrain;
        Masks = masks;
        Features = features;
        ValidationChecks = new ReadOnlyCollection<PayloadValidationCheck>(
            CopyItems(validationChecks, nameof(validationChecks)));
        PayloadSha256Hex = payloadSha256Hex;
        Imagery = imagery;
        ClassificationSampling = classificationSampling;
    }

    /// <summary>Fixed report kind discriminator, <c>panopticon-geospatial-payload-report</c>.</summary>
    public string Kind => ReportKind;

    public int SchemaVersion => CurrentSchemaVersion;

    /// <summary>Serialized diagnostics; exception detail and absolute paths are excluded.</summary>
    public IReadOnlyList<PayloadDiagnostic> Diagnostics { get; }

    public PayloadTerrainGenerationSummary Terrain { get; }

    /// <summary>Imagery generation summary; null when the payload has no imagery layer.</summary>
    public PayloadImageryGenerationSummary? Imagery { get; }

    /// <summary>Classification sampling summary; null when no classification raster was sampled.</summary>
    public PayloadSamplingSummary? ClassificationSampling { get; }

    public PayloadMaskGenerationSummary Masks { get; }

    public PayloadFeatureGenerationSummary Features { get; }

    /// <summary>Checks in the order the validators declared them.</summary>
    public IReadOnlyList<PayloadValidationCheck> ValidationChecks { get; }

    /// <summary>SHA-256 of the exact <c>payload.json</c> bytes this report accompanies.</summary>
    public string PayloadSha256Hex { get; }

    private static T[] CopyItems<T>(IReadOnlyList<T> items, string paramName)
    {
        var copy = new T[items.Count];

        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is null)
            {
                throw new ArgumentException("Sequence items must not be null.", paramName);
            }

            copy[index] = items[index];
        }

        return copy;
    }
}
