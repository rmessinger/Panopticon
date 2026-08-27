using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;
using OpenSage.Mathematics;
using OpenSage.Network;
using OpenSage.Tests;
using OpenSage.Tests.Data;
using Xunit;
using Xunit.Abstractions;

namespace OpenSage.Tests.Logic.Object.Update;

// MissileAIUpdate flight: the Missile Defender's MissileDefenderMissile (PhysicsBehavior +
// MissileAIUpdate + THRUST locomotor) must ignite, fly toward the target, and deliver its
// warhead. Regression guard for the "missile spawns and falls straight through the ground"
// bug, where nothing drove the thrust locomotor so gravity was unopposed.

[Collection("SerialGameTests")]
public class MissileAIUpdateFlightTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private Game? _game;

    public MissileAIUpdateFlightTests(ITestOutputHelper output)
    {
        _output = output;
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

    private GameObject Spawn(string definition, Vector3 position, Player owner)
    {
        var def = _game!.AssetStore.ObjectDefinitions.GetByName(definition)
            ?? throw new InvalidOperationException($"{definition} not found");
        var obj = _game.Scene3D.GameObjects.CreateObject(def, owner);
        obj.UpdateTransform(position, null);
        return obj;
    }

    private void Step(int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            _game!.Update([]);
        }
    }

    private List<GameObject> LiveMissiles()
    {
        return _game!.Scene3D.GameObjects.Objects
            .Where(o => o.Definition.Name == "MissileDefenderMissile" && !o.IsEffectivelyDead)
            .ToList();
    }

    [GameFact(SageGame.CncGenerals)]
    public void MissileDefenderMissile_HomesOntoTarget_AndDeliversWarhead()
    {
        using var game = _game = CreateGame();
        var scene = game.Scene3D;
        var usa = scene.Players[2];
        var china = scene.Players[0];

        var defender = Spawn("AmericaInfantryMissileDefender", new Vector3(600, 600, 0), usa);
        var tank = Spawn("ChinaTankBattleMaster", new Vector3(720, 600, 0), china);
        Step(30);

        Assert.NotNull(defender.CurrentWeapon);
        defender.CurrentWeapon!.SetTarget(new WeaponTarget(scene.GameObjects, tank.Id));

        // Track every live missile's horizontal distance to the target each frame.
        var sawMissile = false;
        var minHorizontalDistance = float.MaxValue;
        for (var i = 0; i < 300; i++)
        {
            Step(1);
            var targetPos = tank.IsEffectivelyDead ? new Vector3(720, 600, 0) : tank.Translation;
            foreach (var missile in LiveMissiles())
            {
                sawMissile = true;
                var dx = missile.Translation.X - targetPos.X;
                var dy = missile.Translation.Y - targetPos.Y;
                minHorizontalDistance = MathF.Min(minHorizontalDistance, MathF.Sqrt(dx * dx + dy * dy));
            }
        }

        Assert.True(sawMissile, "No MissileDefenderMissile was ever spawned");

        // The pre-fix bug: the missile dropped vertically at the muzzle, never closing the
        // (initially 120-unit) horizontal gap. Homing must bring it near the target.
        Assert.True(minHorizontalDistance < 30,
            $"Missile never closed on the target: min horizontal distance {minHorizontalDistance}");

        Assert.True(tank.BodyModule.Health < tank.BodyModule.MaxHealth,
            "Missile warhead never damaged the target");
    }

    public void Dispose()
    {
        _game?.Dispose();
        Platform.Stop();
    }
}
