using System;
using System.IO;
using Panopticon.Tools.ReplaySketch.Services;

namespace Panopticon.Tests.Tools.ReplaySketch;

/// <summary>
/// Tests for <see cref="GeneralsInstallationService"/> — specifically the
/// <see cref="GeneralsInstallationService.SetPath"/> override that lets the app
/// work without a shell-inherited CNC_GENERALS_PATH environment variable
/// (which MAUI Mac Catalyst apps cannot see).
/// </summary>
public class GeneralsInstallationServiceTests : IDisposable
{
    // Save and restore CNC_GENERALS_PATH so tests don't pollute each other.
    private readonly string? _originalEnvValue =
        Environment.GetEnvironmentVariable("CNC_GENERALS_PATH");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CNC_GENERALS_PATH", _originalEnvValue);
    }

    // ── SetPath ───────────────────────────────────────────────────────────────

    [Fact]
    public void SetPath_SetsEnvironmentVariable()
    {
        var svc = new GeneralsInstallationService();
        var path = Path.GetTempPath();

        svc.SetPath(path);

        Assert.Equal(path, Environment.GetEnvironmentVariable("CNC_GENERALS_PATH"));
    }

    [Fact]
    public void SetPath_ExistingDirectory_MakesInstallationDiscoverable()
    {
        // Any existing directory satisfies the EnvironmentInstallationLocator check.
        var path = Path.GetTempPath();
        var svc = new GeneralsInstallationService();

        svc.SetPath(path);
        var found = svc.TryGetInstallation(out var installation);

        Assert.True(found);
        Assert.NotNull(installation);
        Assert.Equal(path, installation!.Path);
    }

    [Fact]
    public void SetPath_NonExistentDirectory_EnvVarIsStillUpdated()
    {
        // Even with a non-existent path, the env var should be set (other locators
        // like Steam may still find an installation, so we don't assert on discovery).
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var svc = new GeneralsInstallationService();

        svc.SetPath(path);

        Assert.Equal(path, Environment.GetEnvironmentVariable("CNC_GENERALS_PATH"));
    }

    [Fact]
    public void SetPath_AfterCachedResult_RetriesDiscoveryAndReturnsNewPath()
    {
        // Prime the cache with whatever the current env finds.
        var svc = new GeneralsInstallationService();
        _ = svc.TryGetInstallation(out _);

        // SetPath to a specific existing directory; EnvironmentInstallationLocator
        // is checked first so this path wins over Steam.
        var path = Path.GetTempPath();
        svc.SetPath(path);
        var found = svc.TryGetInstallation(out var installation);

        Assert.True(found);
        Assert.Equal(path, installation!.Path);
    }

    [Fact]
    public void SetPath_CalledTwice_UsesLatestPath()
    {
        var first = Path.GetTempPath();
        var second = Path.Combine(Path.GetTempPath(), "ReplaySketchTest_" + Guid.NewGuid());
        Directory.CreateDirectory(second);
        try
        {
            var svc = new GeneralsInstallationService();
            svc.SetPath(first);
            _ = svc.TryGetInstallation(out _); // prime the cache

            svc.SetPath(second);
            svc.TryGetInstallation(out var installation);

            Assert.Equal(second, installation?.Path);
        }
        finally
        {
            Directory.Delete(second);
        }
    }

    // ── Browse-and-Load fix scenarios ─────────────────────────────────────────
    // These tests cover the specific scenario fixed when the Browse button was
    // changed from a file picker (strip directory) to a folder picker:
    // the installation path must be set and discoverable unconditionally, even
    // if map loading subsequently fails (e.g. the folder exists but contains
    // no valid game files).

    [Fact]
    public void SetPath_InstallationDiscoverable_BeforeMapLoadAttempt()
    {
        // Simulates the Browse+Load flow: after the user browses to a folder,
        // SetPath is called immediately. Installation must be discoverable right
        // away — before LoadMap runs and regardless of whether it succeeds.
        var path = Path.GetTempPath();
        var svc = new GeneralsInstallationService();

        svc.SetPath(path);

        var found = svc.TryGetInstallation(out var installation);

        Assert.True(found);
        Assert.Equal(path, installation!.Path);
    }

    [Fact]
    public void SetPath_InstallationDiscoverable_EvenWhenDirectoryHasNoGameFiles()
    {
        // A valid directory with no game files is still "discovered" — the
        // EnvironmentInstallationLocator only checks that the directory exists.
        // This is the state after Browse picks a folder but the folder turns out
        // to not contain a loadable map; the installation path is still set.
        var emptyDir = Path.Combine(Path.GetTempPath(), "ReplaySketchTest_Empty_" + Guid.NewGuid());
        Directory.CreateDirectory(emptyDir);
        try
        {
            var svc = new GeneralsInstallationService();

            svc.SetPath(emptyDir);
            var found = svc.TryGetInstallation(out var installation);

            Assert.True(found);
            Assert.Equal(emptyDir, installation!.Path);
        }
        finally
        {
            Directory.Delete(emptyDir);
        }
    }

    [Fact]
    public void SetPath_EnvVarVisibleToNewServiceInstance()
    {
        // Simulates the app-restart restore path: on startup the VM reads the
        // saved preference and calls SetPath, which sets the process env var.
        // A new GeneralsInstallationService instance (created anywhere in the
        // same process) must see the same path via the env var.
        var path = Path.GetTempPath();
        var firstSvc = new GeneralsInstallationService();
        firstSvc.SetPath(path);

        var secondSvc = new GeneralsInstallationService();
        var found = secondSvc.TryGetInstallation(out var installation);

        Assert.True(found);
        Assert.Equal(path, installation!.Path);
    }
}
