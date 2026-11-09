#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>
/// Result of loading a staged pack.
/// </summary>
public sealed record StagedPackLoadResult(
    bool Succeeded,
    IReadOnlyList<ImportDiagnostic> Diagnostics,
    StagedPackPublication? Publication)
{
    public IEnumerable<ImportDiagnostic> Errors => Diagnostics.Where(d => d.IsError);
}
