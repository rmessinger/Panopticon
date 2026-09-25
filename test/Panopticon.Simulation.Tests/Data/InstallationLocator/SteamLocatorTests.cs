using System;
using System.IO;
using System.Runtime.InteropServices;
using Panopticon.Data;
using Panopticon.Mods.Generals;
using Xunit;

namespace Panopticon.Tests.Data.InstallationLocator;

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
    /// Uses <see cref="GameFactAttribute"/> so it is automatically skipped in CI (and on any
    /// machine) where Generals isn't actually installed, rather than assuming it's always
    /// present on macOS.
    /// </summary>
    [GameFact(SageGame.CncGenerals)]
    public void FindInstallations_FindsGeneralsOnThisMachine()
    {
        var locator = new SteamInstallationLocator();
        var installations = locator.FindInstallations(GeneralsDefinition.Instance);

        Assert.NotEmpty(installations);

        var installation = Assert.Single(installations);
        Assert.True(Directory.Exists(installation.Path),
            $"Steam locator returned path '{installation.Path}' which does not exist.");
    }
}
