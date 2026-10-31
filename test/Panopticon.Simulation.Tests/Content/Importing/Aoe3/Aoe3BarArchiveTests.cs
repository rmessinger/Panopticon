using System;
using System.IO;
using Panopticon.FileFormats.Bar;
using Xunit;

namespace Panopticon.Tests.Content.Importing.Aoe3;

/// <summary>
/// Reads every .bar archive of the installed AoE3 DE Game directory and opens
/// the C10 reference model entry. Interactive: requires AOE3DE_PATH pointing
/// at the install root containing Game.
/// </summary>
[Trait("Category", "Interactive")]
public sealed class Aoe3BarArchiveTests
{
    private const string MusketeerModelPath = @"Art\units\infantry_ranged\musketeer\musketeer_4age.gr2";

    [Fact]
    public void AllArchivesParseToEntryTables()
    {
        var archiveCount = 0;

        foreach (var archivePath in Directory.EnumerateFiles(Aoe3Install.GetGameDirectory(), "*.bar", SearchOption.AllDirectories))
        {
            using var stream = File.OpenRead(archivePath);
            var archive = BarArchive.Read(stream);

            Assert.NotEmpty(archive.Entries);
            archiveCount++;
        }

        Assert.Equal(42, archiveCount);
    }

    [Fact]
    public void MusketeerModelOpensAtRecordedSize()
    {
        using var fileSystem = Aoe3Install.CreateFileSystem();

        var entry = fileSystem.GetFile(MusketeerModelPath);

        Assert.NotNull(entry);
        using var stream = entry!.Open();
        Assert.Equal(entry.Length, (uint)stream.Length);

        var buffer = new byte[entry.Length];
        stream.ReadExactly(buffer, 0, buffer.Length);

        Assert.Equal(-1, stream.ReadByte());
    }
}
