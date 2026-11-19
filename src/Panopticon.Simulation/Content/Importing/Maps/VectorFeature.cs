#nullable enable

using System;
using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>One emitted vector feature with its source identity, classification rule, attributes, and geometry.</summary>
public sealed record VectorFeature
{
    public VectorFeature(
        string sourceStableId,
        string sourceId,
        string kind,
        int ruleIndex,
        IReadOnlyDictionary<string, string> attributes,
        bool? passable,
        bool? buildable,
        string? coverage,
        VectorGeometry geometry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceStableId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(geometry);
        SourceStableId = sourceStableId;
        SourceId = sourceId;
        Kind = kind;
        RuleIndex = ruleIndex;
        Attributes = new Dictionary<string, string>(attributes, StringComparer.Ordinal);
        Passable = passable;
        Buildable = buildable;
        Coverage = coverage;
        Geometry = geometry;
    }

    public string SourceStableId { get; }

    public string SourceId { get; }

    public string Kind { get; }

    /// <summary>Index of the emission rule that produced this feature; -1 when no rule applies.</summary>
    public int RuleIndex { get; }

    /// <summary>Defensive copy with ordinal key comparison; the source dictionary is not retained.</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; }

    public bool? Passable { get; }

    public bool? Buildable { get; }

    public string? Coverage { get; }

    public VectorGeometry Geometry { get; }
}
