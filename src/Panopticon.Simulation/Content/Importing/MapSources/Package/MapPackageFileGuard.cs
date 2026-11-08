#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Panopticon.Content.Importing.MapSources.Package;

/// <summary>
/// Filesystem guard for one manifest entry path under a package root. Defense in depth
/// beyond the schema-level path validation: the combined path must canonicalize inside
/// the root, and no traversed component may be a symbolic link or reparse point. All
/// rejections carry a stable diagnostic code; no raw IO exception escapes.
/// </summary>
public static class MapPackageFileGuard
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// Resolves one entry path under the package root. Returns the absolute path only
    /// when it is contained in the root and every traversed component is link-free;
    /// missing final entries resolve successfully so staged writes can create them.
    /// </summary>
    public static bool TryResolve(
        string packageRoot,
        string entryPath,
        [NotNullWhen(true)] out string? fullPath,
        [NotNullWhen(false)] out MapPackageDiagnostic? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);

        fullPath = null;
        error = null;

        try
        {
            return TryResolveCore(packageRoot, entryPath, out fullPath, out error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.IoFailure,
                $"Resolving '{entryPath}' under '{packageRoot}' failed: {ex.Message}");
            return false;
        }
    }

    private static bool TryResolveCore(
        string packageRoot,
        string entryPath,
        [NotNullWhen(true)] out string? fullPath,
        [NotNullWhen(false)] out MapPackageDiagnostic? error)
    {
        fullPath = null;
        error = null;

        var rootFullPath = Path.GetFullPath(packageRoot);

        string candidate;

        try
        {
            candidate = Path.GetFullPath(Path.Combine(rootFullPath, entryPath));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            error = new MapPackageDiagnostic(
                MapPackageDiagnostic.PathInvalid,
                $"Entry path '{entryPath}' cannot be combined with the package root: {ex.Message}");
            return false;
        }

        // Containment runs before the schema check so traversal is reported as an escape
        // even where schema validation would also reject: rooted entry paths that replace
        // the root and any '..' escape both fail here.
        var containmentPrefix = rootFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(containmentPrefix, PathComparison))
        {
            error = new MapPackageDiagnostic(
                MapPackageChunkDiagnostic.PathEscape,
                $"Entry path '{entryPath}' resolves to '{candidate}', which escapes the package root '{rootFullPath}'.");
            return false;
        }

        if (!MapPackagePath.TryValidatePath(entryPath, out var pathReason))
        {
            error = new MapPackageDiagnostic(
                MapPackageDiagnostic.PathInvalid,
                $"Entry path '{entryPath}' is invalid: {pathReason}");
            return false;
        }

        var current = rootFullPath;

        foreach (var segment in entryPath.Split('/'))
        {
            current = Path.Combine(current, segment);

            if (IsLinkOrReparsePoint(current))
            {
                error = new MapPackageDiagnostic(
                    MapPackageChunkDiagnostic.SymlinkRejected,
                    $"Package path component '{current}' of entry '{entryPath}' is a symbolic link or reparse point; links are never followed.");
                return false;
            }
        }

        fullPath = current;
        return true;
    }

    private static bool IsLinkOrReparsePoint(string path)
    {
        // An ordinary missing entry cannot be followed; inaccessible entries propagate to the caller.
        if (TryResolveLinkTarget(path) is not null)
        {
            return true;
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    // Returns null only for an ordinary missing entry or a plain non-link; link resolution failures propagate.
    private static string? TryResolveLinkTarget(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: false)?.FullName;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }
}
