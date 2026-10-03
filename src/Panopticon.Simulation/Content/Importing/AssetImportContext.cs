#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

using Panopticon.IO;

namespace Panopticon.Content.Importing;

/// <summary>
/// Import-only view of the load environment: the virtual file system, language, and enabled
/// feature gates. No graphics device, shader store, or asset store; importers never allocate
/// runtime resources.
/// </summary>
public sealed class AssetImportContext
{
    public FileSystem FileSystem { get; }

    public string? Language { get; }

    public IReadOnlySet<string> EnabledFeatureGates { get; }

    public AssetImportContext(
        FileSystem fileSystem,
        string? language,
        IEnumerable<string>? enabledFeatureGates = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        FileSystem = fileSystem;
        Language = language;
        EnabledFeatureGates = enabledFeatureGates?.ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);
    }

    public bool IsFeatureGateEnabled(string name)
    {
        return EnabledFeatureGates.Contains(name);
    }
}
