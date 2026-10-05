#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Units;

/// <summary>The required closure in discovery order (roots first) and resolution diagnostics.</summary>
public sealed record DependencyResult(
    IReadOnlyList<SourceRecord> Closure,
    IReadOnlyList<ImportDiagnostic> Diagnostics);
