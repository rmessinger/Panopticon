using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Tests;
using Panopticon.Tests.Data;
using Panopticon.Tests.Logic.Object.Weapon;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Logic.Object.Update;

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
        var previousPositions = new Dictionary<ObjectId, Vector3>();
        var minimumAlignment = 1.0f;
        var orientationSamples = 0;
        var pivotSamples = 0;
        for (var i = 0; i < 300; i++)
        {
            Step(1);
            var targetPos = tank.IsEffectivelyDead ? new Vector3(720, 600, 0) : tank.Translation;
            foreach (var missile in LiveMissiles())
            {
                sawMissile = true;
                ProjectileVisualAssertions.AssertLeadingEdgePivot(missile);
                pivotSamples++;
                var dx = missile.Translation.X - targetPos.X;
                var dy = missile.Translation.Y - targetPos.Y;
                minHorizontalDistance = MathF.Min(minHorizontalDistance, MathF.Sqrt(dx * dx + dy * dy));

                if (previousPositions.TryGetValue(missile.Id, out var previousPosition))
                {
                    var displacement = missile.Translation - previousPosition;
                    if (displacement.LengthSquared() > 1e-6f)
                    {
                        var forward = Vector3.Normalize(missile.TransformMatrix.GetXVector());
                        var travelDirection = Vector3.Normalize(displacement);
                        minimumAlignment = MathF.Min(
                            minimumAlignment,
                            Vector3.Dot(forward, travelDirection));
                        orientationSamples++;
                    }
                }

                previousPositions[missile.Id] = missile.Translation;
            }
        }

        Assert.True(sawMissile, "No MissileDefenderMissile was ever spawned");

        // The pre-fix bug: the missile dropped vertically at the muzzle, never closing the
        // (initially 120-unit) horizontal gap. Homing must bring it near the target.
        Assert.True(minHorizontalDistance < 30,
            $"Missile never closed on the target: min horizontal distance {minHorizontalDistance}");

        Assert.True(orientationSamples > 0, "No moving missile orientation was sampled");
        Assert.True(pivotSamples > 0, "No missile model pivot was sampled");
        Assert.True(minimumAlignment > 0.95f,
            $"Missile forward axis diverged from travel direction: minimum dot {minimumAlignment}");

        Assert.True(tank.BodyModule.Health < tank.BodyModule.MaxHealth,
            "Missile warhead never damaged the target");
    }

    [GameFact(SageGame.CncGenerals)]
    public void MissileWithoutFuel_FacesBallisticTravelDirection()
    {
        using var game = _game = CreateGame();
        var usa = game.Scene3D.Players[2];

        var missile = Spawn("HumveeMissile", new Vector3(600, 600, 120), usa);
        missile.SetWarhead(new WeaponTemplate());
        missile.CurrentWeapon!.SetTarget(new WeaponTarget(new Vector3(1400, 600, 120)));

        var previousPosition = missile.Translation;
        var minimumAlignment = 1.0f;
        var coastSamples = 0;
        for (var frame = 0; frame < 120 && !missile.IsEffectivelyDead; frame++)
        {
            Step(1);

            var displacement = missile.Translation - previousPosition;
            previousPosition = missile.Translation;
            if (frame < 35
                || missile.Physics is not { Velocity: { Z: < -0.05f } }
                || displacement.LengthSquared() <= 1e-6f)
            {
                continue;
            }

            var forward = Vector3.Normalize(missile.TransformMatrix.GetXVector());
            var travelDirection = Vector3.Normalize(displacement);
            minimumAlignment = MathF.Min(minimumAlignment, Vector3.Dot(forward, travelDirection));
            coastSamples++;
        }

        Assert.True(coastSamples > 0, "Missile never entered a descending ballistic coast");
        Assert.True(minimumAlignment > 0.999f,
            $"Ballistic missile forward axis diverged from travel direction: minimum dot {minimumAlignment}");
    }

    public void Dispose()
    {
        _game?.Dispose();
        Platform.Stop();
    }
}
