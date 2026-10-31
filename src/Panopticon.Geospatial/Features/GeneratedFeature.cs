using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Panopticon.Geospatial.Interchange;

namespace Panopticon.Geospatial.Features;

/// <summary>
/// Immutable mapped feature with retained source provenance. Point positions carry
/// sampled game-unit Z separately from source geometry altitude metadata.
/// </summary>
public sealed record GeneratedFeature
{
    // Wrapped read-only, not a bare Dictionary: the shared instance must not be castable
    // back to a mutable dictionary.
    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    public GeneratedFeature(
        GeneratedFeatureKind kind,
        GeoGeometry geometry,
        string sourceId,
        string featureId,
        string partId,
        int ruleIndex,
        IReadOnlyDictionary<string, string>? attributes = null,
        GamePosition? position = null,
        GeneratedObjectPlacement? objectPlacement = null,
        bool? passable = null,
        bool? buildable = null)
        : this(kind, geometry, sourceId, featureId, partId, ruleIndex, attributes, position, objectPlacement, passable, buildable, copyAttributes: true)
    {
    }

    /// <summary>
    /// Trusted path for the mapper: shares an already defensively immutable dictionary
    /// (<see cref="GeospatialFeature.Attributes"/>) across outputs from one source
    /// instead of copying it per output.
    /// </summary>
    internal static GeneratedFeature CreateShared(
        GeneratedFeatureKind kind,
        GeoGeometry geometry,
        string sourceId,
        string featureId,
        string partId,
        int ruleIndex,
        IReadOnlyDictionary<string, string>? attributes,
        GamePosition? position,
        GeneratedObjectPlacement? objectPlacement,
        bool? passable,
        bool? buildable) =>
        new(kind, geometry, sourceId, featureId, partId, ruleIndex, attributes, position, objectPlacement, passable, buildable, copyAttributes: false);

    private GeneratedFeature(
        GeneratedFeatureKind kind,
        GeoGeometry geometry,
        string sourceId,
        string featureId,
        string partId,
        int ruleIndex,
        IReadOnlyDictionary<string, string>? attributes,
        GamePosition? position,
        GeneratedObjectPlacement? objectPlacement,
        bool? passable,
        bool? buildable,
        bool copyAttributes)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Undefined generated feature kind.");
        }

        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureId);
        ArgumentException.ThrowIfNullOrWhiteSpace(partId);
        ArgumentOutOfRangeException.ThrowIfLessThan(ruleIndex, -1);

        var isPoint = geometry is GeoPointGeometry;
        if (isPoint && position is null)
        {
            throw new ArgumentException("Point geometry requires a game position carrying the sampled terrain Z.", nameof(position));
        }

        if (!isPoint && position is not null)
        {
            throw new ArgumentException("Only point geometry carries a game position.", nameof(position));
        }

        if (kind == GeneratedFeatureKind.ObjectCandidate && objectPlacement is null)
        {
            throw new ArgumentException("Object candidates require object placement parameters.", nameof(objectPlacement));
        }

        if (kind != GeneratedFeatureKind.ObjectCandidate && objectPlacement is not null)
        {
            throw new ArgumentException("Object placement parameters apply to object candidates only.", nameof(objectPlacement));
        }

        if (kind == GeneratedFeatureKind.Passability && passable is null)
        {
            throw new ArgumentException("Passability regions require a passable flag.", nameof(passable));
        }

        if (kind != GeneratedFeatureKind.Passability && passable is not null)
        {
            throw new ArgumentException("The passable flag applies to passability regions only.", nameof(passable));
        }

        if (kind == GeneratedFeatureKind.Buildability && buildable is null)
        {
            throw new ArgumentException("Buildability regions require a buildable flag.", nameof(buildable));
        }

        if (kind != GeneratedFeatureKind.Buildability && buildable is not null)
        {
            throw new ArgumentException("The buildable flag applies to buildability regions only.", nameof(buildable));
        }

        Kind = kind;
        Geometry = geometry;
        SourceId = sourceId;
        FeatureId = featureId;
        PartId = partId;
        RuleIndex = ruleIndex;
        Attributes = copyAttributes ? CopyAttributes(attributes) : attributes ?? EmptyAttributes;
        Position = position;
        ObjectPlacement = objectPlacement;
        Passable = passable;
        Buildable = buildable;
    }

    public GeneratedFeatureKind Kind { get; }

    /// <summary>Clipped local geometry in game coordinates; source altitude metadata stays attached for provenance.</summary>
    public GeoGeometry Geometry { get; }

    public string SourceId { get; }

    public string FeatureId { get; }

    /// <summary>Stable identity of the clipped source part: SourceId/FeatureId#PartIndex.</summary>
    public string PartId { get; }

    /// <summary>Index of the governing rule in the manifest feature-mapping list; -1 for an unmatched annotation.</summary>
    public int RuleIndex { get; }

    /// <summary>Source attributes; the public constructor copies, the mapper shares the source's immutable dictionary.</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; }

    /// <summary>Game-units position for point geometry; Z is the sampled terrain height. Null for lines and polygons.</summary>
    public GamePosition? Position { get; }

    /// <summary>Object definition and resolved orientation; object candidates only.</summary>
    public GeneratedObjectPlacement? ObjectPlacement { get; }

    /// <summary>Passable flag carried by the matching rule; passability regions only.</summary>
    public bool? Passable { get; }

    /// <summary>Buildable flag carried by the matching rule; buildability regions only.</summary>
    public bool? Buildable { get; }

    private static IReadOnlyDictionary<string, string> CopyAttributes(IReadOnlyDictionary<string, string>? attributes)
    {
        if (attributes is null || attributes.Count == 0)
        {
            return EmptyAttributes;
        }

        var copy = new Dictionary<string, string>(attributes.Count, StringComparer.Ordinal);
        foreach (var pair in attributes)
        {
            if (pair.Key is null || pair.Value is null)
            {
                throw new ArgumentException("Attribute keys and values must not be null.", nameof(attributes));
            }

            copy.Add(pair.Key, pair.Value);
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }
}
