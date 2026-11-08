#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Units;

/// <summary>
/// A source field the adapter could not map. Non-gameplay fields only warn; a gameplay field is an
/// unsupported mechanic and blocks publication unless the caller's import policy explicitly
/// excluded it, which is reported as a warning naming the exclusion.
/// </summary>
public sealed record UnclassifiedField(
    string Name,
    bool AffectsGameplay,
    Provenance? Provenance = null,
    bool ExcludedByPolicy = false,
    string? RawValue = null,
    IReadOnlyList<string>? RawBlock = null)
{
    public IReadOnlyList<string>? RawBlock { get; init; } = RawBlock?.ToArray();
}
