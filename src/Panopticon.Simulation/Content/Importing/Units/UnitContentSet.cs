#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Units;

/// <summary>
/// Everything one adapter read: records, the requested roots, and read-stage diagnostics. Duplicate
/// ids are kept so the resolver can report them instead of silently picking one.
/// </summary>
public sealed class UnitContentSet
{
    public ContentSource Source { get; }

    public IReadOnlyList<SourceRecord> Records { get; }

    /// <summary>Records the caller asked to publish; the dependency closure is computed from these.</summary>
    public IReadOnlyList<ContentId> Roots { get; }

    public IReadOnlyList<ImportDiagnostic> Diagnostics { get; }

    public UnitContentSet(
        ContentSource source,
        IEnumerable<SourceRecord> records,
        IEnumerable<ContentId> roots,
        IEnumerable<ImportDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(roots);

        Source = source;
        Records = records.ToArray();
        Roots = roots.ToArray();
        Diagnostics = diagnostics?.ToArray() ?? [];

        Ensure.NoNullElements(Records, "Record");
        Ensure.NoNullElements(Diagnostics, "Diagnostic");
    }
}
