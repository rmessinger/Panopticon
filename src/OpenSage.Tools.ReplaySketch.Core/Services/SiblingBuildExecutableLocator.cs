using System;
using System.IO;
using System.Runtime.InteropServices;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// Locates a project executable by searching sibling build-output directories
/// (<c>bin/&lt;configuration&gt;/&lt;tfm&gt;/</c>) relative to a starting directory, preferring the
/// configuration of the running process.
/// </summary>
public sealed class SiblingBuildExecutableLocator
{
    /// <summary>
    /// Attempts to find the executable for one project under the shared <c>src</c> tree.
    /// Returns the full path on success, or null if not found.
    /// </summary>
    public string? Locate(string projectName, string? baseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            throw new ArgumentException("Project name must not be empty.", nameof(projectName));
        }

        try
        {
            var srcDir = FindSrcDirectory(baseDirectory ?? AppContext.BaseDirectory, projectName);
            if (srcDir == null)
            {
                return null;
            }

            var binDir = Path.Combine(srcDir, projectName, "bin");
            if (!Directory.Exists(binDir))
            {
                return null;
            }

            var preferredConfig = DetectConfiguration(baseDirectory ?? AppContext.BaseDirectory);

            return TryFindInBinDir(binDir, projectName, preferredConfig)
                ?? TryFindInBinDir(binDir, projectName, config: null);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static string? FindSrcDirectory(string startDir, string projectName)
    {
        var current = startDir;
        while (current != null)
        {
            try
            {
                var projectDir = Path.Combine(current, projectName);
                if (Directory.Exists(projectDir))
                {
                    return current;
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                return null;
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }

    private static string? DetectConfiguration(string baseDirectory)
    {
        if (baseDirectory.Contains(Path.DirectorySeparatorChar + "Debug" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Debug";
        }

        if (baseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Release";
        }

        return null;
    }

    private static string? TryFindInBinDir(string binDir, string projectName, string? config)
    {
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"{projectName}.exe"
            : projectName;

        string[] configDirs;
        try
        {
            configDirs = config != null
                ? [Path.Combine(binDir, config)]
                : Directory.GetDirectories(binDir);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return null;
        }

        foreach (var configDir in configDirs)
        {
            string[] tfmDirs;
            try
            {
                if (!Directory.Exists(configDir))
                {
                    continue;
                }

                tfmDirs = Directory.GetDirectories(configDir);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var tfmDir in tfmDirs)
            {
                var candidate = Path.Combine(tfmDir, exeName);
                try
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (Exception e) when (e is UnauthorizedAccessException or IOException)
                {
                    continue;
                }
            }
        }

        return null;
    }
}
