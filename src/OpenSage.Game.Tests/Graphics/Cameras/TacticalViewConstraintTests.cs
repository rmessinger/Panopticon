using System;
using System.Numerics;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Mathematics;
using OpenSage.Network;
using OpenSage.Tests;
using OpenSage.Tests.Data;
using Xunit;

namespace OpenSage.Tests.Graphics.Cameras;

// Contract: the tactical camera constraint (which pins the look-at point so the view stays
// inside the map) is recomputed from the current height on every LookAt. Tooling must set
// the height BEFORE LookAt — aiming while a far zoom's constraint is in effect clamps the
// target to the map centre, objects end up off-screen, and frustum culling then drops their
// meshes entirely (which mimics "units don't render" in headless captures).

[Collection("SerialGameTests")]
public class TacticalViewConstraintTests : IDisposable
{
    private Game? _game;

    private Game CreateGame()
    {
        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);

        Platform.Start();
        var game = new Game(installation, null, new Configuration { NoAudio = true, SkipStartingBuildings = true }, null);

        var playerSettings = new PlayerSetting[]
        {
            new(0, "FactionAmerica", new ColorRgb(255, 0, 0), 0, PlayerOwner.EasyAi, "USA"),
            new(1, "FactionChina", new ColorRgb(0, 255, 0), 0, PlayerOwner.EasyAi, "China"),
            new(2, "FactionGLA", new ColorRgb(0, 0, 255), 0, PlayerOwner.EasyAi, "GLA"),
        };
        game.StartSkirmishOrMultiPlayerGame("maps/alpine assault/alpine assault.map", new EchoConnection(), playerSettings, 1, isMultiPlayer: false);
        game.StartRun();
        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        foreach (var player in game.Scene3D.Players)
        {
            player.DisableAI();
        }

        game.EnableFixedLogicTimestep();
        return game;
    }

    [GameFact(SageGame.CncGenerals)]
    public void LookAtAfterHeightChange_LandsOnTarget()
    {
        using var game = _game = CreateGame();
        var scene = game.Scene3D;

        var tankDef = game.AssetStore.ObjectDefinitions.GetByName("ChinaTankBattleMaster")
            ?? throw new InvalidOperationException("tank not found");
        var tank = scene.GameObjects.CreateObject(tankDef, scene.Players[0]);
        var height = scene.Terrain.GetGroundHeight(new Vector2(660, 600)) ?? 0f;
        tank.UpdateTransform(new Vector3(660, 600, height), tank.Rotation);
        tank.UpdateColliders();

        for (var i = 0; i < 30; i++)
        {
            game.Update([]);
            game.Panel.EnsureFrame(new Rectangle(0, 0, 1024, 768));
            game.Render();
        }

        var view = scene.TacticalView;

        // Frame the whole map wide first: the constraint pins the look-at to the map centre
        // (visible ground exceeds the map at this height).
        view.InitHeightForMap();
        view.SetAngleAndPitchToDefault();
        view.SnapHeightAboveGround(3200f);
        view.LookAt(new Vector3(630, 600, 0));

        // Zoom in and aim again: with the constraint recomputed for the close height, the
        // look-at must land on the requested point, and an on-screen object must survive
        // frustum culling.
        view.SnapHeightAboveGround(150f);
        view.LookAt(new Vector3(630, 600, 0));

        game.Update([]);
        game.Panel.EnsureFrame(new Rectangle(0, 0, 1024, 768));
        game.Render();

        Assert.True(MathF.Abs(scene.Camera.Target.X - 630f) < 5f
            && MathF.Abs(scene.Camera.Target.Y - 600f) < 5f,
            $"Camera target {scene.Camera.Target} was clamped away from the requested look-at point");

        Assert.True(tank.RoughCollider.Intersects(scene.Camera.BoundingFrustum),
            "Tank at screen centre was frustum-culled after close-up LookAt");
    }

    public void Dispose()
    {
        _game?.Dispose();
        Platform.Stop();
    }
}
