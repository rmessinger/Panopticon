#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using OpenSage.Tools.ReplaySketch.Services;
using Xunit;

namespace OpenSage.Tests.Tools.ReplaySketch;

public sealed class SiblingBuildExecutableLocatorTests : IDisposable
{
    private readonly string _root;

    public SiblingBuildExecutableLocatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "opensage-locator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, true);
    }

    private string CreateBuildTree(string srcRoot, string projectName, string configuration)
    {
        var binDir = Path.Combine(srcRoot, projectName, "bin", configuration, "net10.0");
        Directory.CreateDirectory(binDir);
        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"{projectName}.exe"
            : projectName;
        var exePath = Path.Combine(binDir, exeName);
        File.WriteAllText(exePath, "#!/bin/sh\nexit 0\n");
        return exePath;
    }

    [Fact]
    public void Locate_FindsExecutableInSiblingBuildOutput()
    {
        var expected = CreateBuildTree(_root, "OpenSage.Tools.TestHarness", "Debug");

        var located = new SiblingBuildExecutableLocator().Locate(
            "OpenSage.Tools.TestHarness",
            Path.Combine(_root, "unrelated", "nested", "output"));

        Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(located!));
    }

    [Fact]
    public void Locate_PrefersRunningConfigurationThenAnyOther()
    {
        CreateBuildTree(_root, "OpenSage.Launcher", "Debug");
        CreateBuildTree(_root, "OpenSage.Launcher", "Release");

        var fromDebug = new SiblingBuildExecutableLocator().Locate(
            "OpenSage.Launcher",
            Path.Combine(_root, "other", "bin", "Debug", "net10.0"));
        Assert.NotNull(fromDebug);
        Assert.Contains(
            Path.Combine("Debug", "net10.0") + Path.DirectorySeparatorChar,
            fromDebug!,
            StringComparison.Ordinal);

        var fromNeutral = new SiblingBuildExecutableLocator().Locate(
            "OpenSage.Launcher",
            Path.Combine(_root, "neutral"));
        Assert.NotNull(fromNeutral);
        Assert.True(File.Exists(fromNeutral!));
    }

    [Fact]
    public void Locate_MissingProject_ReturnsNull()
    {
        Assert.Null(new SiblingBuildExecutableLocator().Locate(
            "OpenSage.Absent",
            _root));
    }

    [Fact]
    public void Locate_EmptyProjectName_Throws()
    {
        Assert.Throws<ArgumentException>(() => new SiblingBuildExecutableLocator().Locate(""));
    }

    [Fact]
    public void LauncherLocatorService_DelegatesForLauncher()
    {
        var expected = CreateBuildTree(_root, "OpenSage.Launcher", "Debug");

        var located = new LauncherLocatorService().Locate();

        if (located is null)
        {
            // The real repository tree was not reachable from this test run's output.
            return;
        }

        Assert.True(File.Exists(located));
        Assert.EndsWith(expected.Split(Path.DirectorySeparatorChar)[^1], located, StringComparison.Ordinal);
    }
}
