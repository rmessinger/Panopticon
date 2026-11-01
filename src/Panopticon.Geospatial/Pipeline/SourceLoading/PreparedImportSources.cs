using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Panopticon.Geospatial.Interchange;
using Panopticon.Geospatial.Manifest;

namespace Panopticon.Geospatial.Pipeline.SourceLoading;

/// <summary>
/// Immutable source-preparation result: the loaded manifest with its SHA-256, the protected
/// full input paths, per-source provenance in manifest order, combined vector features with
/// manifest source ids, the selected extent, and rasters by role. Collections are copied on
/// construction.
/// </summary>
internal sealed record PreparedImportSources
{
    public PreparedImportSources(
        GeospatialImportManifest loadedManifest,
        string manifestSha256,
        IReadOnlyList<string>? protectedInputPaths,
        IReadOnlyList<LoadedSource> sources,
        IReadOnlyList<GeospatialFeature> vectorFeatures,
        SelectedExtentFeature selectedExtent,
        LoadedRaster primaryElevation,
        LoadedRaster? imagery = null,
        LoadedRaster? classification = null)
    {
        ArgumentNullException.ThrowIfNull(loadedManifest);
        ArgumentException.ThrowIfNullOrEmpty(manifestSha256);
        ArgumentNullException.ThrowIfNull(selectedExtent);
        ArgumentNullException.ThrowIfNull(primaryElevation);

        if (manifestSha256.Length != 64)
        {
            throw new ArgumentException("The manifest SHA-256 must be 64 hexadecimal characters.", nameof(manifestSha256));
        }

        LoadedManifest = loadedManifest;
        ManifestSha256 = manifestSha256;
        ProtectedInputPaths = CopyPaths(protectedInputPaths);
        Sources = CopyItems(sources, nameof(sources));
        VectorFeatures = CopyItems(vectorFeatures, nameof(vectorFeatures));
        SelectedExtent = selectedExtent;
        PrimaryElevation = primaryElevation;
        Imagery = imagery;
        Classification = classification;
    }

    /// <summary>The exact manifest instance <see cref="ManifestLoader"/> produced for this import.</summary>
    public GeospatialImportManifest LoadedManifest { get; }

    /// <summary>SHA-256 of the manifest bytes as 64 lowercase hex characters.</summary>
    public string ManifestSha256 { get; }

    /// <summary>
    /// Full paths of the manifest and of every resolved input source, deduplicated in first
    /// occurrence order. Output placement must reject directories containing any of them.
    /// </summary>
    public IReadOnlyList<string> ProtectedInputPaths { get; }

    /// <summary>Loaded sources in manifest order.</summary>
    public IReadOnlyList<LoadedSource> Sources { get; }

    /// <summary>
    /// Features of every vector-role source plus the extent source, in manifest source order
    /// and reader order within each source, with source ids bound to manifest source ids.
    /// </summary>
    public IReadOnlyList<GeospatialFeature> VectorFeatures { get; }

    /// <summary>The exactly one selected extent polygon with its owning feature.</summary>
    public SelectedExtentFeature SelectedExtent { get; }

    public LoadedRaster PrimaryElevation { get; }

    public LoadedRaster? Imagery { get; }

    public LoadedRaster? Classification { get; }

    /// <summary>
    /// Effective vertical datum of the elevation source: the declared datum when known,
    /// otherwise the datum embedded in the file when known, otherwise unknown. Metadata
    /// only; no geoid or unit conversion runs.
    /// </summary>
    public string EffectiveDemDatum => PrimaryElevation.Source.EffectiveVerticalDatum;

    private static ReadOnlyCollection<string> CopyPaths(IReadOnlyList<string>? paths)
    {
        if (paths is null || paths.Count == 0)
        {
            return new ReadOnlyCollection<string>(Array.Empty<string>());
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var copied = new List<string>();

        foreach (var path in paths)
        {
            if (path is null)
            {
                throw new ArgumentException("Protected input paths must not be null.", nameof(paths));
            }

            if (seen.Add(path))
            {
                copied.Add(path);
            }
        }

        return new ReadOnlyCollection<string>(copied);
    }

    private static ReadOnlyCollection<T> CopyItems<T>(IReadOnlyList<T>? items, string paramName)
    {
        if (items is null || items.Count == 0)
        {
            return new ReadOnlyCollection<T>(Array.Empty<T>());
        }

        var copied = new T[items.Count];

        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is null)
            {
                throw new ArgumentException("Items must not be null.", paramName);
            }

            copied[i] = items[i];
        }

        return new ReadOnlyCollection<T>(copied);
    }
}
