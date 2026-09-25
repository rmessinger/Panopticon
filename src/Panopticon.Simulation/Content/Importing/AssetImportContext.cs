#nullable enable

using System;
using Panopticon.IO;

namespace Panopticon.Content.Importing;

/// <summary>
/// Import-only view of the load environment: the virtual file system and language. No graphics
/// device, shader store, or asset store; importers never allocate runtime resources.
/// </summary>
public sealed class AssetImportContext
{
    public FileSystem FileSystem { get; }

    public string? Language { get; }

    public AssetImportContext(FileSystem fileSystem, string? language)
    {
        if (fileSystem is null)
        {
            throw new ArgumentNullException(nameof(fileSystem));
        }

        FileSystem = fileSystem;
        Language = language;
    }
}
