#nullable enable

using System;
using System.IO;
using Xunit;

namespace Panopticon.Tests.Terrain;

[Collection("SerialGameTests")]
public sealed class EditorInstancingArtifactRootTests
{
    private const string RunStamp = "20260101T000000Z";

    [Fact]
    public void ConfiguredArtifactRoot_IsHonored()
    {
        var root = Path.Combine(Path.GetTempPath(), $"b4a-g17-root-{Guid.NewGuid():N}");
        var expected = Path.Combine(root, $"b4a-g17-validation-{RunStamp}");

        WithEnvironment(EditorInstancingInstalledTests.ArtifactRootVariable, root, () =>
        {
            Assert.Equal(expected, EditorInstancingInstalledTests.ResolveArtifactDirectory(RunStamp));
        });
    }

    [Fact]
    public void EmptyArtifactRoot_FallsBackToUniqueTempDirectory()
    {
        WithEnvironment(EditorInstancingInstalledTests.ArtifactRootVariable, string.Empty, () =>
        {
            var dir = EditorInstancingInstalledTests.ResolveArtifactDirectory(RunStamp);
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(dir));
        });
    }

    [Fact]
    public void DefaultArtifactRoot_IsUnderTempPathAndUniquePerCall()
    {
        WithEnvironment(EditorInstancingInstalledTests.ArtifactRootVariable, null, () =>
        {
            var first = EditorInstancingInstalledTests.ResolveArtifactDirectory(RunStamp);
            var second = EditorInstancingInstalledTests.ResolveArtifactDirectory(RunStamp);

            var tempRoot = Path.GetFullPath(Path.GetTempPath());
            Assert.StartsWith(tempRoot, Path.GetFullPath(first));
            Assert.StartsWith(tempRoot, Path.GetFullPath(second));
            Assert.NotEqual(first, second);
        });
    }

    [Fact]
    public void CapturePublication_IsSkippedWhenCaptureRootUnset()
    {
        WithEnvironment(EditorInstancingInstalledTests.CaptureRootVariable, null, () =>
        {
            Assert.Null(EditorInstancingInstalledTests.ResolveCaptureRoot());
        });
    }

    [Fact]
    public void CapturePublication_UsesConfiguredCaptureRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"b4a-g17-capture-{Guid.NewGuid():N}");

        WithEnvironment(EditorInstancingInstalledTests.CaptureRootVariable, root, () =>
        {
            Assert.Equal(root, EditorInstancingInstalledTests.ResolveCaptureRoot());
        });
    }

    private static void WithEnvironment(string name, string? value, Action body)
    {
        var previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        try
        {
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previous);
        }
    }
}
