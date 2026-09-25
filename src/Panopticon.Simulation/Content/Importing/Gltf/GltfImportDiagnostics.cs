#nullable enable

using System;
using System.Collections.Generic;

namespace Panopticon.Content.Importing.Gltf;

/// <summary>
/// Per-import diagnostics: at most one warning per ignored optional extension name, plus any
/// other non-fatal notes the importer records.
/// </summary>
internal sealed class GltfImportDiagnostics
{
    private readonly List<string> _warnings = [];
    private readonly HashSet<string> _warnedExtensionNames = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Warnings => _warnings;

    public void WarnOptionalExtensionIgnored(string extensionName)
    {
        if (_warnedExtensionNames.Add(extensionName))
        {
            _warnings.Add($"Optional glTF extension '{extensionName}' is not interpreted; core semantics are unaffected.");
        }
    }

    public void Warn(string message)
    {
        _warnings.Add(message);
    }
}
