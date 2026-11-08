#nullable enable

namespace Panopticon.Content.Importing.Units.Packages;

/// <summary>Path and digest validation helpers for unit package entries.</summary>
public static class UnitPackagePath
{
    public static bool TryValidatePath(string? path, out string? reason)
    {
        if (string.IsNullOrEmpty(path))
        {
            reason = "Path is null or empty.";
            return false;
        }

        if (path.Length > UnitPackageFormat.MaxPathLength)
        {
            reason = $"Path exceeds maximum length of {UnitPackageFormat.MaxPathLength} characters (length was {path.Length}).";
            return false;
        }

        if (path.Contains('\\'))
        {
            reason = $"Path '{path}' contains backslashes; only POSIX-style relative paths are allowed.";
            return false;
        }

        if (path.StartsWith('/'))
        {
            reason = $"Path '{path}' has a leading slash; only relative paths are allowed.";
            return false;
        }

        if (path.StartsWith("//"))
        {
            reason = $"Path '{path}' has a UNC prefix; UNC paths are not allowed.";
            return false;
        }

        if (path.Contains(':'))
        {
            reason = $"Path '{path}' contains a colon, drive letter, or URI scheme; only relative file paths are allowed.";
            return false;
        }

        if (path.Contains("//"))
        {
            reason = $"Path '{path}' contains an empty segment ('//').";
            return false;
        }

        var segments = path.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];

            if (string.IsNullOrEmpty(segment))
            {
                reason = $"Path '{path}' contains an empty segment.";
                return false;
            }

            if (segment is "." or "..")
            {
                reason = $"Path '{path}' contains relative traversal segment '{segment}'.";
                return false;
            }
        }

        reason = null;
        return true;
    }

    public static bool IsValidSha256(string? sha256)
    {
        if (sha256 is null || sha256.Length != UnitPackageFormat.Sha256HexLength)
        {
            return false;
        }

        for (var i = 0; i < sha256.Length; i++)
        {
            var c = sha256[i];

            if ((c < '0' || c > '9') && (c < 'a' || c > 'f'))
            {
                return false;
            }
        }

        return true;
    }
}
