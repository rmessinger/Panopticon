#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Units;

public sealed record InheritanceResult(
    IReadOnlyDictionary<ContentId, SourceRecord> Resolved,
    IReadOnlyList<ImportDiagnostic> Diagnostics);
