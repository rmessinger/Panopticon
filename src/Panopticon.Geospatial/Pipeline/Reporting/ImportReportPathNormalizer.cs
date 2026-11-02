using System;
using System.Collections.Generic;
using System.Text;
using Panopticon.Geospatial.Payload.Models;
using Panopticon.Geospatial.Pipeline.GenerationPlanning;
using Panopticon.Geospatial.Pipeline.SourceLoading;

namespace Panopticon.Geospatial.Pipeline.Reporting;

/// <summary>
/// Lexical path and message normalization for report assembly. Runtime absolute input
/// paths map onto the canonical manifest-relative forms the provenance carries, the
/// manifest's own full path maps to its basename, unknown absolute paths inside the
/// manifest's project root relativize lexically, and owned output and staging
/// directory prefixes collapse to the stable '<payload>' placeholder. Windows drive,
/// UNC, and scheme 'scheme://' URI forms are recognized on every host, and Windows-style
/// paths compare case-insensitively while POSIX-style paths compare ordinally. Every
/// operation is a string comparison: no filesystem probing, no link resolution, no
/// clocks. Trusted callback text is rewritten only at these known paths; no universal
/// redaction is claimed. A path with no honest relative spelling yields null; none is
/// ever fabricated with '..' or by rewriting separators.
/// </summary>
internal sealed class ImportReportPathNormalizer
{
    internal const string Placeholder = "<payload>";

    private readonly (string Key, string Replacement)[] _maps;

    private readonly string[] _ownedKeys;

    private readonly string? _projectRoot;

    private ImportReportPathNormalizer((string Key, string Replacement)[] maps, string[] ownedKeys, string? projectRoot)
    {
        _maps = maps;
        _ownedKeys = ownedKeys;
        _projectRoot = projectRoot;
    }

    public static ImportReportPathNormalizer Create(
        IReadOnlyList<LoadedSource> sources,
        IReadOnlyList<string> protectedInputPaths,
        IReadOnlyList<string>? ownedDirectories)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(protectedInputPaths);

        var maps = new List<(string Key, string Replacement)>();
        string? manifestKey = null;

        if (protectedInputPaths.Count > 0)
        {
            manifestKey = NormalizeSeparators(protectedInputPaths[0]);
        }

        foreach (var source in sources)
        {
            if (source is null)
            {
                throw new ArgumentException("Sequence items must not be null.", nameof(sources));
            }

            AddMap(maps, source.FullPath, ImportGenerationPlanPaths.CanonicalizeProvenancePath(source.RelativePath));
        }

        // The manifest is the first protected path; when it is also a declared source
        // the source mapping already covers it.
        if (manifestKey is not null && !ContainsKey(maps, manifestKey))
        {
            AddMap(maps, manifestKey, Basename(manifestKey));
        }

        var ownedKeys = new List<string>();

        if (ownedDirectories is not null)
        {
            foreach (var directory in ownedDirectories)
            {
                ownedKeys.Add(OwnedKey(directory));
            }
        }

        foreach (var key in ownedKeys)
        {
            if (!ContainsKey(maps, key))
            {
                maps.Add((key, Placeholder));
            }
        }

        SortLongestFirst(maps);
        SortStringsLongestFirst(ownedKeys);

