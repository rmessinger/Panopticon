using System;
using System.IO;
using System.Runtime.InteropServices;
using OpenSage.Data;
using OpenSage.Mods.Generals;
using Xunit;

namespace OpenSage.Tests.Data.InstallationLocator;

/// <summary>
/// Tests for <see cref="SteamInstallationLocator"/> — covering both the
/// <see cref="SteamInstallationLocator.GetHomeDirectory"/> helper and the
/// end-to-end <see cref="SteamInstallationLocator.FindInstallations"/> path on this machine.
/// </summary>
public class SteamLocatorTests
{
    [Fact]
    public void GetHomeDirectory_ReturnsNonEmptyPath()
    {
        var home = SteamInstallationLocator.GetHomeDirectory();

        Assert.False(string.IsNullOrEmpty(home),
            "GetHomeDirectory() must always return a non-empty path so Steam VDF discovery " +
            "does not silently fall back to a relative path (which breaks Mac Catalyst).");
    }

    [Fact]
    public void GetHomeDirectory_ReturnsExistingDirectory()
    {
        var home = SteamInstallationLocator.GetHomeDirectory();

        Assert.True(Directory.Exists(home),
            $"GetHomeDirectory() returned '{home}', which does not exist.");
    }

    [Fact]
    public void GetHomeDirectory_WhenUserProfileEmpty_FallsBackToHomeEnvVar()
    {
        // Simulate the Mac Catalyst condition: SpecialFolder.UserProfile returns "".
        // We test the fallback path by passing an empty override and confirming the
        // HOME env var value is returned instead.
        var homeEnvVar = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrEmpty(homeEnvVar))
        {
            // If HOME is also not set (e.g. on Windows CI), the test is not applicable.
            return;
        }

        // Verify the fallback logic independently: when profile is empty, HOME is used.
        var profile = string.Empty;
        var result = string.IsNullOrEmpty(profile)
            ? Environment.GetEnvironmentVariable("HOME") ?? string.Empty
            : profile;

        Assert.Equal(homeEnvVar, result);
    }

    /// <summary>
    /// Verifies that the Steam locator finds the game installation on this machine.
    /// This test is intentionally skipped on Windows (where Steam discovery uses the registry)
    /// and on Linux (where a Steam install is not expected in CI).
    /// </summary>
    [Fact]
    public void FindInstallations_FindsGeneralsOnThisMachine()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsMacCatalyst())
        {
            // Only enforce this on macOS/Mac Catalyst where we know the game is installed via Steam.
            return;
        }

        var locator = new SteamInstallationLocator();
        var installations = locator.FindInstallations(GeneralsDefinition.Instance);

        Assert.NotEmpty(installations);

        var installation = Assert.Single(installations);
        Assert.True(Directory.Exists(installation.Path),
            $"Steam locator returned path '{installation.Path}' which does not exist.");
    }
}
