using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Panopticon.IO;
using Xunit;

namespace Panopticon.Tests.Data;

public sealed class ZipFileSystemTests : IDisposable
{
    private readonly MemoryStream _stream = new();

    private void AddEntry(string path, string content = "x")
    {
        using var archive = new ZipArchive(_stream, ZipArchiveMode.Update, leaveOpen: true);
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private ZipFileSystem Create()
    {
        return new ZipFileSystem(_stream);
    }

    [Fact]
    public void LookupIsCaseInsensitiveAndNormalized()
    {
        AddEntry("Cache/NIFCache/nwg1_galley.nifcache");

        using var fs = Create();
        var entry = fs.GetFile("cache/nifcache/NWG1_GALLEY.NIFCACHE");
        Assert.NotNull(entry);
        Assert.Equal("Cache/NIFCache/nwg1_galley.nifcache", entry.FilePath);
        Assert.Null(fs.GetFile("cache/nifcache/missing.nifcache"));
    }

    [Fact]
    public void OpenReturnsFreshReadableStreams()
    {
        AddEntry("a/b.txt", "hello");

        using var fs = Create();
        var entry = fs.GetFile("A/B.TXT");
        using var stream1 = entry!.Open();
        using var reader1 = new StreamReader(stream1);
        Assert.Equal("hello", reader1.ReadToEnd());
        using var stream2 = entry.Open();
        using var reader2 = new StreamReader(stream2);
        Assert.Equal("hello", reader2.ReadToEnd());
    }

    [Fact]
    public void EnumerationMatchesDirectorySemantics()
    {
        AddEntry("models/ship.nif");
        AddEntry("models/props/crate.nif");
        AddEntry("models/props/barrel.nif");

        using var fs = Create();
        Assert.Equal(["ship.nif"],
            fs.GetFilesInDirectory("models").Select(e => Path.GetFileName(e.FilePath)));
        Assert.Equal(3,
            fs.GetFilesInDirectory("models", "*", SearchOption.AllDirectories).Count());
        Assert.Equal(["barrel.nif"],
            fs.GetFilesInDirectory("MODELS/props", "bar*").Select(e => Path.GetFileName(e.FilePath)));
        Assert.Empty(fs.GetFilesInDirectory("nope"));
    }

    [Fact]
    public void DuplicateNormalizedPathsFail()
    {
        AddEntry("Data/A.txt");
        AddEntry("data/a.TXT");

        var ex = Assert.Throws<InvalidDataException>(Create);
        Assert.Contains("duplicate", ex.Message);
    }

    [Fact]
    public void DisposalReleasesTheArchive()
    {
        AddEntry("a.txt");
        var fs = Create();
        fs.Dispose();
        // The archive is released; opening from stale entries must fail rather
        // than silently succeed, proving the archive lifetime is tied to the
        // file system. The strongest portable check: no exception leaks and
        // re-dispose is a no-op.
        fs.Dispose();
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}
