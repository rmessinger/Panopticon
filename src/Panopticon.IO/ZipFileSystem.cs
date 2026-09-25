using System.IO.Compression;

namespace Panopticon.IO;

/// <summary>
/// Read-only file system over an ordinary ZIP archive. Entry paths are
/// normalized to forward slashes (the ZIP canonical separator, regardless of
/// host OS) and looked up case-insensitively; duplicate normalized names
/// are rejected instead of resolved by archive order. The archive stays
/// alive until the file system is disposed and every open returns a fresh
/// stream.
/// </summary>
public sealed class ZipFileSystem : FileSystem
{
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, IndexedEntry> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _directories = new(StringComparer.OrdinalIgnoreCase);

    private sealed record IndexedEntry(string CanonicalPath, ZipArchiveEntry Entry);

    private const char Separator = '/';

    private static string NormalizeZipPath(string path)
    {
        return path.Replace('\\', Separator);
    }

    public ZipFileSystem(Stream stream)
    {
        _archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        IndexEntries();
    }

    public ZipFileSystem(string path)
    {
        _archive = ZipFile.OpenRead(path);
        IndexEntries();
    }

    private void IndexEntries()
    {
        foreach (var entry in _archive.Entries)
        {
            var normalizedPath = NormalizeZipPath(entry.FullName).Trim(Separator);
            if (normalizedPath.Length == 0)
            {
                continue;
            }

            if (_files.ContainsKey(normalizedPath))
            {
                throw new InvalidDataException(
                    $"ZIP archive contains duplicate entries for '{normalizedPath}' " +
                    $"(e.g. '{entry.FullName}'); ambiguous archives are rejected.");
            }

            _files[normalizedPath] = new IndexedEntry(normalizedPath, entry);

            var parent = GetParentDirectory(normalizedPath);
            while (parent.Length > 0)
            {
                if (!_directories.TryGetValue(parent, out var children))
                {
                    children = [];
                    _directories[parent] = children;
                }

                children.Add(GetFileName(normalizedPath));
                normalizedPath = parent;
                parent = GetParentDirectory(normalizedPath);
            }
        }
    }

    public override FileSystemEntry? GetFile(string filePath)
    {
        var normalizedPath = NormalizeZipPath(filePath).Trim(Separator);
        if (!_files.TryGetValue(normalizedPath, out var indexed))
        {
            return null;
        }

        return CreateEntry(indexed);
    }

    public override IEnumerable<FileSystemEntry> GetFilesInDirectory(
        string directoryPath,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        var normalizedDirectory = NormalizeZipPath(directoryPath).Trim(Separator);

        var search = new SearchPattern(searchPattern);

        foreach (var path in EnumerateFilePaths(normalizedDirectory, searchOption))
        {
            if (search.Match(GetFileName(path)))
            {
                yield return CreateEntry(_files[path]);
            }
        }
    }

    private IEnumerable<string> EnumerateFilePaths(string directoryPath, SearchOption searchOption)
    {
        if (!_directories.TryGetValue(directoryPath, out var children))
        {
            yield break;
        }

        foreach (var child in children)
        {
            var childPath = directoryPath.Length == 0
                ? child
                : directoryPath + Separator + child;
            if (_files.ContainsKey(childPath))
            {
                yield return childPath;
            }
            else if (searchOption == SearchOption.AllDirectories)
            {
                foreach (var descendant in EnumerateFilePaths(childPath, searchOption))
                {
                    yield return descendant;
                }
            }
        }
    }

    private FileSystemEntry CreateEntry(IndexedEntry indexed)
    {
        return new FileSystemEntry(
            this,
            indexed.CanonicalPath,
            (uint)indexed.Entry.Length,
            () => indexed.Entry.Open());
    }

    private static string GetParentDirectory(string path)
    {
        var separatorIndex = path.LastIndexOf(Separator);
        return separatorIndex < 0 ? string.Empty : path[..separatorIndex];
    }

    private static string GetFileName(string path)
    {
        var separatorIndex = path.LastIndexOf(Separator);
        return separatorIndex < 0 ? path : path[(separatorIndex + 1)..];
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        _archive.Dispose();
        base.Dispose(disposeManagedResources);
    }
}
