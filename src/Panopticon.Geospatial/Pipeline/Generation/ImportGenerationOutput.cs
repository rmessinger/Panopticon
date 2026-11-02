using System;
using Panopticon.Geospatial.Payload.Models;

namespace Panopticon.Geospatial.Pipeline.Generation;

/// <summary>
/// Immutable result of chunk generation: the assembled payload metadata (the payload
/// hash itself is only known once the metadata write happens) plus the per-layer
/// generation summaries converted from the runtime accumulators. Terrain, imagery, and
/// classification sampling statistics stay separate; they are never merged.
/// </summary>
internal sealed record ImportGenerationOutput
{
    internal ImportGenerationOutput(
        GeneratedMapPayloadMetadata metadata,
        PayloadTerrainGenerationSummary terrain,
        PayloadImageryGenerationSummary? imagery,
        PayloadMaskGenerationSummary masks,
        PayloadFeatureGenerationSummary features,
        PayloadSamplingSummary classificationSampling)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(masks);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(classificationSampling);

        Metadata = metadata;
        Terrain = terrain;
        Imagery = imagery;
        Masks = masks;
        Features = features;
        ClassificationSampling = classificationSampling;
    }

    /// <summary>Payload metadata with the southwest row-major chunk descriptors; not yet written.</summary>
    public GeneratedMapPayloadMetadata Metadata { get; }

    /// <summary>Terrain generation summary over every streamed chunk, aprons included.</summary>
    public PayloadTerrainGenerationSummary Terrain { get; }

    /// <summary>Imagery generation summary; null when the import carries no imagery layer.</summary>
    public PayloadImageryGenerationSummary? Imagery { get; }

    public PayloadMaskGenerationSummary Masks { get; }

    public PayloadFeatureGenerationSummary Features { get; }

    /// <summary>Classification sampling summary of the material stage, independent of terrain and imagery.</summary>
    public PayloadSamplingSummary ClassificationSampling { get; }
}
