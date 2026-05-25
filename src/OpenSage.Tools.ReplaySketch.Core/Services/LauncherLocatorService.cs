using System;
using System.IO;
using System.Runtime.InteropServices;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// Locates the <c>OpenSage.Launcher</c> executable by searching sibling build-output
/// directories relative to ReplaySketch's own output directory.
/// </summary>
public sealed class LauncherLocatorService
{
    private const string LauncherProjectName = "OpenSage.Launcher";

    /// <summary>
    /// Attempts to find the launcher executable.
    /// Returns the full path on success, or <see langword="null"/> if not found.
    /// </summary>
    public string? Locate()
    {
        try
        {
            // Walk up from our output dir to find the src/ ancestor
            var srcDir = FindSrcDirectory(AppContext.BaseDirectory);
            if (srcDir == null)
            {
                return null;
            }

            var launcherBinDir = Path.Combine(srcDir, LauncherProjectName, "bin");
            if (!Directory.Exists(launcherBinDir))
            {
                return null;
            }

            var preferredConfig = DetectConfiguration();

            // Try preferred config first, then fall back to any config found
            return TryFindInBinDir(launcherBinDir, preferredConfig)
                ?? TryFindInBinDir(launcherBinDir, config: null);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static string? FindSrcDirectory(string startDir)
    {
        var current = startDir;
        while (current != null)
        {
            try
            {
                // Check whether the current dir is named "src" or contains the launcher project
                var launcherProject = Path.Combine(current, LauncherProjectName);
                if (Directory.Exists(launcherProject))
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

    /// <summary>
    /// Detects whether the running process was built in Debug or Release configuration
    /// by inspecting its own base directory path.
    /// </summary>
    private static string? DetectConfiguration()
    {
        var dir = AppContext.BaseDirectory;
        if (dir.Contains(Path.DirectorySeparatorChar + "Debug" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Debug";
        }

        if (dir.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Release";
        }

        return null;
    }

    /// <summary>
    /// Searches <c>bin/&lt;config&gt;/&lt;tfm&gt;/</c> subdirectories for the launcher executable.
    /// When <paramref name="config"/> is <see langword="null"/>, all config subdirectories are searched.
    /// </summary>
    private static string? TryFindInBinDir(string binDir, string? config)
    {
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"{LauncherProjectName}.exe"
            : LauncherProjectName;

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
