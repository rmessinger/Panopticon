#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>
/// Filesystem guard for manifest entry paths under a package root. Validates that paths
/// remain contained inside the package root and that traversed components are free of symlinks.
/// </summary>
public static class UnitPackageFileGuard
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static bool TryResolve(
        string packageRoot,
        string entryPath,
        [NotNullWhen(true)] out string? fullPath,
        [NotNullWhen(false)] out string? errorCode,
        [NotNullWhen(false)] out string? errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);

        fullPath = null;
        errorCode = null;
        errorMessage = null;

        try
        {
            return TryResolveCore(packageRoot, entryPath, out fullPath, out errorCode, out errorMessage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            errorCode = UnitPackageDiagnosticCodes.IoFailure;
            errorMessage = $"Resolving '{entryPath}' under '{packageRoot}' failed: {ex.Message}";
            return false;
        }
    }

    private static bool TryResolveCore(
        string packageRoot,
        string entryPath,
        [NotNullWhen(true)] out string? fullPath,
        [NotNullWhen(false)] out string? errorCode,
        [NotNullWhen(false)] out string? errorMessage)
    {
        fullPath = null;
        errorCode = null;
        errorMessage = null;

        var rootFullPath = Path.GetFullPath(packageRoot);

        string candidate;

        try
        {
            candidate = Path.GetFullPath(Path.Combine(rootFullPath, entryPath));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            errorCode = UnitPackageDiagnosticCodes.PathInvalid;
            errorMessage = $"Entry path '{entryPath}' cannot be combined with package root: {ex.Message}";
            return false;
        }

        var containmentPrefix = rootFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(containmentPrefix, PathComparison))
        {
            errorCode = UnitPackageDiagnosticCodes.PathEscape;
            errorMessage = $"Entry path '{entryPath}' resolves to '{candidate}', which escapes package root '{rootFullPath}'.";
            return false;
        }

        if (!UnitPackagePath.TryValidatePath(entryPath, out var pathReason))
        {
            errorCode = UnitPackageDiagnosticCodes.PathInvalid;
            errorMessage = $"Entry path '{entryPath}' is invalid: {pathReason}";
            return false;
        }

        var current = rootFullPath;

        foreach (var segment in entryPath.Split('/'))
        {
            current = Path.Combine(current, segment);

            if (IsLinkOrReparsePoint(current))
            {
                errorCode = UnitPackageDiagnosticCodes.SymlinkRejected;
                errorMessage = $"Package path component '{current}' of entry '{entryPath}' is a symbolic link or reparse point; links are never followed.";
                return false;
            }
        }

        fullPath = current;
        return true;
    }

    private static bool IsLinkOrReparsePoint(string path)
    {
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
