using System;

namespace OpenSage.Tools.ReplaySketch.Services;

/// <summary>
/// Locates the <c>OpenSage.Launcher</c> executable by searching sibling build-output
/// directories relative to ReplaySketch's own output directory.
/// </summary>
public sealed class LauncherLocatorService
{
    private const string LauncherProjectName = "OpenSage.Launcher";

    private readonly SiblingBuildExecutableLocator _locator = new();

    /// <summary>
    /// Attempts to find the launcher executable.
    /// Returns the full path on success, or <see langword="null"/> if not found.
    /// </summary>
    public string? Locate()
    {
        return _locator.Locate(LauncherProjectName);
    }
}
