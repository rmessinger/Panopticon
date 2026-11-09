#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Panopticon.Content.Importing.Units.Packages;
using Panopticon.IO;

namespace Panopticon.Tests.Content.IniSessionReload;

/// <summary>
/// Scratch content-root file system that records every request. A request throws when it names a
/// manifest.json or lies under a directory that contains one.
/// </summary>
internal sealed class ContentRootProbe : FileSystem
{
    private readonly string _rootFullPath;
    private readonly DiskFileSystem _inner;
    private readonly List<string> _requests = new();

    public ContentRootProbe(string rootDirectory)
    {
        _rootFullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        _inner = new DiskFileSystem(rootDirectory);
        AddDisposable(_inner);
    }

    public IReadOnlyList<string> Requests => _requests;

    public static bool IsManifest(string path) =>
        string.Equals(Path.GetFileName(path), UnitPackageFormat.ManifestFileName, StringComparison.OrdinalIgnoreCase);

    public override FileSystemEntry? GetFile(string filePath)
    {
        Record(filePath);
        return _inner.GetFile(filePath);
    }

    public override IEnumerable<FileSystemEntry> GetFilesInDirectory(
        string directoryPath,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        Record(directoryPath);
        return _inner.GetFilesInDirectory(directoryPath, searchPattern, searchOption);
    }

    private void Record(string path)
    {
        _requests.Add(path);
        if (IsManifest(path) || IsUnderPackageDirectory(path))
        {
            throw new InvalidOperationException($"Content root access touched package data: '{path}'.");
        }
    }

    private bool IsUnderPackageDirectory(string path)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_rootFullPath, NormalizeFilePath(path)));
        for (var directory = fullPath; directory is not null && IsWithinRoot(directory); directory = Path.GetDirectoryName(directory))
        {
            if (File.Exists(Path.Combine(directory, UnitPackageFormat.ManifestFileName)))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsWithinRoot(string fullPath) =>
        fullPath.Length == _rootFullPath.Length
        || (fullPath.StartsWith(_rootFullPath, StringComparison.Ordinal)
            && fullPath[_rootFullPath.Length] == Path.DirectorySeparatorChar);
}
