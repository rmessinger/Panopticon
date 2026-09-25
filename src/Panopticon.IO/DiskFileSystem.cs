using System.Linq;

namespace Panopticon.IO;

public sealed class DiskFileSystem : FileSystem
{
    public readonly string RootDirectory;

    public DiskFileSystem(string rootDirectory)
    {
        RootDirectory = rootDirectory;
    }

    public override IEnumerable<FileSystemEntry> GetFilesInDirectory(
        string directoryPath,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        var childDirectory = Path.Combine(RootDirectory, NormalizeFilePath(directoryPath));

        var childDirectoryInfo = new DirectoryInfo(childDirectory);
        if (!childDirectoryInfo.Exists)
        {
            yield break;
        }

        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = searchOption == SearchOption.AllDirectories,
            MatchCasing = MatchCasing.CaseInsensitive,
        };

        foreach (var fileInfo in childDirectoryInfo.EnumerateFiles(searchPattern, enumerationOptions))
        {
            yield return CreateFileSystemEntry(fileInfo);
        }
    }

    public override FileSystemEntry? GetFile(string filePath)
    {
        var normalizedPath = NormalizeFilePath(filePath);

        var fullFilePath = Path.Combine(RootDirectory, normalizedPath);
        var fileInfo = new FileInfo(fullFilePath);
        if (fileInfo.Exists)
        {
            return CreateFileSystemEntry(fileInfo);
        }

        return GetFileCaseInsensitive(normalizedPath);
    }

    // The game's asset queries use arbitrary casing (e.g. "art/w3d/ModelName.w3d"), which only
    // matches on case-insensitive filesystems. Fall back to a segment-wise case-insensitive walk.
    private FileSystemEntry? GetFileCaseInsensitive(string normalizedPath)
    {
        var segments = normalizedPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var currentDirectory = new DirectoryInfo(RootDirectory);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            currentDirectory = currentDirectory.EnumerateDirectories()
                .FirstOrDefault(d => string.Equals(d.Name, segments[i], StringComparison.OrdinalIgnoreCase));

            if (currentDirectory == null)
            {
                return null;
            }
        }

        var fileName = segments[^1];
        var fileMatch = currentDirectory.EnumerateFiles()
            .FirstOrDefault(f => string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase));

        return fileMatch == null ? null : CreateFileSystemEntry(fileMatch);
    }

    private FileSystemEntry CreateFileSystemEntry(FileInfo fileInfo)
    {
        var fullPath = fileInfo.FullName;

        var relativePath = Path.GetRelativePath(
            RootDirectory,
            fullPath);

        return new FileSystemEntry(
            this,
            relativePath,
            (uint)fileInfo.Length,
            () => File.OpenRead(fullPath));
    }

    public string GetFullPath(FileSystemEntry entry)
    {
        return Path.Combine(RootDirectory, entry.FilePath);
    }
}
