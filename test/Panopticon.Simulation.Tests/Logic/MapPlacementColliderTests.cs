using System;
using System.Linq;
using System.Numerics;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Tests.Data;
using Xunit;
using SageGame = Panopticon.SageGame;

namespace Panopticon.Tests.Logic;

// Both Game-creating probe tests share one collection: xUnit runs collections serially, avoiding
// the static TranslationManager's duplicate-key failure when two Game instances exist at once.
[Collection("SerialGameTests")]
public class MapPlacementColliderTests
{
    // Regression for the paused-start invisible-buildings bug: map-placed objects' colliders are
    // created against the identity transform and were only refreshed on the object's first logic
    // tick. A game that renders before ticking logic (the validation ground's hold-orders start)
    // frustum-culled every map object off-camera (Scene3D.BuildRenderList culls on RoughCollider),
    // hiding the map's buildings until the player unpaused.
    [GameFact(SageGame.CncGenerals)]
    public void MapObjects_HaveCollidersAtTheirTranslation_BeforeFirstLogicTick()
    {
        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        using var game = new Game(installation, null, new Configuration { NoAudio = true, SkipStartingBuildings = true }, null);

        var playerSettings = new PlayerSetting[]
        {
            new(0, "FactionAmerica", new ColorRgb(255, 0, 0), 0, PlayerOwner.EasyAi, "USA"),
            new(1, "FactionChina", new ColorRgb(0, 255, 0), 0, PlayerOwner.EasyAi, "China"),
            new(2, "FactionGLA", new ColorRgb(0, 0, 255), 0, PlayerOwner.EasyAi, "GLA"),
        };
        game.StartSkirmishOrMultiPlayerGame("maps/alpine assault/alpine assault.map", new EchoConnection(), playerSettings, 1, isMultiPlayer: false);
        game.StartRun();
        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        game.IsLogicRunning = false;

        var checkedCount = 0;
        foreach (var obj in game.Scene3D.GameObjects.Objects)
        {
            var collider = obj.RoughCollider;
            if (collider == null)
            {
                continue;
            }

            var bounds = collider.WorldBounds;
            var distance = Vector3.Distance(bounds.Center, obj.Translation);
            Assert.True(distance <= bounds.Radius + 1f,
                $"{obj.Definition.Name} at ({obj.Translation.X:F0},{obj.Translation.Y:F0}) has a collider centred at " +
                $"({bounds.Center.X:F0},{bounds.Center.Y:F0}) radius {bounds.Radius:F0} before the first logic tick");
            checkedCount++;
        }

        // The map must actually exercise the assertion.
        Assert.True(checkedCount > 100, $"only {checkedCount} objects checked");
    }
}
