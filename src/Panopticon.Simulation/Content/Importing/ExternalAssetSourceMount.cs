#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Panopticon;
using Panopticon.Content.Importing.Gltf;
using Panopticon.IO;

namespace Panopticon.Content.Importing;

/// <summary>
/// Disposable mount of one absolute selected external source file. The file's directory is
/// the canonical root, the file name is the root virtual key, and the import context sees
/// only a dedicated <see cref="ExternalSourceFileSystem"/>. Model and animation mounts are
/// independent instances; neither is layered into a game installation filesystem.
/// </summary>
public sealed class ExternalAssetSourceMount : IDisposable
{
    private readonly ExternalSourceFileSystem _fileSystem;

    public string CanonicalRootPath { get; }

    public string RootVirtualKey { get; }

    public ExternalFormatSelection FormatSelection { get; }

    public AssetImportContext ImportContext { get; }

    public ExternalAssetSourceMount(
        string absoluteSelectedFile,
        GltfImportOptions? options = null,
        IEnumerable<string>? enabledFeatureGates = null)
    {
        Ensure.NotNullOrWhiteSpace(absoluteSelectedFile, "Selected file path must not be empty.");

        if (!Path.IsPathRooted(absoluteSelectedFile))
        {
            throw new ArgumentException(
                $"Selected file path '{absoluteSelectedFile}' must be absolute.",
                nameof(absoluteSelectedFile));
        }

        var fileName = Path.GetFileName(absoluteSelectedFile);
        Ensure.NotNullOrEmpty(
            fileName,
            "Selected file path must name a file, not end at a directory.",
            nameof(absoluteSelectedFile));

        var selectionResult = ExternalAssetInspection.SelectFormat(fileName, options);
        if (!selectionResult.IsSuccess || selectionResult.Selection is not { } selection)
        {
            throw new InvalidDataException(
                $"{ExternalFormatSelectionResult.UnsupportedFormatCode}: '{fileName}' does not use a supported external model format.");
        }

        CanonicalRootPath = Path.GetDirectoryName(Path.GetFullPath(absoluteSelectedFile))
            ?? throw new ArgumentException(
                $"Selected file path '{absoluteSelectedFile}' has no parent directory.",
                nameof(absoluteSelectedFile));

        RootVirtualKey = fileName;
        FormatSelection = selection;
        _fileSystem = new ExternalSourceFileSystem(new DiskFileSystem(CanonicalRootPath));
        ImportContext = new AssetImportContext(_fileSystem, language: null, enabledFeatureGates);
    }

    public void Dispose()
    {
        // FileSystem.Dispose is idempotent via DisposableBase.
        _fileSystem.Dispose();
    }
}
