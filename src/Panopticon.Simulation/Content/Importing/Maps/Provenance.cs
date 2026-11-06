#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Source and generation provenance. <c>SourceSchemaVersion</c> is the source payload version, never the interchange schema version. Collections are copied at construction.</summary>
public sealed record Provenance
{
    public Provenance(string? sourceSchemaVersion, IReadOnlyList<SourceRecord> sources, IReadOnlyList<GeneratedFeature> generatedFeatures)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(generatedFeatures);
        SourceSchemaVersion = sourceSchemaVersion;
        Sources = sources.ToArray();
        GeneratedFeatures = generatedFeatures.ToArray();
    }

    public string? SourceSchemaVersion { get; }

    public IReadOnlyList<SourceRecord> Sources { get; }

    public IReadOnlyList<GeneratedFeature> GeneratedFeatures { get; }
}
