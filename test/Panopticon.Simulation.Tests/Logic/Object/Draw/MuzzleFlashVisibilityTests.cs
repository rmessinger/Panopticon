using System;
using System.Collections.Generic;
using System.Numerics;
using Panopticon.Client;
using Panopticon.Graphics;
using Panopticon.Graphics.Cameras;
using Panopticon.Graphics.Rendering;
using Panopticon.Graphics.Shaders;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Tests.Data;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Logic.Object.Draw;

[Collection("SerialGameTests")]
public class MuzzleFlashVisibilityTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private Game? _game;

    public MuzzleFlashVisibilityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose()
    {
        _game?.Dispose();
    }

    private Game CreateGame()
    {
        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);

        Platform.Start();
        var game = new Game(installation, null, new Configuration { NoAudio = true, SkipStartingBuildings = true }, null);

        var playerSettings = new PlayerSetting[]
        {
            new(0, "FactionAmerica", new ColorRgb(255, 0, 0), 0, PlayerOwner.EasyAi, "USA"),
            new(1, "FactionChina", new ColorRgb(0, 255, 0), 0, PlayerOwner.EasyAi, "China"),
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

    private GameObject Spawn(string definition, Vector3 position, Player owner)
    {
        var def = _game!.AssetStore.ObjectDefinitions.GetByName(definition)
            ?? throw new InvalidOperationException($"{definition} not found");
        var obj = _game.Scene3D.GameObjects.CreateObject(def, owner);
        obj.UpdateTransform(position, null);
        return obj;
    }

    private static List<string> RenderedMuzzleItems(GameObject obj, Camera camera, ref long ticks)
    {
        var renderList = new RenderList();
        var delta = TimeSpan.FromMilliseconds(33).Ticks;
        ticks += delta;
        var drawable = (Drawable)obj.Client!;
        drawable.BuildObjectRenderList(renderList, camera, new TimeInterval(ticks, delta));
        return MuzzleItems(renderList);
    }

    private static List<string> RenderedMuzzleItems(ModelInstance instance, Camera camera)
    {
        var renderList = new RenderList();
        instance.Update(TimeInterval.Zero);
        instance.SetWorldMatrix(Matrix4x4.Identity);
        instance.BuildRenderList(renderList, camera, false, new MeshShaderResources.RenderItemConstantsPS { Opacity = 1.0f });
        return MuzzleItems(renderList);
    }

    private static List<string> MuzzleItems(RenderList renderList)
    {
        var names = new List<string>();
        foreach (var bucket in new[] { renderList.Opaque, renderList.Transparent })
        {
            for (var i = 0; i < bucket.RenderItems.Length; i++)
            {
                var name = bucket.RenderItems[i].DebugName;
                if (name != null && name.Contains("MUZZ", StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    [GameFact(SageGame.CncGenerals)]
    public void MuzzleFlash_RendersOnlyWhileFiring()
    {
        using var game = _game = CreateGame();
        var scene = game.Scene3D;
        var usa = scene.Players[0];
        var china = scene.Players[1];
        long ticks = 0;

        foreach (var attackerName in new[] { "AmericaVehicleHumvee", "AmericaInfantryRanger" })
        {
            var attacker = Spawn(attackerName, new Vector3(600, 600, 0), usa);
            var target = Spawn("CarAsian1", new Vector3(680, 600, 0), china);
            target.BodyModule.SetMaxHealth(100000f, MaxHealthChangeType.FullyHeal);

            var idleLeaks = 0;
            for (var frame = 0; frame < 60; frame++)
            {
                game.Update([]);
                var items = RenderedMuzzleItems(attacker, scene.Camera, ref ticks);
                if (items.Count > 0)
                {
                    idleLeaks++;
                    _output.WriteLine($"{attackerName} idle frame {frame}: {string.Join(",", items)} flags={attacker.ModelConditionFlags.DisplayName}");
                }
            }

            attacker.CurrentWeapon!.SetTarget(new WeaponTarget(scene.GameObjects, target.Id));
            var firingShown = 0;
            var nonFiringShown = 0;
            for (var frame = 0; frame < 150; frame++)
            {
                game.Update([]);
                var firing = attacker.ModelConditionFlags.Get(ModelConditionFlag.FiringA);
                var items = RenderedMuzzleItems(attacker, scene.Camera, ref ticks);
                if (items.Count > 0 && firing)
                {
                    firingShown++;
                }
                else if (items.Count > 0)
                {
                    nonFiringShown++;
                    _output.WriteLine($"{attackerName} attack frame {frame}: shown while not FiringA: {string.Join(",", items)} flags={attacker.ModelConditionFlags.DisplayName}");
                }
            }

            _output.WriteLine($"{attackerName}: idleLeaks={idleLeaks} firingShown={firingShown} nonFiringShown={nonFiringShown}");
            Assert.Equal(0, idleLeaks);
            Assert.Equal(0, nonFiringShown);
            Assert.True(firingShown > 0, $"{attackerName} never rendered its muzzle flash while firing");
            attacker.Destroy();
            target.Destroy();
        }
    }

    // Standalone renders (TestHarness captures, the diagnostics model view) have no draw module
    // or firing state, so muzzle flashes must stay hidden at every camera angle.
    [GameFact(SageGame.CncGenerals)]
    public void MuzzleFlash_HiddenInStandaloneModelRender()
    {
        using var game = _game = CreateGame();
        var camera = game.Scene3D.Camera;

        foreach (var modelName in new[] { "AVHUMMER", "AIRngr_SKN" })
        {
            var model = game.AssetStore.Models.GetByName(modelName)
                ?? throw new InvalidOperationException($"{modelName} not found");
            using var instance = model.CreateInstance(game.AssetStore.LoadContext);

            Assert.NotEmpty(RenderedMuzzleItems(instance, camera));

            MuzzleFlashBones.HideForStandaloneRender(instance, game.AssetStore.ObjectDefinitions);

            Assert.Empty(RenderedMuzzleItems(instance, camera));
        }
    }
}
