#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Units.Materialization;

/// <summary>Detached runtime assets for a closure, not yet registered anywhere.</summary>
public sealed record MaterializedUnitContent(
    ContentSource Source,
    IReadOnlyDictionary<ContentId, BaseAsset> Assets,
    IReadOnlyList<AppliedConversion> Conversions,
    IReadOnlyList<ImportDiagnostic> Diagnostics)
{
    public bool Succeeded => !Diagnostics.Any(diagnostic => diagnostic.IsError);

    public T Get<T>(ContentId id)
        where T : BaseAsset
    {
        return (T)Assets[id];
    }
}
