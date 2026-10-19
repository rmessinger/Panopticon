using System.IO;
using System.Linq;
using Panopticon.Data.Map;
using Panopticon.Tools.ReplaySketch.Services;
using Panopticon.Tools.WorldBuilder.Core;
using Xunit;

namespace Panopticon.Tests.Tools.WorldBuilder.Core;

/// <summary>
/// Card WI-A5-30: the scenario-population companion document. Population edits route through the
/// shared WI-A5-14 controller, terrain/map ownership stays in EditorMap, and both linked artifacts
/// survive save/reload.
/// </summary>
public sealed class ScenarioPopulationTests
{
    // NewMapFactory defaults: 32x32 playable, border 6 -> elevation grid 45x45 -> playable world
    // rectangle [60, 390) on both axes (6 border tiles * 10 units per tile).
    private const float MinWorld = 60f;
    private const float SpanWorld = 330f;

    private static EditorMap NewSavedMap(string root)
    {
        var map = NewMapFactory.Create(new NewMapParameters { PlayableWidth = 32, PlayableHeight = 32, InitialHeight = 10 });
        map.SaveAs(Path.Combine(root, "test.map"));
        return map;
    }

    [Fact]
    public void PopulationEdits_RouteThroughTheSharedController()
    {
        var map = NewMapFactory.Create(new NewMapParameters { PlayableWidth = 32, PlayableHeight = 32, InitialHeight = 10 });
        var document = ScenarioPopulationDocument.Open(map);

        Assert.True(document.AddAtWorld("AmericaTankCrusader", 0, MinWorld, MinWorld));
        Assert.True(document.AddAtWorld("GLATankScorpion", 1, MinWorld + SpanWorld, MinWorld + SpanWorld));
        Assert.Equal(2, document.Editor.Placements.Count);
        Assert.True(document.IsDirty);

        document.Editor.Select(0);
        Assert.True(document.MoveSelectedToWorld(MinWorld + SpanWorld / 2, MinWorld + SpanWorld / 2));
        var moved = Assert.IsType<Panopticon.Tools.ReplaySketch.Model.NormalizedPosition>(document.Editor.Placements[0].Position);
        Assert.Equal(0.5f, moved.NormX, 3);
        Assert.Equal(0.5f, moved.NormY, 3);

        Assert.True(document.ChangeOwnerSelected(1));
        Assert.Equal(1, document.Editor.Placements[0].PlayerIndex);

        Assert.True(document.DeleteSelected());
        Assert.Single(document.Editor.Placements);

        // Rejected edits report diagnostics and never mutate through the shared controller.
        var count = document.Editor.Placements.Count;
        Assert.False(document.AddAtWorld("AmericaTankCrusader", 9, MinWorld, MinWorld));
        Assert.NotEmpty(document.LastDiagnostics);
        Assert.Equal(count, document.Editor.Placements.Count);
    }

    [Fact]
    public void TerrainAndPopulationOwnershipStaySeparate()
    {
        var map = NewMapFactory.Create(new NewMapParameters { PlayableWidth = 32, PlayableHeight = 32, InitialHeight = 10 });
        var document = ScenarioPopulationDocument.Open(map);
        Assert.True(document.AddAtWorld("AmericaTankCrusader", 0, MinWorld, MinWorld));

        // Population edits do not touch the map's terrain or objects.
        Assert.Equal(10, map.MapFile.HeightMapData.Elevations[10, 10]);
        Assert.Empty(map.MapFile.ObjectsList.Objects.Where(o => o.TypeName == "AmericaTankCrusader"));

        // Map edits do not touch the population document.
        map.UndoStack.Push(HeightMapEdit.Capture(map.MapFile.HeightMapData, 10, 10, 1, 1, elevations => elevations[10, 10] = 42, "raise"));
        Assert.Single(document.Editor.Placements);
        Assert.Equal(42, map.MapFile.HeightMapData.Elevations[10, 10]);
    }

    [Fact]
    public void SaveReload_PreservesBothLinkedArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wb-population-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var map = NewSavedMap(root);
            map.UndoStack.Push(HeightMapEdit.Capture(map.MapFile.HeightMapData, 10, 10, 1, 1, elevations => elevations[10, 10] = 42, "raise"));
            map.Save();

            var document = ScenarioPopulationDocument.Open(map);
            Assert.True(document.AddAtWorld("AmericaTankCrusader", 0, MinWorld + 100, MinWorld + 100));
            document.Editor.Select(0);
            Assert.True(document.ChangeOwnerSelected(1));
            document.Save();

            // Map artifact: elevation edit survived.
            var reloadedMap = MapFile.FromStream(File.OpenRead(Path.Combine(root, "test.map")));
            Assert.Equal(42, reloadedMap.HeightMapData.Elevations[10, 10]);

            // Scenario artifact: placement + owner survived through the companion reload.
            var reopened = EditorMap.Open(reloadedMap, Path.Combine(root, "test.map"));
            var reloadedDocument = ScenarioPopulationDocument.Open(reopened);
            var placement = Assert.Single(reloadedDocument.Editor.Placements);
            Assert.Equal("AmericaTankCrusader", placement.DefinitionName);
            Assert.Equal(1, placement.PlayerIndex);
            Assert.False(reloadedDocument.IsDirty);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WorldToNormalized_ClampsOutOfRangeCoordinates()
    {
        var map = NewMapFactory.Create(new NewMapParameters { PlayableWidth = 32, PlayableHeight = 32, InitialHeight = 10 });
        var document = ScenarioPopulationDocument.Open(map);

        Assert.Equal(0f, document.WorldToNormalized(-500f, -500f).NormX);
        Assert.Equal(0f, document.WorldToNormalized(-500f, -500f).NormY);
        Assert.Equal(1f, document.WorldToNormalized(10_000f, 10_000f).NormX);
        Assert.Equal(1f, document.WorldToNormalized(10_000f, 10_000f).NormY);
    }

    [Fact]
    public void DefaultScaffold_IsAValidScenario()
    {
        var map = NewMapFactory.Create(new NewMapParameters { PlayableWidth = 32, PlayableHeight = 32, InitialHeight = 10 });
        var document = ScenarioPopulationDocument.Open(map);

        var report = ScenarioValidator.Validate(document.Scenario, null);
        Assert.Empty(report.Diagnostics);
    }
}
