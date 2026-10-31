using System;
using System.IO;
using System.Linq;
using System.Text;
using Panopticon.IO;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Aoe3;

public sealed class BarFileSystemTests : IDisposable
{
    private readonly string _rootDirectory;

    public BarFileSystemTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), "PanopticonBarFileSystemTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_rootDirectory, "Art"));
        Directory.CreateDirectory(Path.Combine(_rootDirectory, "Data"));

        File.WriteAllBytes(
            Path.Combine(_rootDirectory, "Art", "Art.bar"),
            SyntheticBarBuilder.Build(
                "Art\\",
                SyntheticBarBuilder.Stored(@"units\a.gr2", Encoding.ASCII.GetBytes("model a")),
                SyntheticBarBuilder.Flagged(@"units\locked.gr2", new byte[4], 4, 2)));

        File.WriteAllBytes(
            Path.Combine(_rootDirectory, "Art", "Art2.bar"),
            SyntheticBarBuilder.Build(
                "Art\\",
                SyntheticBarBuilder.Stored(@"props\b.gr2", Encoding.ASCII.GetBytes("model b"))));

        File.WriteAllBytes(
            Path.Combine(_rootDirectory, "Data", "Data.bar"),
            SyntheticBarBuilder.Build(
                "Data\\",
                SyntheticBarBuilder.Stored(@"protoy.xml", Encoding.ASCII.GetBytes("<proto/>"))));
    }

    public void Dispose()
    {
        Directory.Delete(_rootDirectory, recursive: true);
    }

    [Fact]
    public void GetFileResolvesEntriesAcrossArchives()
    {
        using var fileSystem = new BarFileSystem(_rootDirectory);

        var entry = fileSystem.GetFile(@"Art\units\a.gr2");

        Assert.NotNull(entry);
        Assert.Equal((uint)7, entry!.Length);

        using var stream = entry.Open();
        var buffer = new byte[7];
        stream.ReadExactly(buffer, 0, buffer.Length);

        Assert.Equal("model a", Encoding.ASCII.GetString(buffer));
    }

    [Fact]
    public void GetFileIsCaseInsensitiveAndSeparatorAgnostic()
    {
        using var fileSystem = new BarFileSystem(_rootDirectory);

        var entry = fileSystem.GetFile("art/UNITS/A.GR2");

        Assert.NotNull(entry);
    }

    [Fact]
    public void GetFileReturnsNullForMissingEntry()
    {
        using var fileSystem = new BarFileSystem(_rootDirectory);

        Assert.Null(fileSystem.GetFile(@"Art\units\missing.gr2"));
    }

    [Fact]
    public void GetFilesInDirectoryListsEntriesAcrossArchives()
    {
        using var fileSystem = new BarFileSystem(_rootDirectory);

        var units = fileSystem.GetFilesInDirectory(@"Art\units", "*.gr2").ToList();

        Assert.Equal(2, units.Count);
        Assert.Contains(units, e => e.FilePath.EndsWith("a.gr2"));
        Assert.Contains(units, e => e.FilePath.EndsWith("locked.gr2"));
    }

    [Fact]
    public void GetFilesInDirectoryRecursesIntoSubdirectories()
    {
        using var fileSystem = new BarFileSystem(_rootDirectory);

        var all = fileSystem.GetFilesInDirectory("Art", "*.gr2", SearchOption.AllDirectories).ToList();

        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void OpenEncryptedEntryThrowsWithArchiveAndEntryPath()
    {
        using var fileSystem = new BarFileSystem(_rootDirectory);

        var entry = fileSystem.GetFile(@"Art\units\locked.gr2");

        Assert.NotNull(entry);
        var exception = Assert.Throws<NotSupportedException>(() => entry!.Open());
        Assert.Contains("Art.bar", exception.Message);
        Assert.Contains("locked.gr2", exception.Message);
    }
}
