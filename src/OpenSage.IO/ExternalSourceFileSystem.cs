#nullable enable

using System;
using System.Collections.Generic;
using System.IO;

namespace OpenSage.IO;

/// <summary>
/// Read-only wrapper over one disk root that only accepts normalized relative virtual keys.
/// Empty, rooted, escaping, dot-segment, alternate-separator, and otherwise non-normalized
/// keys throw before delegation; resolved physical paths must remain under the canonical
/// root, so symlinked files or directories cannot escape it.
/// </summary>
public sealed class ExternalSourceFileSystem : FileSystem
{
    private readonly DiskFileSystem _inner;
    private readonly string _canonicalRoot;
    private readonly StringComparison _pathComparison;

    public ExternalSourceFileSystem(DiskFileSystem inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        _canonicalRoot = Path.GetFullPath(inner.RootDirectory);
        _canonicalRoot = Path.TrimEndingDirectorySeparator(_canonicalRoot);
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        AddDisposable(inner);
    }

    public override FileSystemEntry? GetFile(string filePath)
    {
        ThrowIfDisposed();

        var normalizedKey = ValidateKey(filePath, allowRoot: false);
        var entry = _inner.GetFile(normalizedKey);
        if (entry is null)
        {
            return null;
        }

        EnsureEntryPhysicallyUnderRoot(entry);

        return entry;
    }

    public override IEnumerable<FileSystemEntry> GetFilesInDirectory(
        string directoryPath,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        ThrowIfDisposed();

        var normalizedKey = ValidateKey(directoryPath, allowRoot: true);

        foreach (var entry in _inner.GetFilesInDirectory(normalizedKey, searchPattern, searchOption))
        {
            if (IsEntryPhysicallyUnderRoot(entry))
            {
                yield return entry;
            }
        }
    }

    private string ValidateKey(string key, bool allowRoot)
    {
        if (key is null)
        {
            throw new InvalidDataException("External source keys must not be null.");
        }

        if (key.Length == 0)
        {
            if (allowRoot)
            {
                return "";
            }

            throw new InvalidDataException("External source keys must not be empty.");
        }

        if (key.Contains('\\'))
        {
            throw new InvalidDataException(
                $"External source key '{key}' uses an alternate separator; only '/' is accepted.");
        }

        if (key[0] == '/')
        {
            throw new InvalidDataException($"External source key '{key}' is rooted; relative keys only.");
        }

        if (key.Contains(':'))
        {
            throw new InvalidDataException($"External source key '{key}' contains a drive or scheme separator.");
        }

        foreach (var segment in key.Split('/'))
        {
            if (segment.Length == 0)
            {
                throw new InvalidDataException(
                    $"External source key '{key}' contains an empty path segment.");
            }

            if (segment is "." or "..")
            {
                throw new InvalidDataException(
                    $"External source key '{key}' contains a dot segment; normalized keys only.");
            }

            if (string.IsNullOrWhiteSpace(segment))
            {
                throw new InvalidDataException(
                    $"External source key '{key}' contains a whitespace-only path segment.");
            }
        }

        EnsureCombinedPathUnderRoot(key);

        return key;
    }

    private void EnsureCombinedPathUnderRoot(string key)
    {
        var combined = Path.Combine(_canonicalRoot, FileSystem.NormalizeFilePath(key));
        var resolved = Path.GetFullPath(combined);

        if (!IsUnderRoot(resolved))
        {
            throw new InvalidDataException(
                $"External source key '{key}' resolves outside the source root.");
        }
    }

    /// <summary>
    /// Resolves every path segment physically (following symlinks) and rejects entries whose
    /// physical location leaves the root at any hop.
    /// </summary>
    private void EnsureEntryPhysicallyUnderRoot(FileSystemEntry entry)
    {
        if (!IsEntryPhysicallyUnderRoot(entry))
        {
            throw new InvalidDataException(
                $"External source entry '{entry.FilePath}' resolves outside the source root; " +
                "symlink escapes are rejected.");
        }
    }

    private bool IsEntryPhysicallyUnderRoot(FileSystemEntry entry)
    {
        var physical = _canonicalRoot;
        var segments = entry.FilePath.Split('/', '\\');

        for (var i = 0; i < segments.Length - 1; i++)
        {
            var directory = Path.Combine(physical, segments[i]);
            physical = new DirectoryInfo(directory).ResolveLinkTarget(returnFinalTarget: true)?.FullName
                ?? directory;

            if (!IsUnderRoot(physical))
            {
                return false;
            }
        }

        var filePath = Path.Combine(physical, segments[^1]);
        var resolvedFile = new FileInfo(filePath).ResolveLinkTarget(returnFinalTarget: true)?.FullName
            ?? filePath;

        return IsUnderRoot(resolvedFile);
    }

    private bool IsUnderRoot(string resolvedPath)
    {
        return string.Equals(resolvedPath, _canonicalRoot, _pathComparison) ||
            resolvedPath.StartsWith(_canonicalRoot + Path.DirectorySeparatorChar, _pathComparison);
    }

    private void ThrowIfDisposed()
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(ExternalSourceFileSystem));
        }
    }
}
