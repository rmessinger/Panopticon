using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenSage.Data;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// Discovers all available map files in a game installation.
/// </summary>
public static class MapDiscoveryService
{
    /// <summary>
    /// Pair of (DisplayLabel, MapPath) for a discovered map.
    /// </summary>
    public sealed record MapOption(string Label, string Path);

    /// <summary>
    /// Discovers all .map files in the game installation's "maps" directory.
    /// Returns maps sorted by label.
    /// </summary>
    /// <param name="installation">A valid game installation.</param>
    /// <returns>List of discovered maps, sorted by label.</returns>
    public static IReadOnlyList<MapOption> DiscoverMaps(GameInstallation installation)
    {
        using var fileSystem = installation.CreateFileSystem();

        var mapPaths = fileSystem
            .GetFilesInDirectory("maps", "*.map", SearchOption.AllDirectories)
            .Select(entry => entry.FilePath)
            .OrderBy(path => path)
            .ToList();

        var results = new List<MapOption>();

        foreach (var mapPath in mapPaths)
        {
            var label = ExtractMapLabel(mapPath);
            results.Add(new MapOption(label, mapPath));
        }

        return results.OrderBy(m => m.Label).ToList();
    }

    /// <summary>
    /// Extracts a display label from a map path.
    /// Path format: "maps/folder/name.map" or "maps/name.map"
    /// Returns the deepest folder name, or the filename without extension if at root.
    /// </summary>
    private static string ExtractMapLabel(string mapPath)
    {
        // Remove leading "maps/" if present
        var relativePath = mapPath.StartsWith("maps/", StringComparison.OrdinalIgnoreCase)
            ? mapPath.Substring(5)
            : mapPath;

        // Split by path separators
        var parts = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return "Unknown";
        }

        // If multiple parts, use the last folder name (before the .map file)
        if (parts.Length > 1)
        {
            var folderName = parts[parts.Length - 2];
            return TitleCase(folderName);
        }

        // If only one part, it's the filename — strip extension and title-case it
        var fileName = Path.GetFileNameWithoutExtension(parts[0]);
        return TitleCase(fileName);
    }

    /// <summary>
    /// Converts a snake_case or dash-separated string to Title Case.
    /// E.g. "alpine_assault" → "Alpine Assault", "alpine-assault" → "Alpine Assault"
    /// </summary>
    private static string TitleCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        // Replace underscores and hyphens with spaces
        var normalized = input.Replace('_', ' ').Replace('-', ' ');

        // Split on spaces and title-case each word
        var words = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var titleCased = words
            .Select(word => char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant())
            .ToList();

        return string.Join(" ", titleCased);
    }
}
