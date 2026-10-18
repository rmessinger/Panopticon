using System.IO;
using Panopticon.Data.Map;
using Panopticon.Tools.WorldBuilder.Core;

namespace Panopticon.Tests.Tools.WorldBuilder.Core;

/// <summary>
/// Card WI-A5-34: Save writes the map.ini and map.str companions next to the .map file and
/// Open restores them; absent companions default to empty strings; the .map stream stays
/// authoritative for map data.
/// </summary>
public sealed class MapCompanionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "worldbuilder-companions-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void SaveWritesCompanionsAndOpenRestoresThem()
    {
        var map = NewMap();
        map.MapIniContent = "; authored map.ini";
        map.MapStrContent = "MAPEDIT:MapName=\"Authored\"";
        var mapPath = Path.Combine(_root, "authored.map");
        Directory.CreateDirectory(_root);

        map.SaveAs(mapPath);

        Assert.True(File.Exists(mapPath + ".ini"));
        Assert.Equal("; authored map.ini", File.ReadAllText(mapPath + ".ini"));
        Assert.True(File.Exists(mapPath + ".str"));
        Assert.Equal("MAPEDIT:MapName=\"Authored\"", File.ReadAllText(mapPath + ".str"));

        using var stream = File.OpenRead(mapPath);
        var reopened = EditorMap.Open(MapFile.FromStream(stream), mapPath);
        Assert.Equal("; authored map.ini", reopened.MapIniContent);
        Assert.Equal("MAPEDIT:MapName=\"Authored\"", reopened.MapStrContent);
        Assert.False(reopened.IsDirty);
    }

    [Fact]
    public void AbsentCompanions_DefaultToEmpty()
    {
        var map = NewMap();
        var mapPath = Path.Combine(_root, "plain.map");
        Directory.CreateDirectory(_root);

        map.SaveAs(mapPath);

        Assert.False(File.Exists(mapPath + ".ini"));
        Assert.False(File.Exists(mapPath + ".str"));

        using var stream = File.OpenRead(mapPath);
        var reopened = EditorMap.Open(MapFile.FromStream(stream), mapPath);
        Assert.Equal(string.Empty, reopened.MapIniContent);
        Assert.Equal(string.Empty, reopened.MapStrContent);
    }

    [Fact]
    public void SaveAs_RetargetsCompanions_AndLeavesTheStaleOnesInPlace()
    {
        var map = NewMap();
        map.MapIniContent = "; first location";
        var firstPath = Path.Combine(_root, "first.map");
        var secondPath = Path.Combine(_root, "second.map");
        Directory.CreateDirectory(_root);

        map.SaveAs(firstPath);
        map.MapIniContent = "; second location";
        map.SaveAs(secondPath);

        Assert.Equal("; first location", File.ReadAllText(firstPath + ".ini"));
        Assert.True(File.Exists(secondPath + ".ini"));
        Assert.Equal("; second location", File.ReadAllText(secondPath + ".ini"));
    }

    private EditorMap NewMap()
    {
        return NewMapFactory.Create(new NewMapParameters
        {
            PlayableWidth = 32,
            PlayableHeight = 32,
            InitialHeight = 10,
        });
    }
}
