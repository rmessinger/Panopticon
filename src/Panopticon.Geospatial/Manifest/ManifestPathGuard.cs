using System;
using System.IO;

namespace Panopticon.Geospatial.Manifest;

internal static class ManifestPathGuard
{
    public static string NormalizeRelativePath(string relativePath) => relativePath.Replace('\\', '/');

    public static string? Validate(string manifestDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.IndexOf('\0') >= 0)
        {
            return $"Source path '{relativePath}' is empty or contains invalid characters.";
        }

        if (relativePath.Contains("://", StringComparison.Ordinal))
        {
            return $"Source path '{relativePath}' is a URL; only paths relative to the manifest directory are allowed.";
        }

        if (relativePath.StartsWith(@"\\", StringComparison.Ordinal)
            || (relativePath.Length >= 2 && relativePath[1] == ':')
            || Path.IsPathRooted(relativePath)
            || Path.IsPathRooted(NormalizeRelativePath(relativePath)))
        {
            return $"Source path '{relativePath}' is absolute; only paths relative to the manifest directory are allowed.";
        }

        var root = Path.GetFullPath(manifestDirectory);
        root = Path.TrimEndingDirectorySeparator(root);
        if (root.Length == 0)
        {
            root = Path.DirectorySeparatorChar.ToString();
        }

        // Initial policy rejects every '..' segment outright: lexical containment cannot
        // guarantee confinement once intermediate symlinks are resolved.
        var current = root;
        var sawSegment = false;
        foreach (var segment in NormalizeRelativePath(relativePath).Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                return $"Source path '{relativePath}' contains a parent ('..') segment, which is not allowed.";
            }

            sawSegment = true;
            current = Path.Combine(current, segment);
            try
            {
                current = ResolveLinks(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Fail closed: an unresolvable link chain cannot be proven confined.
                return $"Source path '{relativePath}' was rejected because symlink resolution failed: {ex.GetType().Name}.";
            }

            if (!IsUnderRoot(root, current))
            {
                return $"Source path '{relativePath}' escapes the manifest directory.";
            }
        }

        if (!sawSegment)
        {
            return $"Source path '{relativePath}' does not name a file below the manifest directory.";
        }

        return null;
    }

    private static string ResolveLinks(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            // Missing entries cannot be probed for links; readers report them later.
            return path;
        }

        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        var target = info.ResolveLinkTarget(returnFinalTarget: true);
        if (target is null)
        {
            return path;
        }

        // Link targets may be relative to the link's own directory; absolute targets pass
        // through Path.Combine unchanged.
        var linkDirectory = Path.GetDirectoryName(path);
        return linkDirectory is null ? path : Path.GetFullPath(Path.Combine(linkDirectory, target.FullName));
    }

    private static bool IsUnderRoot(string root, string path) =>
        path.Length > root.Length
        && path.StartsWith(root, StringComparison.Ordinal)
        && (root.Length == 1 || path[root.Length] == Path.DirectorySeparatorChar);
}
