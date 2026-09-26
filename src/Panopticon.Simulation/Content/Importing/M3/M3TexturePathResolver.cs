#nullable enable

using System;
using System.IO;
using Panopticon.IO;

namespace Panopticon.Content.Importing.M3;

internal static class M3TexturePathResolver
{
    private const string CorePackage = "mods/core.sc2mod/base.sc2assets";

    public static ResolvedM3Texture Resolve(string modelPath, string layerPath, FileSystem fileSystem)
    {
        if (string.IsNullOrEmpty(modelPath))
        {
            throw new ArgumentException("Model path must not be null or empty.", nameof(modelPath));
        }

        if (string.IsNullOrEmpty(layerPath))
        {
            throw new ArgumentException("Layer path must not be null or empty.", nameof(layerPath));
        }

        var package = GetPackagePath(modelPath);
        var first = $"{package}/{layerPath}";
        var second = $"{CorePackage}/{layerPath}";
        foreach (var attempt in new[] { first, second })
        {
            var entry = ResolveEntry(attempt, fileSystem);
            if (entry is not null)
            {
                using var stream = entry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return new ResolvedM3Texture(entry.FilePath.Replace('\\', '/'), memory.ToArray());
            }
        }

        throw new FileNotFoundException(
            $"M3 texture layer '{layerPath}' for model '{modelPath}' was not found. Searched '{first}' and '{second}'.");
    }

    private static string GetPackagePath(string modelPath)
    {
        var normalized = modelPath.Replace('\\', '/');
        const string Suffix = ".sc2assets";
        var index = normalized.IndexOf(Suffix, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            throw new InvalidDataException($"M3 model path '{modelPath}' does not include an .sc2assets package.");
        }

        return normalized[..(index + Suffix.Length)];
    }

    private static FileSystemEntry? ResolveEntry(string path, FileSystem fileSystem)
    {
        var entry = fileSystem.GetFile(path);
        if (entry is not null)
        {
            return entry;
        }

        var normalized = path.Replace('\\', '/');
        foreach (var candidate in fileSystem.GetFilesInDirectory(string.Empty, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(candidate.FilePath.Replace('\\', '/'), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }
}

internal sealed record ResolvedM3Texture(string Path, byte[] Bytes);
