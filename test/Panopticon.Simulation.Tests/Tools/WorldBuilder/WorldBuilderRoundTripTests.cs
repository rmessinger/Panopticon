using System;
using System.IO;
using System.Linq;
using Panopticon.Data.Map;
using Panopticon.Logic;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Tests.Data;
using Panopticon.Tools.WorldBuilder.Core;
using Xunit;

namespace Panopticon.Tests.Tools.WorldBuilder;

/// <summary>
/// WI-A5-02: the WorldBuilder author-to-save-to-load round-trip over a real new map. Card 27
/// made pushes the single application owner, card 28 made new maps writable, cards 34/35 gave
/// them companions and playable defaults; these tests hold the resulting contract together.
/// The skirmish play leg is install-backed and skipped in CI.
/// </summary>
public sealed class WorldBuilderRoundTripTests : MockedGameTest
{
    private const string PlacedTypeName = "AmericaTankCrusader";

    [Fact]
    public void NewMap_EditsUndoRedo_AndSaveReload()
    {
        var root = Path.Combine(Path.GetTempPath(), "panopticon-a5-02-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var mapPath = Path.Combine(root, "a5roundtrip.map");

            // Author: a new map, one height edit, one object placement (undo/redo both first).
            var map = NewMapFactory.Create(new NewMapParameters
            {
                PlayableWidth = 32,
                PlayableHeight = 32,
                InitialHeight = 10,
            });
            var elevations = map.MapFile.HeightMapData.Elevations!;
            var heightEdit = HeightMapEdit.Capture(
                map.MapFile.HeightMapData, 10, 10, 2, 2, m => m[10, 10] = 42, "raise");
            map.UndoStack.Push(heightEdit);
            var placed = new MapObject(new System.Numerics.Vector3(100, 0, 100), 0f, RoadType.None, PlacedTypeName);
            map.UndoStack.Push(ObjectPlacementEdit.CreatePlacement(map.MapFile.ObjectsList, placed));

            Assert.Equal(42, elevations[10, 10]);
            Assert.Contains(map.MapFile.ObjectsList.Objects, o => o.TypeName == PlacedTypeName);

            // Undo both: terrain and objects return to the factory state.
            map.UndoStack.Undo();
            map.UndoStack.Undo();
            Assert.NotEqual(42, elevations[10, 10]);
            Assert.DoesNotContain(map.MapFile.ObjectsList.Objects, o => o.TypeName == PlacedTypeName);

            // Redo both: the authored state is back.
            map.UndoStack.Redo();
            map.UndoStack.Redo();
            Assert.Equal(42, elevations[10, 10]);
            Assert.Contains(map.MapFile.ObjectsList.Objects, o => o.TypeName == PlacedTypeName);

            // Save and reload: both edits survive, the reopened map is clean.
            map.SaveAs(mapPath);
            Assert.False(map.IsDirty);
            var reopened = EditorMap.Open(MapFile.FromStream(File.OpenRead(mapPath)), mapPath);
            Assert.False(reopened.IsDirty);
            Assert.Equal(42, reopened.MapFile.HeightMapData.Elevations![10, 10]);
            var reloadedObject = Assert.Single(
                reopened.MapFile.ObjectsList.Objects, o => o.TypeName == PlacedTypeName);
            Assert.Equal(100, reloadedObject.Position.X);
            Assert.NotNull(reloadedObject.Properties);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void SavedNewMap_StartsInEngine()
    {
        var mapFolderName = "panopticon-a5-roundtrip";
        // Stage the saved map where the engine's user-data file system finds it; the game
        // wires that file system at construction only when the folder exists, so staging
        // happens before the Game is created.
        var userDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Command and Conquer Generals Data");
        var stagedDir = Path.Combine(userDataRoot, "maps", mapFolderName);
        var preExisting = Directory.Exists(stagedDir);
        Assert.False(preExisting, "staging folder must be fresh");
        Directory.CreateDirectory(stagedDir);
        try
        {
            var map = NewMapFactory.Create(new NewMapParameters
            {
                PlayableWidth = 32,
                PlayableHeight = 32,
                InitialHeight = 10,
            });
            map.SaveAs(Path.Combine(stagedDir, mapFolderName + ".map"));

            var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
            Platform.Start();
            var game = new Game(
                installation, null, new Configuration { NoAudio = true }, null);
            try
            {
                var playerSettings = new PlayerSetting[]
                {
                    new(0, "FactionAmerica", new ColorRgb(255, 0, 0), 0, PlayerOwner.EasyAi, "USA"),
                    new(1, "FactionGLA", new ColorRgb(0, 255, 0), 0, PlayerOwner.EasyAi, "GLA"),
                };
                game.StartSkirmishOrMultiPlayerGame(
                    $"maps/{mapFolderName}/{mapFolderName}.map",
                    new EchoConnection(),
                    playerSettings,
                    1,
                    isMultiPlayer: false);
                game.StartRun();

                Assert.NotNull(game.Scene3D);
                Assert.True(game.Scene3D.Players.Count >= 2, "skirmish sides booted");
            }
            finally
            {
                game.Dispose();
            }
        }
        finally
        {
            Directory.Delete(stagedDir, recursive: true);
        }
    }
}
