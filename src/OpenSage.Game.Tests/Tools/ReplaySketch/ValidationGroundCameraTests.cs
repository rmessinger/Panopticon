using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenSage.Data;
using OpenSage.Graphics;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Mathematics;
using OpenSage.Network;
using OpenSage.Tests.Data;
using OpenSage.Tools.ReplaySketch.Services;
using Xunit;
using SageGame = OpenSage.SageGame;

namespace OpenSage.Tests.Tools.ReplaySketch;

[Collection("SerialGameTests")]
public class ValidationGroundCameraTests
{
    private static CapturedFrame BuildFrameAndRender(out List<Vector2> framed, out Game game, out GameInstallation installation)
    {
        installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        game = new Game(installation, null, new Configuration { NoAudio = true, SkipStartingBuildings = true }, null);

        var playerSettings = new PlayerSetting[]
        {
            new(0, "FactionAmerica", new ColorRgb(255, 0, 0), 0, PlayerOwner.EasyAi, "USA"),
            new(1, "FactionChina", new ColorRgb(0, 255, 0), 0, PlayerOwner.EasyAi, "China"),
            new(2, "FactionGLA", new ColorRgb(0, 0, 255), 0, PlayerOwner.EasyAi, "GLA"),
        };
        game.StartSkirmishOrMultiPlayerGame("maps/alpine assault/alpine assault.map", new EchoConnection(), playerSettings, 1, isMultiPlayer: false);
        game.StartRun();
        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        foreach (var idx in new[] { 2, 3, 4 })
        {
            game.Scene3D.Players[idx].DisableAI();
        }

        // The checked-in standard scenario, the same one the TestHarness runs.
        var scenario = ValidationGroundScenarioStore.Standard();
        var map = MapMetadataService.Load(installation, scenario.MapPath);
        var plan = ValidationGroundBuilder.Build(scenario, map);

        var civilian = game.Scene3D.Players[1];
        var g = game;
        var scene3D = game.Scene3D;
        GameObject SpawnAt(string definitionName, Vector3 planarPosition, Player owner)
        {
            var definition = g.AssetStore.ObjectDefinitions.GetByName(definitionName);
            var obj = scene3D.GameObjects.CreateObject(definition, owner);
            var height = scene3D.Terrain.GetGroundHeight(new Vector2(planarPosition.X, planarPosition.Y)) ?? 0f;
            obj.UpdateTransform(new Vector3(planarPosition.X, planarPosition.Y, height), obj.Rotation);
            obj.UpdateColliders();
            return obj;
        }

        foreach (var t in plan.TargetPlacements)
        {
            SpawnAt(t.DefinitionName, t.Position, civilian);
        }

        foreach (var s in plan.SupplySourcePlacements)
        {
            SpawnAt(s.DefinitionName, s.Position, civilian);
        }

        foreach (var u in plan.UnitPlacements)
        {
            SpawnAt(u.DefinitionName, u.Position, game.Scene3D.Players[u.PlayerIndex]);
        }

        framed = new List<Vector2>(plan.UnitPlacements.Count + plan.TargetPlacements.Count + plan.SupplySourcePlacements.Count);
        foreach (var p in plan.UnitPlacements)
        {
            framed.Add(new Vector2(p.Position.X, p.Position.Y));
        }

        foreach (var p in plan.TargetPlacements)
        {
            framed.Add(new Vector2(p.Position.X, p.Position.Y));
        }

        foreach (var p in plan.SupplySourcePlacements)
        {
            framed.Add(new Vector2(p.Position.X, p.Position.Y));
        }

        // The validation ground starts paused with the orders held; the camera frames the whole
        // formation before the first logic tick.
        game.IsLogicRunning = false;
        for (var i = 0; i < 30; i++)
        {
            game.Update(Array.Empty<Input.InputMessage>());
            game.Panel.EnsureFrame(new Rectangle(0, 0, 1280, 800));
            game.Render();
        }

        ValidationGroundCamera.FrameFormation(game.Scene3D, framed);

        game.Update(Array.Empty<Input.InputMessage>());
        game.Panel.EnsureFrame(new Rectangle(0, 0, 1280, 800));
        game.Render();
        return FrameCapturer.Capture(game.GraphicsDevice, game.Panel.Framebuffer.ColorTargets[0].Target);
    }

    [GameFact(SageGame.CncGenerals)]
    public void FrameFormation_AllPlacementsVisibleOnScreen()
    {
        var frame = BuildFrameAndRender(out var framed, out var game, out var installation);
        try
        {
            var viewProjection = game.Scene3D.Camera.ViewProjection;
            foreach (var p in framed)
            {
                var v = Vector4.Transform(new Vector4(p.X, p.Y, 0f, 1f), viewProjection);
                Assert.True(v.W > 0f && MathF.Abs(v.X / v.W) <= 0.95f && MathF.Abs(v.Y / v.W) <= 0.95f,
                    $"placement ({p.X:F0},{p.Y:F0}) projects to NDC " +
                    $"({(v.W > 0 ? v.X / v.W : float.PositiveInfinity):F2},{(v.W > 0 ? v.Y / v.W : float.PositiveInfinity):F2})");
            }

            // The world must actually render: at the framed height the upper 60% of the frame is
            // terrain, not clear color (the engine's clear color is 105,105,105; a terrain-void
            // regression at mid camera heights shows through as exactly that).
            var clearColored = 0;
            var sampled = 0;
            var px = frame.PixelData;
            for (var y = 40; y < frame.Height * 0.6; y += 5)
            {
                for (var x = 40; x < frame.Width - 40; x += 5)
                {
                    var i = (y * frame.Width + x) * 4;
                    // BGRA
                    var r = px[i + 2];
                    var g = px[i + 1];
                    var b = px[i];
                    sampled++;
                    if (MathF.Abs(r - 105) < 6 && MathF.Abs(g - 105) < 6 && MathF.Abs(b - 105) < 6)
                    {
                        clearColored++;
                    }
                }
            }

            Assert.True(clearColored < sampled * 0.05f,
                $"{clearColored}/{sampled} upper-frame pixels are clear color: the terrain did not render at the framed camera height");

            // Screenshot support for visual verification (VG_CAMERA_SHOT=/path/to/output.rgba).
            var shotPath = Environment.GetEnvironmentVariable("VG_CAMERA_SHOT");
            if (!string.IsNullOrEmpty(shotPath))
            {
                System.IO.File.WriteAllBytes(shotPath, frame.PixelData);
                Console.WriteLine($"VG camera screenshot data: {shotPath} ({frame.Width}x{frame.Height} {frame.Format})");
            }
        }
        finally
        {
            game.Dispose();
        }
    }
}
