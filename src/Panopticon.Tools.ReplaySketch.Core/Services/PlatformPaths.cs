using System;
using System.IO;

namespace Panopticon.Tools.ReplaySketch.Services;

/// <summary>
/// Returns platform-appropriate base directories for application data and caches.
/// On macCatalyst, <c>Environment.SpecialFolder.LocalApplicationData</c> resolves to
/// <c>~/Documents</c>, which on macOS is typically synced by iCloud Drive.
/// Reading iCloud-placeholder files can stall for 10+ seconds while a download completes.
/// This helper uses <c>$HOME/Library/Application Support</c> and
/// <c>$HOME/Library/Caches</c> directly on all Apple platforms to avoid that trap.
/// Regenerable cache data (e.g. preview thumbnails) must use <see cref="CacheBase"/>,
/// not <see cref="AppDataBase"/>; iCloud can evict files in Application Support but
/// never touches ~/Library/Caches.
/// </summary>
public static class PlatformPaths
{
    /// <summary>
    /// Base directory for persistent application data (logs, user-facing state).
    /// Equivalent to <c>%LocalAppData%</c> on Windows and
    /// <c>~/Library/Application Support</c> on macOS / macCatalyst.
    /// </summary>
    public static string AppDataBase { get; } = ResolveAppDataBase();

    /// <summary>
    /// Base directory for regenerable cache data (thumbnails, derived assets).
    /// Uses <c>~/Library/Caches</c> on macOS / macCatalyst, which is excluded from
    /// iCloud Drive and Time Machine and is never evicted to a cloud placeholder.
    /// Falls back to <see cref="AppDataBase"/> on other platforms.
    /// </summary>
    public static string CacheBase { get; } = ResolveCacheBase();

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

    private static string ResolveCacheBase()
    {
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
            {
                var caches = Path.Combine(home, "Library", "Caches");
                if (IsWritable(caches))
                    return caches;
            }
        }

        // Windows / Linux: use the standard cache folder (same as AppDataBase on those platforms).
        return AppDataBase;
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".panopticon-write-probe");
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
