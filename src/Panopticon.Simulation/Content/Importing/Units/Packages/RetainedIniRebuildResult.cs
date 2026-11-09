#nullable enable

using System.Collections.Generic;
using System.Linq;
using Panopticon.Content.Importing.Units.Materialization;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>Result of rebuilding native templates and re-issuing handles from retained INI blocks.</summary>
public sealed class RetainedIniRebuildResult
{
    private readonly IReadOnlyDictionary<ContentId, SourceRecord> _rebuiltRecords;
    private readonly IReadOnlyList<ImportDiagnostic> _diagnostics;

    public bool Succeeded => !_diagnostics.Any(d => d.IsError);

    public IReadOnlyDictionary<ContentId, SourceRecord> RebuiltRecords => _rebuiltRecords;

    public ILegacyNativeTable? Natives { get; }

    public IReadOnlyList<ImportDiagnostic> Diagnostics => _diagnostics;

    public RetainedIniRebuildResult(
        IDictionary<ContentId, SourceRecord>? rebuiltRecords,
        ILegacyNativeTable? natives,
        IEnumerable<ImportDiagnostic>? diagnostics)
    {
        _rebuiltRecords = rebuiltRecords is not null
            ? new Dictionary<ContentId, SourceRecord>(rebuiltRecords)
            : new Dictionary<ContentId, SourceRecord>();
        Natives = natives;
        _diagnostics = diagnostics?.ToArray() ?? [];
    }
}
