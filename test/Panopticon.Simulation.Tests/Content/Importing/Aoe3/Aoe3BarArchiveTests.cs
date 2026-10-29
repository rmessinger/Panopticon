using System;
using System.IO;
using System.Linq;
using Panopticon.FileFormats.Bar;
using Panopticon.IO;
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

    private static string GetGameDirectory()
    {
        var root = Environment.GetEnvironmentVariable("AOE3DE_PATH");
        if (string.IsNullOrEmpty(root))
        {
            throw new InvalidOperationException(
                "Interactive test requires AOE3DE_PATH pointing at an Age of Empires III DE install root (containing Game).");
        }

        var gameDirectory = Path.Combine(root, "Game");
        if (!Directory.Exists(gameDirectory))
        {
            throw new InvalidOperationException(
                $"Interactive test requires the AoE3 DE Game directory at '{gameDirectory}'.");
        }

        return gameDirectory;
    }

    [Fact]
    public void AllArchivesParseToEntryTables()
    {
        var archiveCount = 0;

        foreach (var archivePath in Directory.EnumerateFiles(GetGameDirectory(), "*.bar", SearchOption.AllDirectories))
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
        using var fileSystem = new BarFileSystem(GetGameDirectory());

        var entry = fileSystem.GetFile(MusketeerModelPath);

        Assert.NotNull(entry);
        using var stream = entry!.Open();
        Assert.Equal(entry.Length, (uint)stream.Length);

        var buffer = new byte[entry.Length];
        stream.ReadExactly(buffer, 0, buffer.Length);

        Assert.Equal(-1, stream.ReadByte());
    }
}
