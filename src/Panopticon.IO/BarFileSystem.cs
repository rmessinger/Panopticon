using Panopticon.FileFormats.Bar;

namespace Panopticon.IO;

/// <summary>
/// A file system over every .bar archive under a directory tree (the AoE3 DE
/// Game directory). Entry paths are the archive root path plus the entry
/// relative path, for example 'Art\units\...'.
/// </summary>
public sealed class BarFileSystem : FileSystem
{
    private readonly BarDirectory _rootDirectory = new BarDirectory();

    public BarFileSystem(string rootDirectory)
    {
        foreach (var archivePath in Directory.EnumerateFiles(rootDirectory, "*.bar", SearchOption.AllDirectories))
        {
            AddBarArchive(archivePath);
        }
    }

    private void AddBarArchive(string archivePath)
    {
        var stream = AddDisposable(new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read));
        var archive = BarArchive.Read(stream);

        foreach (var entry in archive.Entries)
        {
            var directoryParts = entry.FullName.Split('\\', '/');

            var directory = _rootDirectory;
            for (var i = 0; i < directoryParts.Length - 1; i++)
            {
                directory = directory.GetOrCreateDirectory(directoryParts[i]);
            }

            var fileName = directoryParts[directoryParts.Length - 1];

            directory.Files[fileName] = CreateFileSystemEntry(archivePath, entry);
        }
    }

    public override IEnumerable<FileSystemEntry> GetFilesInDirectory(
        string directoryPath,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        var search = new SearchPattern(searchPattern);

        var directory = _rootDirectory;

        if (directoryPath != "")
        {
            var directoryParts = NormalizeFilePath(directoryPath).Split(Path.DirectorySeparatorChar);
            for (var i = 0; i < directoryParts.Length; i++)
            {
                if (!directory.Directories.TryGetValue(directoryParts[i], out directory))
                {
                    return Enumerable.Empty<FileSystemEntry>();
                }
            }
        }

        return GetFilesInDirectory(directory, search, searchOption);
    }

    private IEnumerable<FileSystemEntry> GetFilesInDirectory(
        BarDirectory directory,
        SearchPattern searchPattern,
        SearchOption searchOption)
    {
        foreach (var (fileName, file) in directory.Files)
        {
            if (!searchPattern.Match(fileName))
            {
                continue;
            }

            yield return file;
        }

        if (searchOption == SearchOption.AllDirectories)
        {
            foreach (var subdirectory in directory.Directories.Values)
            {
                foreach (var file in GetFilesInDirectory(subdirectory, searchPattern, searchOption))
                {
                    yield return file;
                }
            }
        }
    }

    public override FileSystemEntry? GetFile(string filePath)
    {
        var directoryParts = NormalizeFilePath(filePath).Split(Path.DirectorySeparatorChar);

        var directory = _rootDirectory;
        for (var i = 0; i < directoryParts.Length - 1; i++)
        {
            if (!directory.Directories.TryGetValue(directoryParts[i], out directory))
            {
                return null;
            }
        }

        var fileName = directoryParts[directoryParts.Length - 1];

        return directory.Files.TryGetValue(fileName, out var file) ? file : null;
    }

    private FileSystemEntry CreateFileSystemEntry(string archivePath, BarEntry entry)
    {
        return new FileSystemEntry(
            this,
            NormalizeFilePath(entry.FullName),
            entry.DecompressedSize,
            () => OpenEntry(archivePath, entry));
    }

    private static Stream OpenEntry(string archivePath, BarEntry entry)
    {
        try
        {
            return entry.Open();
        }
        catch (NotSupportedException exception)
        {
            throw new NotSupportedException(
                $"BAR archive '{archivePath}': {exception.Message}",
                exception);
        }
    }

    private sealed class BarDirectory
    {
        public readonly Dictionary<string, BarDirectory> Directories = new(StringComparer.InvariantCultureIgnoreCase);
        public readonly Dictionary<string, FileSystemEntry> Files = new(StringComparer.InvariantCultureIgnoreCase);

        public BarDirectory GetOrCreateDirectory(string directoryName)
        {
            if (!Directories.TryGetValue(directoryName, out var directory))
            {
                directory = new BarDirectory();
                Directories.Add(directoryName, directory);
            }

            return directory;
        }
    }
}
