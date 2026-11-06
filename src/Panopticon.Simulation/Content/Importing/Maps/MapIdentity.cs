#nullable enable

namespace Panopticon.Content.Importing.Maps;

/// <summary>Map identity. <c>LegacyGlobalVersion</c> is source provenance, not the interchange schema version.</summary>
public sealed record MapIdentity(string Name, MapSourceKind SourceKind, int? LegacyGlobalVersion);
