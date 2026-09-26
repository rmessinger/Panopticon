using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Panopticon.FileFormats.Casc;
using Panopticon.IO;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Data.Casc;

[Trait("Category", "Interactive")]
public sealed class Sc2CascInstallTests
{
    private const string MarinePath = "mods/liberty.sc2mod/base.sc2assets/assets/units/terran/marine/marine.m3";
    private const string ZealotPath = "mods/liberty.sc2mod/base.sc2assets/assets/units/protoss/zealot/zealot.m3";
    private const string ZerglingPath = "mods/liberty.sc2mod/base.sc2assets/assets/units/zerg/zergling/zergling.m3";

    private readonly ITestOutputHelper _output;

    public Sc2CascInstallTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void InstalledStorageMatchesC7aCensusAndProofFiles()
    {
        var root = GetInstallRoot();
        var stopwatch = Stopwatch.StartNew();
        using var storage = CascStorage.Open(root);
        stopwatch.Stop();
        _output.WriteLine($"CASC load: {stopwatch.Elapsed.TotalSeconds:F2}s");

        Assert.Equal(675_745, storage.RawEntryCount);
        Assert.Equal(33_435, CountExtension(storage, ".m3"));
        Assert.Equal(453, CountExtension(storage, ".m3a"));
        Assert.Equal(49_286, CountExtension(storage, ".dds"));

        AssertProofEntry(storage, MarinePath, 520_912, "6c7cd25a530828b1bd67cfadb25f4f4c");
        AssertProofEntry(storage, ZealotPath, 578_720, "cfa1fad07310c629706b8ea05518eb9b");
        AssertProofEntry(storage, ZerglingPath, 479_648, "ecfd1361f87972e29c1969ed6c94072a");

        using var fileSystem = new CascFileSystem(storage);
        AssertFileSystemRead(fileSystem, MarinePath.ToUpperInvariant(), 520_912);
        AssertFileSystemRead(fileSystem, ToggleCase(ZealotPath), 578_720);
        AssertFileSystemRead(fileSystem, ZerglingPath.ToUpperInvariant(), 479_648);
    }

    private static string GetInstallRoot()
    {
        var root = Environment.GetEnvironmentVariable("STARCRAFT_II_PATH");
        if (string.IsNullOrEmpty(root))
        {
            throw new InvalidOperationException(
                "Interactive test requires STARCRAFT_II_PATH pointing at a StarCraft II install.");
        }

        if (!File.Exists(Path.Combine(root, ".build.info")))
        {
            throw new InvalidOperationException(
                $"Interactive test requires a StarCraft II .build.info file at '{root}'.");
        }

        return root;
    }

    private static int CountExtension(CascStorage storage, string extension)
    {
        return storage.Entries.Count(e => Path.GetExtension(e.Path).Equals(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertProofEntry(CascStorage storage, string path, int size, string contentKey)
    {
        var entry = storage.Entries.Single(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(size, entry.Size);
        Assert.Equal(contentKey, entry.ContentKey.ToString());
        Assert.Equal(size, storage.OpenFile(entry.ContentKey).Length);
    }

    private static void AssertFileSystemRead(FileSystem fileSystem, string path, int size)
    {
        var entry = fileSystem.GetFile(path);
        Assert.NotNull(entry);
        Assert.Equal((uint)size, entry.Length);
        using var stream = entry.Open();
        Assert.Equal(size, stream.Length);
    }

    private static string ToggleCase(string path)
    {
        var chars = path.ToCharArray();
        for (var i = 0; i < chars.Length; i += 2)
        {
            chars[i] = char.ToUpperInvariant(chars[i]);
        }

        return new string(chars);
    }
}
