using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using OpenSage.Data;
using OpenSage.Mods.Generals;

namespace OpenSage.Tools.ReplaySketch.Services;

public sealed class GeneralsInstallationService
{
    private GameInstallation? _cachedInstallation;
    private bool _searchDone;

    /// <summary>
    /// Overrides the install path for this session: sets the CNC_GENERALS_PATH
    /// environment variable and clears the cached result so the next
    /// <see cref="TryGetInstallation"/> call re-discovers using the new path.
    /// </summary>
    public void SetPath(string folderPath)
    {
        ReplaySketchLog.Write($"GeneralsInstallationService.SetPath path='{folderPath}'");
        Environment.SetEnvironmentVariable("CNC_GENERALS_PATH", folderPath);
        _cachedInstallation = null;
        _searchDone = false;
    }

    /// <summary>
    /// Tries to locate a Generals installation via the registry, Steam, or the
    /// <c>CNC_GENERALS_PATH</c> environment variable.
    /// Returns <c>false</c> and a null <paramref name="installation"/> if none is found.
    /// </summary>
    public bool TryGetInstallation(out GameInstallation? installation)
    {
        if (!_searchDone)
        {
            ReplaySketchLog.Write("GeneralsInstallationService.TryGetInstallation probing locators");
            var stopwatch = Stopwatch.StartNew();
            _cachedInstallation = InstallationLocators
                .FindAllInstallations(GeneralsDefinition.Instance)
                .FirstOrDefault();
            _searchDone = true;

            if (_cachedInstallation is null)
            {
                ReplaySketchLog.Write(
                    $"GeneralsInstallationService.TryGetInstallation no installation found after {stopwatch.ElapsedMilliseconds} ms; diagnostics: {GetDiagnostics().Replace(Environment.NewLine, " | ")}");
            }
            else
            {
                ReplaySketchLog.Write(
                    $"GeneralsInstallationService.TryGetInstallation found '{_cachedInstallation.Path}' after {stopwatch.ElapsedMilliseconds} ms");
            }
        }
        else
        {
            ReplaySketchLog.Write(
                $"GeneralsInstallationService.TryGetInstallation using cached result '{_cachedInstallation?.Path ?? "(null)"}'");
        }

        installation = _cachedInstallation;
        return installation is not null;
    }

    /// <summary>
    /// Returns a multi-line diagnostic string describing what paths were probed and why
    /// discovery succeeded or failed. Intended for display in the UI when no installation
    /// is found, to help pinpoint the root cause without requiring a debugger.
    /// </summary>
    public string GetDiagnostics()
    {
        var lines = new System.Text.StringBuilder();

        var envPath = Environment.GetEnvironmentVariable("CNC_GENERALS_PATH");
        lines.AppendLine($"CNC_GENERALS_PATH: {(string.IsNullOrEmpty(envPath) ? "(not set)" : envPath)}");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var homeEnv = Environment.GetEnvironmentVariable("HOME");
            lines.AppendLine($"UserProfile: '{userProfile}'");
            lines.AppendLine($"HOME env: '{homeEnv}'");

            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(homeDir))
                homeDir = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
            lines.AppendLine($"GetHomeDirectory: '{homeDir}'");

            string vdfPath;
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                vdfPath = Path.Combine(homeDir, "Library", "Application Support", "Steam", "steamapps", "libraryfolders.vdf");
            }
            else
            {
                vdfPath = Path.Combine(homeDir, ".local", "share", "Steam", "steamapps", "libraryfolders.vdf");
            }

            lines.AppendLine($"VDF path: {vdfPath}");
            lines.AppendLine($"VDF exists: {File.Exists(vdfPath)}");

            if (File.Exists(vdfPath))
            {
                var gameDir = Path.Combine(
                    Path.GetDirectoryName(Path.GetDirectoryName(vdfPath))!,
                    "common",
                    "Command and Conquer Generals");
                lines.AppendLine($"Game dir: {gameDir}");
                lines.AppendLine($"Game dir exists: {Directory.Exists(gameDir)}");
            }
        }

        return lines.ToString().TrimEnd();
    }
}
