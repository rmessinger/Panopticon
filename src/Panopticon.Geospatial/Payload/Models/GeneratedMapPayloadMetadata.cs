using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Panopticon.Geospatial.Features;
using Panopticon.Geospatial.Generation;
using Panopticon.Geospatial.Manifest;

namespace Panopticon.Geospatial.Payload.Models;

/// <summary>
/// Root of <c>payload.json</c>: kind and schema version, grid, default-first material
/// palette, generated features with shared source attributes, manifest hash, ordered
/// provenance, and southwest row-major chunk descriptors.
/// </summary>
public sealed record GeneratedMapPayloadMetadata
{
    public const string PayloadKind = "panopticon-geospatial-payload";

    public const int CurrentSchemaVersion = 1;

    public GeneratedMapPayloadMetadata(
        PayloadGridDefinition grid,
        MaterialPalette palette,
        IReadOnlyList<GeneratedFeature> features,
        string manifestSha256Hex,
        IReadOnlyList<PayloadSourceProvenance> provenance,
        IReadOnlyList<PayloadChunkDescriptor> chunks)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(chunks);
        PayloadValidationRules.Sha256Hex(manifestSha256Hex, nameof(manifestSha256Hex));

        var featureCopy = CopyItems(features, nameof(features));
        var provenanceCopy = CopyItems(provenance, nameof(provenance));
        var chunkCopy = CopyItems(chunks, nameof(chunks));

        ValidateProvenanceIds(provenanceCopy, nameof(provenance));
        ValidateChunkList(grid, chunkCopy, nameof(chunks));
        ValidateElevationDatum(grid.Transform.VerticalDatum, provenanceCopy, nameof(provenance));

        Grid = grid;
        Palette = palette;
        Features = new ReadOnlyCollection<GeneratedFeature>(featureCopy);
        ManifestSha256Hex = manifestSha256Hex;
        Provenance = new ReadOnlyCollection<PayloadSourceProvenance>(provenanceCopy);
        Chunks = new ReadOnlyCollection<PayloadChunkDescriptor>(chunkCopy);
    }

    /// <summary>Fixed payload kind discriminator, <c>panopticon-geospatial-payload</c>.</summary>
    public string Kind => PayloadKind;

    public int SchemaVersion => CurrentSchemaVersion;

    public PayloadGridDefinition Grid { get; }

    /// <summary>Ordered material names; entry 0 is the default material.</summary>
    public MaterialPalette Palette { get; }

    public IReadOnlyList<GeneratedFeature> Features { get; }

    /// <summary>SHA-256 of the import manifest bytes as 64 lowercase hex characters.</summary>
    public string ManifestSha256Hex { get; }

    /// <summary>Stable ordered provenance of every import source.</summary>
    public IReadOnlyList<PayloadSourceProvenance> Provenance { get; }

    /// <summary>Southwest row-major chunk descriptors: index <c>cy * chunksX + cx</c>.</summary>
    public IReadOnlyList<PayloadChunkDescriptor> Chunks { get; }

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

    private static void ValidateProvenanceIds(PayloadSourceProvenance[] provenance, string paramName)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < provenance.Length; index++)
        {
            if (!seen.Add(provenance[index].SourceId))
            {
                throw new ArgumentException(
                    $"The source id '{provenance[index].SourceId}' appears more than once in the provenance list.",
                    paramName);
            }
        }
    }

    /// <summary>
    /// Rejects a known map vertical datum that conflicts with a known datum of the
    /// elevation source; unknown values are permitted and resolved by the importer.
    /// </summary>
    private static void ValidateElevationDatum(string rootDatum, PayloadSourceProvenance[] provenance, string paramName)
    {
        if (PayloadValidationRules.TryKnownDatumKey(rootDatum) is null)
        {
            return;
        }

        foreach (var source in provenance)
        {
            if (source.Role != ManifestSourceRole.Elevation)
            {
                continue;
            }

            if (PayloadValidationRules.IsKnownDatumConflict(rootDatum, source.DeclaredVerticalDatum)
                || PayloadValidationRules.IsKnownDatumConflict(rootDatum, source.EmbeddedVerticalDatum))
            {
                throw new ArgumentException(
                    $"The map vertical datum '{rootDatum}' conflicts with the elevation source '{source.SourceId}'; no geoid conversion is performed.",
                    paramName);
            }
        }
    }

    private static void ValidateChunkList(PayloadGridDefinition grid, PayloadChunkDescriptor[] chunks, string paramName)
    {
        if (chunks.Length != grid.TotalChunkCount)
        {
            throw new ArgumentException(
                $"The grid declares {grid.TotalChunkCount} chunks; the descriptor list holds {chunks.Length}.",
                paramName);
        }

        var hasImagery = grid.ImageryPixelsPerSample is not null;
        for (var index = 0; index < chunks.Length; index++)
        {
            var descriptor = chunks[index];
            if (descriptor.ChunkX != index % grid.ChunksX || descriptor.ChunkY != index / grid.ChunksX)
            {
                throw new ArgumentException(
                    $"The chunk descriptor at index {index} is ({descriptor.ChunkX}, {descriptor.ChunkY}); "
                    + $"southwest row-major order requires ({index % grid.ChunksX}, {index / grid.ChunksX}).",
                    paramName);
            }

            if ((descriptor.Imagery is not null) != hasImagery)
            {
                throw new ArgumentException(
                    $"The chunk descriptor at index {index} "
                    + (hasImagery ? "is missing the imagery reference the grid declares." : "carries an imagery reference but the grid declares no imagery."),
                    paramName);
            }
        }
    }
}
