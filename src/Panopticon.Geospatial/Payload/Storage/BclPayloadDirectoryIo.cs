using System;
using System.IO;

namespace Panopticon.Geospatial.Payload.Storage;

internal sealed class BclPayloadDirectoryIo : IPayloadDirectoryIo
{
    internal static BclPayloadDirectoryIo Instance { get; } = new();

    private static readonly EnumerationOptions DeleteEnumeration = new()
    {
        AttributesToSkip = FileAttributes.None,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false,
    };

    public void CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    public void MoveDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.Move(sourceDirectory, destinationDirectory);
    }

    public void DeleteDirectory(string path)
    {
        if (IsSymbolicLink(path))
        {
            File.Delete(path);

            return;
        }

        if (!Directory.Exists(path))
        {
            return;
        }

        DeleteContents(path);
        Directory.Delete(path);
    }

    private static void DeleteContents(string directory)
    {
        // Links are removed as links; recursive descent never enters a linked target tree.
        // Inaccessible entries propagate so callers fail closed instead of following blind.
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory, "*", DeleteEnumeration))
        {
            if (IsSymbolicLink(entry) || !Directory.Exists(entry))
            {
                File.Delete(entry);
            }
            else
            {
                DeleteContents(entry);
                Directory.Delete(entry);
            }
        }
    }

    // Ordinary not-found is not a link; any other attribute failure must stop the caller.
    internal static bool IsSymbolicLink(string path)
    {
        FileAttributes attributes;

        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }

        return attributes.HasFlag(FileAttributes.ReparsePoint);
    }
}
