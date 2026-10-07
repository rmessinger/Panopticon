using System;
using System.IO;
using System.Linq;
using Panopticon.Content.Importing;
using Panopticon.IO;
using Xunit;

namespace Panopticon.Tests.Content.Importing;

public sealed class ExternalSourceFileSystemTests : IDisposable
{
    private readonly string _root;
    private readonly string _otherRoot;
    private readonly string _outsideRoot;

    public ExternalSourceFileSystemTests()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "panopticon-extsrc-" + Guid.NewGuid().ToString("N"));
        _root = Path.Combine(baseDir, "modelroot");
        _otherRoot = Path.Combine(baseDir, "animroot");
        _outsideRoot = Path.Combine(baseDir, "outside");

        Directory.CreateDirectory(Path.Combine(_root, "textures"));
        Directory.CreateDirectory(_otherRoot);
        Directory.CreateDirectory(_outsideRoot);

        File.WriteAllText(Path.Combine(_root, "tank.gltf"), "{}");
        File.WriteAllText(Path.Combine(_root, "textures", "hull.png"), "png");
        File.WriteAllText(Path.Combine(_otherRoot, "walk.gltf"), "{}");
        File.WriteAllText(Path.Combine(_outsideRoot, "evil.gltf"), "{}");
    }

    public void Dispose()
    {
        Directory.Delete(Path.GetDirectoryName(_root)!, true);
    }

    private ExternalSourceFileSystem CreateFileSystem(string root)
    {
        return new ExternalSourceFileSystem(new DiskFileSystem(root));
    }

    [Fact]
    public void ValidKeys_ResolveNestedRelatives()
    {
        using var fileSystem = CreateFileSystem(_root);

        var model = fileSystem.GetFile("tank.gltf");
        Assert.NotNull(model);

        var texture = fileSystem.GetFile("textures/hull.png");
        Assert.NotNull(texture);
        Assert.Equal("textures" + Path.DirectorySeparatorChar + "hull.png", texture!.FilePath);
    }

    [Fact]
    public void MissingFile_ReturnsNull()
    {
        using var fileSystem = CreateFileSystem(_root);

        Assert.Null(fileSystem.GetFile("absent.gltf"));
        Assert.Null(fileSystem.GetFile("textures/absent.png"));
    }

    [Theory]
    [InlineData("/tank.gltf")]
    [InlineData("textures\\hull.png")]
    [InlineData("textures//hull.png")]
    [InlineData("textures/")]
    [InlineData("./tank.gltf")]
    [InlineData("textures/../tank.gltf")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("textures/.")]
    [InlineData("tank.gltf:x")]
    [InlineData("C:/tank.gltf")]
    [InlineData(" ")]
    public void NonNormalizedKeys_Throw(string key)
    {
        using var fileSystem = CreateFileSystem(_root);

        Assert.Throws<InvalidDataException>(() => fileSystem.GetFile(key));
    }

    [Fact]
    public void EmptyFileKey_Throws()
    {
        using var fileSystem = CreateFileSystem(_root);

        Assert.Throws<InvalidDataException>(() => fileSystem.GetFile(""));
    }

    [Fact]
    public void RootEnumeration_ListsOnlyRootEntries()
    {
        using var fileSystem = CreateFileSystem(_root);

        var top = fileSystem.GetFilesInDirectory("").ToArray();
        Assert.Single(top);
        Assert.Equal("tank.gltf", Path.GetFileName(top[0].FilePath));

        var recursive = fileSystem.GetFilesInDirectory("", "*", SearchOption.AllDirectories).ToArray();
        Assert.Equal(2, recursive.Length);
    }

    [Fact]
    public void SubdirectoryEnumeration_ListsNestedFiles()
    {
        using var fileSystem = CreateFileSystem(_root);

        var entries = fileSystem.GetFilesInDirectory("textures").ToArray();
        var texture = Assert.Single(entries);
        Assert.Equal("hull.png", Path.GetFileName(texture.FilePath));
    }

    [Fact]
    public void CaseDifferenceKey_ResolvesThroughDiskFallback()
    {
        using var fileSystem = CreateFileSystem(_root);

        // DiskFileSystem falls back to a case-insensitive walk, so casing differences
        // still resolve; the wrapper adds policy, not case sensitivity.
        var entry = fileSystem.GetFile("TANK.GLTF");
        Assert.NotNull(entry);
    }

    [Fact]
    public void SymlinkEscape_IsRejected()
    {
        var linkPath = Path.Combine(_root, "escape.gltf");
        try
        {
            File.CreateSymbolicLink(linkPath, Path.Combine(_outsideRoot, "evil.gltf"));
        }
        catch (IOException)
        {
            // Symlink creation requires privileges on some platforms; skip.
            return;
        }

        using var fileSystem = CreateFileSystem(_root);

        Assert.Throws<InvalidDataException>(() => fileSystem.GetFile("escape.gltf"));
    }

    [Fact]
    public void IndependentRoots_DoNotSeeEachOthersFiles()
    {
        using var modelFileSystem = CreateFileSystem(_root);
        using var animationFileSystem = CreateFileSystem(_otherRoot);

        Assert.NotNull(modelFileSystem.GetFile("tank.gltf"));
        Assert.Null(modelFileSystem.GetFile("walk.gltf"));

        Assert.NotNull(animationFileSystem.GetFile("walk.gltf"));
        Assert.Null(animationFileSystem.GetFile("tank.gltf"));
    }

    [Fact]
    public void DisposedFileSystem_RejectsAccess()
    {
        var fileSystem = CreateFileSystem(_root);
        fileSystem.Dispose();
        fileSystem.Dispose();

        Assert.Throws<ObjectDisposedException>(() => fileSystem.GetFile("tank.gltf"));
    }

    [Fact]
    public void Mount_ExposesRootVirtualKeyAndContext()
    {
        using var mount = new ExternalAssetSourceMount(Path.Combine(_root, "tank.gltf"));

        Assert.Equal("tank.gltf", mount.RootVirtualKey);
        Assert.Equal(_root, mount.CanonicalRootPath);
        Assert.Equal(ExternalAssetInspection.GltfFormatId, mount.FormatSelection.FormatId);
        Assert.NotNull(mount.ImportContext.FileSystem.GetFile(mount.RootVirtualKey));
        Assert.NotNull(mount.ImportContext.FileSystem.GetFile("textures/hull.png"));
    }

    [Fact]
    public void Mount_UnsupportedFormat_ThrowsExt001()
    {
        var objectPath = Path.Combine(_root, "decoy.obj");
        File.WriteAllText(objectPath, "{}");

        var exception = Assert.Throws<InvalidDataException>(
            () => new ExternalAssetSourceMount(objectPath));

        Assert.Contains(ExternalFormatSelectionResult.UnsupportedFormatCode, exception.Message);
    }

    [Fact]
    public void Mount_RequiresAbsolutePath()
    {
        Assert.Throws<ArgumentException>(() => new ExternalAssetSourceMount("relative/tank.gltf"));
        Assert.Throws<ArgumentException>(() => new ExternalAssetSourceMount(""));
    }

    [Fact]
    public void Mount_ModelAndAnimationMounts_AreIndependent()
    {
        using var modelMount = new ExternalAssetSourceMount(Path.Combine(_root, "tank.gltf"));
        using var animationMount = new ExternalAssetSourceMount(Path.Combine(_otherRoot, "walk.gltf"));

        Assert.Equal(_root, modelMount.CanonicalRootPath);
        Assert.Equal(_otherRoot, animationMount.CanonicalRootPath);

        Assert.Null(modelMount.ImportContext.FileSystem.GetFile("walk.gltf"));
        Assert.Null(animationMount.ImportContext.FileSystem.GetFile("tank.gltf"));

        Assert.NotSame(modelMount.ImportContext.FileSystem, animationMount.ImportContext.FileSystem);
    }

    [Fact]
    public void Mount_Dispose_IsIdempotent_AndClosesFileSystem()
    {
        var mount = new ExternalAssetSourceMount(Path.Combine(_root, "tank.gltf"));
        var fileSystem = mount.ImportContext.FileSystem;

        mount.Dispose();
        mount.Dispose();

        Assert.Throws<ObjectDisposedException>(() => fileSystem.GetFile("tank.gltf"));
    }
}
