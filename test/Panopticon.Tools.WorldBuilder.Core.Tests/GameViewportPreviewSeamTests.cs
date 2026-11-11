#nullable enable

using System;
using System.IO;
using Panopticon;
using Panopticon.Content.Importing.MapSources.Package;
using Panopticon.IO;
using Panopticon.Tools.WorldBuilder.Core;
using Xunit;

namespace Panopticon.Tests.Tools.WorldBuilder.Core;

public sealed class GameViewportPreviewSeamTests : IDisposable
{
    private readonly string _tempRoot;

    public GameViewportPreviewSeamTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "wb-preview-seam-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void AdmitsForPreview_FollowsExpectedTruthTable(bool hasMapEntry, bool isDirectoryPackage, bool expected)
    {
        var admitted = GameViewportRenderer.AdmitsForPreview(hasMapEntry, isDirectoryPackage);
        Assert.Equal(expected, admitted);
    }

    [Fact]
    public void IsDirectoryPackagePath_DirectoryWithManifest_ReturnsTrue()
    {
        var packageDir = Path.Combine(_tempRoot, "SamplePackage");
        Directory.CreateDirectory(packageDir);
        var manifestPath = Path.Combine(packageDir, MapPackageFormat.ManifestFileName);
        File.WriteAllText(manifestPath, "{}");

        Assert.True(Game.IsDirectoryPackagePath(packageDir));
        Assert.True(Game.IsDirectoryPackagePath(packageDir + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void IsDirectoryPackagePath_ManifestAsFilePath_ReturnsTrue()
    {
        var packageDir = Path.Combine(_tempRoot, "ManifestFilePackage");
        Directory.CreateDirectory(packageDir);
        var manifestPath = Path.Combine(packageDir, MapPackageFormat.ManifestFileName);
        File.WriteAllText(manifestPath, "{}");

        Assert.True(Game.IsDirectoryPackagePath(manifestPath));
    }

    [Fact]
    public void IsDirectoryPackagePath_DirectoryWithoutManifest_ReturnsFalse()
    {
        var regularDir = Path.Combine(_tempRoot, "NotAPackage");
        Directory.CreateDirectory(regularDir);
        File.WriteAllText(Path.Combine(regularDir, "unrelated.txt"), "hello");

        Assert.False(Game.IsDirectoryPackagePath(regularDir));
    }

    [Fact]
    public void IsDirectoryPackagePath_NonexistentPath_ReturnsFalse()
    {
        var missingPath = Path.Combine(_tempRoot, "DoesNotExist", "none");

        Assert.False(Game.IsDirectoryPackagePath(missingPath));
    }

    [Fact]
    public void IsDirectoryPackagePath_PlainFile_ReturnsFalse()
    {
        var legacyMap = Path.Combine(_tempRoot, "test.map");
        File.WriteAllText(legacyMap, "legacy map data");

        Assert.False(Game.IsDirectoryPackagePath(legacyMap));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsDirectoryPackagePath_NullOrWhitespace_ReturnsFalse(string? path)
    {
        Assert.False(Game.IsDirectoryPackagePath(path));
    }

    [Fact]
    public void LegacyPath_RefusedWithoutContentManagerEntry_AndAdmittedWithEntry()
    {
        var legacyMapPath = Path.Combine(_tempRoot, "Standalone.map");
        File.WriteAllText(legacyMapPath, "dummy map content");

        var isPackage = Game.IsDirectoryPackagePath(legacyMapPath);
        Assert.False(isPackage);

        var admittedWithoutEntry = GameViewportRenderer.AdmitsForPreview(hasMapEntry: false, isDirectoryPackage: isPackage);
        Assert.False(admittedWithoutEntry);

        var admittedWithEntry = GameViewportRenderer.AdmitsForPreview(hasMapEntry: true, isDirectoryPackage: isPackage);
        Assert.True(admittedWithEntry);
    }

    [Fact]
    public void DirectoryPackage_AdmittedWithoutContentManagerEntry()
    {
        var packageDir = Path.Combine(_tempRoot, "NativePackage");
        Directory.CreateDirectory(packageDir);
        File.WriteAllText(Path.Combine(packageDir, MapPackageFormat.ManifestFileName), "{}");

        var isPackage = Game.IsDirectoryPackagePath(packageDir);
        Assert.True(isPackage);

        var admitted = GameViewportRenderer.AdmitsForPreview(hasMapEntry: false, isDirectoryPackage: isPackage);
        Assert.True(admitted);
    }

    [Fact]
    public void IsDirectoryPackagePath_WithDiskFileSystemEntry_ValidatesCorrectly()
    {
        var diskRoot = Path.Combine(_tempRoot, "DiskFsRoot");
        var pkgRelative = Path.Combine("Maps", "PkgMap");
        var pkgFull = Path.Combine(diskRoot, pkgRelative);
        Directory.CreateDirectory(pkgFull);
        File.WriteAllText(Path.Combine(pkgFull, MapPackageFormat.ManifestFileName), "{}");

        var diskFs = new DiskFileSystem(diskRoot);
        var packageEntry = new FileSystemEntry(diskFs, pkgRelative, 0, () => Stream.Null);

        Assert.True(Game.IsDirectoryPackagePath(pkgRelative, packageEntry));

        var legacyRelative = Path.Combine("Maps", "Legacy.map");
        var legacyFull = Path.Combine(diskRoot, legacyRelative);
        File.WriteAllText(legacyFull, "legacy");
        var legacyEntry = new FileSystemEntry(diskFs, legacyRelative, 6, () => new MemoryStream([1, 2, 3]));

        Assert.False(Game.IsDirectoryPackagePath(legacyRelative, legacyEntry));
    }
}
