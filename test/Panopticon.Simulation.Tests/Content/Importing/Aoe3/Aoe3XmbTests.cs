using System;
using System.IO;
using System.Linq;
using Panopticon.FileFormats.Bar;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Content.Importing.Aoe3;

/// <summary>
/// Decodes XMB entries of the installed AoE3 DE Game directory. Interactive:
/// requires AOE3DE_PATH pointing at the install root containing Game.
/// </summary>
[Trait("Category", "Interactive")]
public sealed class Aoe3XmbTests
{
    private readonly ITestOutputHelper _output;

    public Aoe3XmbTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void DecodesProtoyAndExposesMusketeerAnimFile()
    {
        using var fileSystem = Aoe3Install.CreateFileSystem();

        var entry = fileSystem.GetFile(@"Data\protoy.xml.XMB");
        Assert.NotNull(entry);

        using var stream = entry!.Open();
        var document = XmbDocument.Read(stream);
        var root = document.Root;
        Assert.NotNull(root);

        var musketeer = root.Elements("unit").FirstOrDefault(
            element => (string?)element.Attribute("name") == "Musketeer");
        Assert.NotNull(musketeer);

        var animFile = musketeer!.Element("animfile")?.Value;
        Assert.False(string.IsNullOrWhiteSpace(animFile));
        _output.WriteLine($"Musketeer AnimFile: {animFile}");
    }

    [Fact]
    public void DecodesDefaultMaterialDefinition()
    {
        using var fileSystem = Aoe3Install.CreateFileSystem();

        var entry = fileSystem.GetFile(@"Render\materialdefs\default.materialdef.XMB");
        Assert.NotNull(entry);

        using var stream = entry!.Open();
        var document = XmbDocument.Read(stream);

        Assert.NotNull(document.Root);
        Assert.Equal("materialdef", document.Root!.Name.LocalName, ignoreCase: true);
    }

    [Fact]
    public void EveryXmbInDataBarDecodes()
    {
        using var archiveStream = File.OpenRead(
            Path.Combine(Aoe3Install.GetGameDirectory(), "Data", "Data.bar"));
        var archive = BarArchive.Read(archiveStream);

        var xmbEntries = archive.Entries
            .Where(entry => entry.FullName.EndsWith(".xmb", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(xmbEntries);

        var decoded = 0;
        foreach (var entry in xmbEntries)
        {
            using var stream = entry.Open();
            var document = XmbDocument.Read(stream);

            Assert.NotNull(document.Root);
            decoded++;
        }

        _output.WriteLine($"Data.bar XMB count: {decoded}");
    }
}
