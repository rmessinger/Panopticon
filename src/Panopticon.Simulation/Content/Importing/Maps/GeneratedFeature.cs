#nullable enable

using System;
using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>A generated feature with a stable ID and string attributes.</summary>
public sealed record GeneratedFeature
{
    public GeneratedFeature(string stableId, string kind, IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(stableId);
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(attributes);
        StableId = stableId;
        Kind = kind;
        Attributes = new Dictionary<string, string>(attributes, StringComparer.Ordinal);
    }

    public string StableId { get; }

    public string Kind { get; }

    /// <summary>Defensive copy with ordinal key comparison; the source dictionary is not retained.</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; }
}
