using Panopticon.FileFormats.Casc;

namespace Panopticon.IO;

public sealed class CascFileSystem : FileSystem
{
    private const char Separator = '/';

    private readonly CascStorage _storage;
    private readonly Dictionary<string, MndxRootEntry> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _directories = new(StringComparer.OrdinalIgnoreCase);

    public CascFileSystem(string installRoot)
    {
        _storage = CascStorage.Open(installRoot);
        IndexEntries();
    }

    public CascFileSystem(CascStorage storage)
    {
        _storage = storage;
        IndexEntries();
    }

    public override FileSystemEntry? GetFile(string filePath)
    {
        var normalizedPath = NormalizeCascPath(filePath).Trim(Separator);
        if (!_files.TryGetValue(normalizedPath, out var entry))
        {
            return null;
        }

        return CreateEntry(entry);
    }

    public override IEnumerable<FileSystemEntry> GetFilesInDirectory(
        string directoryPath,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        var normalizedDirectory = NormalizeCascPath(directoryPath).Trim(Separator);
        var search = new SearchPattern(searchPattern);

        foreach (var path in EnumerateFilePaths(normalizedDirectory, searchOption))
        {
            if (search.Match(GetFileName(path)))
            {
                yield return CreateEntry(_files[path]);
            }
        }
    }

    private void IndexEntries()
    {
        foreach (var entry in _storage.Entries)
        {
            var normalizedPath = NormalizeCascPath(entry.Path).Trim(Separator);
            if (_files.ContainsKey(normalizedPath))
            {
                throw new InvalidDataException($"CASC root contains duplicate entries for '{normalizedPath}'.");
            }

            var indexed = entry with { Path = normalizedPath };
            _files.Add(normalizedPath, indexed);

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

    private FileSystemEntry CreateEntry(MndxRootEntry entry)
    {
        return new FileSystemEntry(
            this,
            entry.Path,
            checked((uint)entry.Size),
            () => new MemoryStream(_storage.OpenFile(entry.ContentKey), writable: false));
    }

    private static string NormalizeCascPath(string path)
    {
        return path.Replace('\\', Separator);
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
        _storage.Dispose();
        base.Dispose(disposeManagedResources);
    }
}
