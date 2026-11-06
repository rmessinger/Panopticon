#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Maps;

/// <summary>Source and generation provenance. <c>SourceSchemaVersion</c> is the source payload version, never the interchange schema version.</summary>
public sealed record Provenance(
    string? SourceSchemaVersion,
    IReadOnlyList<SourceRecord> Sources,
    IReadOnlyList<GeneratedFeature> GeneratedFeatures);
