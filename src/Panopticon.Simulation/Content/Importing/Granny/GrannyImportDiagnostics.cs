#nullable enable

using System.Collections.Generic;

namespace Panopticon.Content.Importing.Granny;

/// <summary>
/// Per-import diagnostics for the Granny and AoE3 material pipelines: non-fatal notes such as
/// missing material files or textures, recorded instead of thrown.
/// </summary>
internal sealed class GrannyImportDiagnostics
{
    private readonly List<string> _warnings = [];

    public IReadOnlyList<string> Warnings => _warnings;

    public void Warn(string message)
    {
        _warnings.Add(message);
    }
}
