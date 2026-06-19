using System;
using System.IO;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// Returns platform-appropriate base directories for application data and caches.
/// On macCatalyst, <c>Environment.SpecialFolder.LocalApplicationData</c> resolves to
/// <c>~/Documents</c>, which on macOS is typically synced by iCloud Drive.
/// Reading iCloud-placeholder files can stall for 10+ seconds while a download completes.
/// This helper uses <c>$HOME/Library/Application Support</c> directly on all Apple
/// platforms to avoid that trap.
/// </summary>
internal static class PlatformPaths
{
    /// <summary>
    /// Base directory for persistent application data (logs, user-facing state).
    /// Equivalent to <c>%LocalAppData%</c> on Windows and
    /// <c>~/Library/Application Support</c> on macOS / macCatalyst.
    /// </summary>
    public static string AppDataBase { get; } = ResolveAppDataBase();

    private static string ResolveAppDataBase()
    {
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
            {
                var appSupport = Path.Combine(home, "Library", "Application Support");
                if (IsWritable(appSupport))
                    return appSupport;
            }
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrEmpty(local) ? Path.GetTempPath() : local;
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".opensage-write-probe");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