        return new ImportReportPathNormalizer([.. maps], [.. ownedKeys], ProjectRootOf(manifestKey));
    }

    /// <summary>
    /// One runtime source path as a serialized manifest-relative path: a known input's
    /// canonical provenance path, the manifest basename, or a lexically relativized
    /// path inside the project root. Owned output and staging paths, root or
    /// descendant, have no manifest-relative spelling and yield null; the
    /// '<payload>' placeholder appears in message text only. Aliases with '.' or empty
    /// segments canonicalize; unrepresentable forms, including every unknown absolute
    /// path outside the project and owned directories, yield null.
    /// </summary>
    public string? NormalizeSourcePath(string? sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath))
        {
            return null;
        }

        var kind = Classify(sourcePath);

        if (kind == PathKind.Relative)
        {
            return CanonicalRelative(sourcePath);
        }

        if (kind == PathKind.Uri)
        {
            return null;
        }

        var normalized = NormalizeSeparators(sourcePath);
        var comparison = ComparisonFor(kind);

        // No fabricated 'manifest-relative' path to a generated file: the owned root
        // and every path under it stay unrepresented in the sourcePath field.
        foreach (var owned in _ownedKeys)
        {
            if (string.Equals(normalized, owned, comparison) || TryStripPrefix(normalized, owned, comparison, out _))
            {
                return null;
            }
        }

        foreach (var (key, replacement) in _maps)
        {
            if (string.Equals(normalized, key, comparison))
            {
                return replacement;
            }
        }

        if (_projectRoot is { } root && TryStripPrefix(normalized, root, comparison, out var relative))
        {
            return CanonicalRelative(relative);
        }

        return null;
    }

    /// <summary>
    /// Rewrites known input paths, the manifest path, and owned directory prefixes in
    /// trusted message text. Everything else, including unrelated absolute paths and
    /// numbers, passes through unchanged; replacements only occur at path-token
    /// boundaries so a longer path ending in a known path never matches.
    /// </summary>
    public string NormalizeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var result = text;

        foreach (var (key, replacement) in _maps)
        {
            result = ReplaceAtBoundaries(result, key, replacement);
        }

        return result;
    }

    private static string? CanonicalRelative(string relativePath)
    {
        var canonical = ImportGenerationPlanPaths.CanonicalizeProvenancePath(relativePath);

        return IsRepresentable(canonical) ? canonical : null;
    }

    private static string OwnedKey(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        if (Classify(directory) is PathKind.Relative or PathKind.Uri)
        {
            throw new ArgumentException($"The owned directory '{directory}' must be an absolute path.", nameof(directory));
        }

        var key = TrimTrailingSeparators(NormalizeSeparators(directory));

        if (key.Length == 0)
        {
            throw new ArgumentException($"The owned directory '{directory}' must not be a filesystem root.", nameof(directory));
        }

        return key;
    }

    private static void AddMap(List<(string Key, string Replacement)> maps, string? fullPath, string replacement)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            return;
        }

        maps.Add((NormalizeSeparators(fullPath), replacement));
    }

    private static bool ContainsKey(List<(string Key, string Replacement)> maps, string key)
    {
        foreach (var (existing, _) in maps)
        {
            if (string.Equals(existing, key, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Longest keys first so a nested owned directory replaces before its parent.</summary>
    private static void SortLongestFirst(List<(string Key, string Replacement)> maps)
    {
        maps.Sort(
            static (left, right) =>
            {
                var byLength = right.Key.Length.CompareTo(left.Key.Length);

                return byLength != 0 ? byLength : string.CompareOrdinal(left.Key, right.Key);
            });
    }

    private static void SortStringsLongestFirst(List<string> keys)
    {
        keys.Sort(
            static (left, right) =>
            {
                var byLength = right.Length.CompareTo(left.Length);

                return byLength != 0 ? byLength : string.CompareOrdinal(left, right);
            });
    }

    /// <summary>
    /// Lexical parent of the manifest path; null when the manifest sits at a filesystem
    /// root, where every absolute path would otherwise count as inside the project.
    /// </summary>
    private static string? ProjectRootOf(string? manifestKey)
    {
        if (manifestKey is null)
        {
            return null;
        }

        var lastSeparator = manifestKey.LastIndexOf('/');

        return lastSeparator > 0 ? manifestKey[..lastSeparator] : null;
    }

    private static bool TryStripPrefix(string path, string key, StringComparison comparison, out string remainder)
    {
        if (path.Length > key.Length && path.StartsWith(key, comparison) && path[key.Length] == '/')
        {
            remainder = path[(key.Length + 1)..];

            return true;
        }

        remainder = string.Empty;

        return false;
    }

    private static bool IsRepresentable(string canonical)
    {
        if (canonical.Length == 0)
        {
            return false;
        }

        try
        {
            PayloadValidationRules.NormalizedRelativePath(canonical, nameof(canonical));

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string ReplaceAtBoundaries(string text, string key, string replacement)
    {
        var index = text.IndexOf(key, StringComparison.Ordinal);

        if (index < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var consumed = 0;

        while (index >= 0)
        {
            var end = index + key.Length;

            builder.Append(text, consumed, index - consumed);

            if (IsBoundary(text, index, end))
            {
                builder.Append(replacement);
            }
            else
            {
                builder.Append(key);
            }

            consumed = end;
            index = text.IndexOf(key, end, StringComparison.Ordinal);
        }

        builder.Append(text, consumed, text.Length - consumed);

        return builder.ToString();
    }

    private static bool IsBoundary(string text, int start, int end)
    {
        // The neighbour chars decide whether the occurrence is a whole path token:
        // separators continue the path into a mapped directory, and any non-path
        // punctuation or space ends it. A continuing identifier character means a
        // different, longer path must be left alone.
        return (start == 0 || IsBoundaryChar(text[start - 1]))
            && (end == text.Length || IsBoundaryChar(text[end]));
    }

    private static bool IsBoundaryChar(char character)
    {
        return char.IsWhiteSpace(character)
            || character is '/' or '\\' or '"' or '\'' or '<' or '>' or '(' or ')' or '[' or ']' or '{' or '}' or ',' or ';' or ':' or '.' or '!' or '?';
    }

    private static PathKind Classify(string path)
    {
        if (IsUri(path))
        {
            return PathKind.Uri;
        }

        if (path.Length >= 2 && IsAsciiLetter(path[0]) && path[1] == ':')
        {
            return PathKind.WindowsAbsolute;
        }

        if (path.StartsWith("//", StringComparison.Ordinal) || path.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return PathKind.Unc;
        }

        return path[0] == '/' ? PathKind.PosixAbsolute : PathKind.Relative;
    }

    private static bool IsUri(string path)
    {
        var end = 0;

        while (end < path.Length && IsSchemeCharacter(path[end]))
        {
            end++;
        }

        return end > 0
            && end + 2 < path.Length
            && path[end] == ':'
            && path[end + 1] == '/'
            && path[end + 2] == '/';
    }

    private static bool IsSchemeCharacter(char character)
    {
        return character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '+' or '-' or '.';
    }

    private static bool IsAsciiLetter(char character)
    {
        return character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');
    }

    private static StringComparison ComparisonFor(PathKind kind)
    {
        return kind is PathKind.WindowsAbsolute or PathKind.Unc
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    private static string NormalizeSeparators(string path) => path.Replace('\\', '/');

    private static string TrimTrailingSeparators(string path)
    {
        var end = path.Length;

        while (end > 0 && path[end - 1] == '/')
        {
            end--;
        }

        return path[..end];
    }

    private static string Basename(string path)
    {
        var lastSeparator = path.LastIndexOf('/');

        return lastSeparator >= 0 ? path[(lastSeparator + 1)..] : path;
    }

    private enum PathKind
    {
        Relative,

        PosixAbsolute,

        WindowsAbsolute,

        Unc,

        Uri,
    }
}
