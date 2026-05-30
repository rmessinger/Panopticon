using System;
using System.IO;
using OpenSage.Tools.ReplaySketch.Services;

namespace OpenSage.Tests.Tools.ReplaySketch;

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
}
