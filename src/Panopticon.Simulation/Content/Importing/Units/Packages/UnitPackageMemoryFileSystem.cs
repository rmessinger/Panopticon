#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.IO;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>In-memory file system for staging retained INI blocks during package reads.</summary>
public sealed class UnitPackageMemoryFileSystem : FileSystem
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    public void AddFile(string path, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(content);
        _files[NormalizeFilePath(path)] = content;
    }

    public override FileSystemEntry? GetFile(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        var normalized = NormalizeFilePath(filePath);

        if (_files.TryGetValue(normalized, out var bytes))
        {
            return new FileSystemEntry(this, normalized, (uint)bytes.Length, () => new MemoryStream(bytes));
        }

        return null;
    }

    public override IEnumerable<FileSystemEntry> GetFilesInDirectory(
        string directoryPath,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        return Enumerable.Empty<FileSystemEntry>();
    }
}
