using System.Linq;
using System.Numerics;
using Panopticon.Logic;
using Panopticon.FX;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Tests.Data;
using Xunit;

namespace Panopticon.Tests.FX;

[Collection("SerialGameTests")]
public sealed class TerrainScorchExecutionTests
{
    [GameFact(SageGame.CncGenerals)]
    public void RandomScorches_UseOnlyClientRandomAndClearOnMapUnload()
    {
        Platform.Start();
        try
        {
            using var game = new Game(InstalledFilesTestData.GetInstallation(SageGame.CncGenerals), null,
                new Configuration { NoAudio = true, SkipStartingBuildings = true }, null);
            game.StartSkirmishOrMultiPlayerGame("maps/alpine assault/alpine assault.map", new EchoConnection(),
                [new PlayerSetting(0, "FactionAmerica", new ColorRgb(255, 0, 0), 0, PlayerOwner.EasyAi, "USA")], 1, false);
            var nugget = game.AssetStore.FXLists.SelectMany(list => list.Nuggets)
                .OfType<TerrainScorchFXNugget>().First(item => item.Type == TerrainScorchType.Random);
            var logic = Assert.IsType<SageRandom>(game.GameLogic.Random);
            var before = new uint[SageRandom.StateWordCount];
            logic.CopyState(before);
            var expected = new SageRandom();
            expected.Initialize(123);
            game.GameClient.Random.Initialize(123);
            var terrain = game.Scene3D.Terrain;
            terrain.Decals.Clear();
            for (var index = 0; index < 20; index++)
            {
                nugget.Execute(new FXListExecutionContext(Quaternion.Identity, new Vector3(100 + index * 200, 100, 0), game.GameEngine));
                Assert.Equal(expected.Next(0, 3), terrain.Decals.Decals[^1].Cell);
            }

            var after = new uint[SageRandom.StateWordCount];
            logic.CopyState(after);
            Assert.Equal(before, after);
            terrain.Update(game.Scene3D.Waters, default);
            var builds = terrain.DecalMeshBuildCount;
            terrain.Update(game.Scene3D.Waters, default);
            Assert.Equal(builds, terrain.DecalMeshBuildCount);
            terrain.SetMaxHeight(new Panopticon.Logic.Object.SphereCollider(
                new Transform(new Vector3(100, 100, 0), Quaternion.Identity), 5), 0);
            terrain.Update(game.Scene3D.Waters, default);
            Assert.Equal(builds + 1, terrain.DecalMeshBuildCount);
            terrain.SetMaxHeight(new Panopticon.Logic.Object.SphereCollider(
                new Transform(new Vector3(100, 900, 0), Quaternion.Identity), 5), 0);
            terrain.Update(game.Scene3D.Waters, default);
            Assert.Equal(builds + 1, terrain.DecalMeshBuildCount);
            terrain.Dispose();
            Assert.Empty(terrain.Decals.Decals);
        }
        finally
        {
            Platform.Stop();
        }
    }
}
