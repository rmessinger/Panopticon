using System;
using System.Runtime.InteropServices;

namespace Panopticon.Utilities;

public static class PlatformUtility
{
    /// <summary>
    /// Check if current platform is windows
    /// </summary>
    /// <returns></returns>
    public static bool IsWindowsPlatform()
    {
        switch (Environment.OSVersion.Platform)
        {
            case PlatformID.Win32Windows:
            case PlatformID.Win32NT:
            case PlatformID.WinCE:
            case PlatformID.Win32S:
                return true;
            default:
                return false;
        }
    }

    public static float GetDefaultDpi()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return 96.0f;
        }
        else
        {
            // SDL_GetDisplayDPI on all non-Windows platforms (including macOS) returns
            // the logical DPI. Since the game was designed for Windows at 96 DPI, using
            // 96 as the baseline ensures window and cursor sizes match the original
            // physical appearance across all supported operating systems.
            return 96.0f;
        }
    }
}
