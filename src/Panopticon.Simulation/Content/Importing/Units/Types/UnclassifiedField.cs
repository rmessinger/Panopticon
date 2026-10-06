#nullable enable

namespace Panopticon.Content.Importing.Units;

/// <summary>
/// A source field the adapter could not map. Non-gameplay fields only warn; a gameplay field is an
/// unsupported mechanic and blocks publication.
/// </summary>
public sealed record UnclassifiedField(string Name, bool AffectsGameplay, Provenance? Provenance = null);
