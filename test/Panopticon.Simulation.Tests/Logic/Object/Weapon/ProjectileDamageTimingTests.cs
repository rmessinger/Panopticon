using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Tests;
using Panopticon.Tests.Data;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Logic.Object.Weapon;

// Projectile weapon damage timing: the warhead (and its damage FX) must be delivered when
// the projectile reaches its target, not when the launcher fires. Regression guard for the
// bug where FiringWeaponState executed the launcher's DamageNuggets at fire time, so the
// impact effect played at the muzzle the moment the missile was launched.

[Collection("SerialGameTests")]
public class ProjectileDamageTimingTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private Game? _game;

    public ProjectileDamageTimingTests(ITestOutputHelper output)
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

    [GameFact(SageGame.CncGenerals)]
    public void TomahawkImpact_DamagesNearbyInfantry()
    {
        using var game = _game = CreateGame();
        var scene = game.Scene3D;
        var launcher = Spawn("AmericaVehicleTomahawk", new Vector3(600, 600, 0), scene.Players[2]);
        var radius = launcher.CurrentWeapon!.Template.Nuggets.OfType<DamageNugget>().Max(nugget => nugget.Radius);
        Assert.True(radius > 0);
        var target = Spawn("AmericaInfantryRanger", new Vector3(900, 600, 0), scene.Players[3]);
        var nearby = Spawn("AmericaInfantryRanger", new Vector3(900, 600 + radius / 2f, 0), scene.Players[3]);
        foreach (var infantry in new[] { target, nearby })
        {
            infantry.BodyModule.SetMaxHealth(10000f, MaxHealthChangeType.FullyHeal);
        }

        for (var frame = 0; frame < 30; frame++)
        {
            game.Update([]);
        }

        launcher.CurrentWeapon.SetTarget(new WeaponTarget(scene.GameObjects, target.Id));
        var sawProjectile = false;
        for (var frame = 0; frame < 600 && target.BodyModule.Health == target.BodyModule.MaxHealth; frame++)
        {
            game.Update([]);
            foreach (var projectile in scene.GameObjects.Objects.Where(unit => unit.IsProjectile && unit.CreatedByObjectID == launcher.Id))
            {
                sawProjectile = true;
                Assert.Same(launcher.CurrentWeapon.Template.RadiusDamageAffects, projectile.CurrentWeapon!.Template.RadiusDamageAffects);
            }
        }

        _output.WriteLine($"Tomahawk radius {radius}: target HP {target.BodyModule.Health}, nearby HP {nearby.BodyModule.Health}");
        Assert.True(sawProjectile);
        Assert.True(target.BodyModule.Health < target.BodyModule.MaxHealth, "Tomahawk never damaged the targeted infantry.");
        Assert.Equal(150f, target.BodyModule.MaxHealth - target.BodyModule.Health);
        Assert.True(nearby.BodyModule.Health < nearby.BodyModule.MaxHealth, "Tomahawk splash never damaged nearby infantry.");
    }

    [GameFact(SageGame.CncGenerals)]
    public void TomahawkGroundBlast_UsesDamageRingsAndRelationshipFilter()
    {
        using var game = _game = CreateGame();
        var scene = game.Scene3D;
        var launcher = Spawn("AmericaVehicleTomahawk", new Vector3(600, 600, 0), scene.Players[2]);
        var center = Spawn("AmericaInfantryRanger", new Vector3(900, 600, 0), scene.Players[3]);
        var outer = Spawn("AmericaInfantryRanger", new Vector3(924, 600, 0), scene.Players[3]);
        var outside = Spawn("AmericaInfantryRanger", new Vector3(1000, 600, 0), scene.Players[3]);
        var ally = Spawn("AmericaInfantryRanger", new Vector3(900, 624, 0), scene.Players[2]);
        var infantry = new[] { center, outer, outside, ally };
        launcher.Owner.Allies.Remove(center.Owner);
        center.Owner.Allies.Remove(launcher.Owner);
        launcher.Owner.AddEnemy(center.Owner);
        foreach (var unit in infantry)
        {
            unit.BodyModule.SetMaxHealth(10000f, MaxHealthChangeType.FullyHeal);
        }

        for (var frame = 0; frame < 30; frame++)
        {
            game.Update([]);
        }

        var weapon = launcher.CurrentWeapon!;
        weapon.SetTarget(new WeaponTarget(center.Translation));
        var context = new WeaponEffectExecutionContext(weapon, game.GameEngine);
        var nuggets = weapon.Template.Nuggets.OfType<DamageNugget>().ToArray();
        foreach (var nugget in nuggets)
        {
            nugget.Execute(context);
        }

        Assert.Equal(150f, center.BodyModule.MaxHealth - center.BodyModule.Health);
        Assert.Equal(50f, outer.BodyModule.MaxHealth - outer.BodyModule.Health);
        Assert.Equal(50f, ally.BodyModule.MaxHealth - ally.BodyModule.Health);
        Assert.Equal(outside.BodyModule.MaxHealth, outside.BodyModule.Health);

        foreach (var unit in infantry)
        {
            unit.BodyModule.SetMaxHealth(10000f, MaxHealthChangeType.FullyHeal);
        }

        weapon.Template.RadiusDamageAffects = new ObjectFilter();
        weapon.Template.RadiusDamageAffects.Rules.Set(ObjectFilterRule.Enemies, true);
        foreach (var nugget in nuggets)
        {
            nugget.Execute(context);
        }

        Assert.Equal(150f, center.BodyModule.MaxHealth - center.BodyModule.Health);
        Assert.Equal(50f, outer.BodyModule.MaxHealth - outer.BodyModule.Health);
        Assert.Equal(ally.BodyModule.MaxHealth, ally.BodyModule.Health);
        Assert.Equal(outside.BodyModule.MaxHealth, outside.BodyModule.Health);
    }

    [GameFact(SageGame.CncGenerals)]
    public void KilledMovingInfantry_ClearsMovementAndCombatState()
    {
        using var game = _game = CreateGame();
        var scene = game.Scene3D;
        var infantry = Spawn("AmericaInfantryRanger", new Vector3(600, 600, 0), scene.Players[2]);
        var enemy = Spawn("AmericaInfantryRanger", new Vector3(1000, 600, 0), scene.Players[3]);
        infantry.CurrentWeapon!.SetTarget(new WeaponTarget(scene.GameObjects, enemy.Id));
        infantry.CurrentWeapon.Fire();
        var firingFlag = ModelConditionFlagUtility.GetFiringFlag(infantry.CurrentWeapon.WeaponIndex);
        infantry.AIUpdate!.SetTargetPoint(enemy.Translation);
        Assert.True(infantry.ModelConditionFlags.Get(ModelConditionFlag.Moving));
        Assert.True(infantry.ModelConditionFlags.Get(firingFlag));

        infantry.Kill(DamageType.Explosion, DeathType.Exploded);

        Assert.True(infantry.IsEffectivelyDead);
        Assert.False(infantry.ModelConditionFlags.Get(ModelConditionFlag.Moving));
        Assert.Null(infantry.CurrentWeapon.CurrentTarget);
        Assert.False(infantry.ModelConditionFlags.Get(ModelConditionFlag.Attacking));
        Assert.False(infantry.ModelConditionFlags.Get(firingFlag));
    }

    [GameFact(SageGame.CncGenerals)]
    public void ProjectileWeapons_DeliverWarheads_OnEveryDetonationPath()
    {
        using var game = _game = CreateGame();
        var scene = game.Scene3D;
        var china = scene.Players[0];
        var civilian = scene.Players[1];

        // (attacker, first-kill frame budget). DragonTankFlameProjectile exhausts its fuel
        // mid-flight (DetonateOnNoFuel must deliver the warhead, not just kill the missile);
        // NukeCannonShell is a Bezier projectile whose kinematic Speed is per logic frame.
        foreach (var (attackerName, budget) in new[] { ("ChinaTankDragon", 60), ("ChinaVehicleNukeLauncher", 300) })
        {
            var attacker = Spawn(attackerName, new Vector3(600, 600, 0), china);
            // The nuke cannon's NukeCannonGun has a 200-unit minimum attack range (dead zone);
            // the car must sit inside the 200..350 window or the weapon holds fire.
            var carDistance = attackerName == "ChinaVehicleNukeLauncher" ? 260 : 80;
            var car = Spawn("CarAsian1", new Vector3(600 + carDistance, 600, 0), civilian);
            car.BodyModule.SetMaxHealth(30f, MaxHealthChangeType.FullyHeal);
            for (var i = 0; i < 30; i++)
            {
                game.Update([]);
            }

            attacker.CurrentWeapon!.SetTarget(new WeaponTarget(scene.GameObjects, car.Id));

            var destroyedFrame = -1;
            for (var frame = 0; frame < budget; frame++)
            {
                game.Update([]);
                if (car.IsEffectivelyDead)
                {
                    destroyedFrame = frame;
                    break;
                }
            }

            Assert.True(destroyedFrame != -1,
                $"{attackerName} never destroyed its target within {budget} frames (warhead not delivered)");
            _output.WriteLine($"{attackerName}: target destroyed at frame {destroyedFrame}");

            // Clean up so the next attacker's trace is isolated.
            attacker.Destroy();
        }
    }

    [GameFact(SageGame.CncGenerals)]
    public void MissileDamage_LandsOnArrival_NotOnLaunch()
    {
        using var game = _game = CreateGame();
        var scene = game.Scene3D;
        var usa = scene.Players[2];
        var china = scene.Players[0];

        var defender = Spawn("AmericaInfantryMissileDefender", new Vector3(600, 600, 0), usa);
        var tank = Spawn("ChinaTankBattleMaster", new Vector3(720, 600, 0), china);
        for (var i = 0; i < 30; i++)
        {
            game.Update([]);
        }

        defender.CurrentWeapon!.SetTarget(new WeaponTarget(scene.GameObjects, tank.Id));

        var maxHealth = tank.BodyModule.MaxHealth;
        var firstDamageFrame = -1;
        var firstMissileFrame = -1;
        // Last frame a live missile existed, and its closest distance to the target.
        var lastMissileFrame = -1;
        var lastMissileDistance = float.MaxValue;

        for (var frame = 0; frame < 300; frame++)
        {
            game.Update([]);

            var missiles = scene.GameObjects.Objects
                .Where(o => o.Definition.Name == "MissileDefenderMissile" && !o.IsEffectivelyDead)
                .ToList();

            if (missiles.Count > 0)
            {
                if (firstMissileFrame == -1)
                {
                    firstMissileFrame = frame;
                }

                lastMissileFrame = frame;
                lastMissileDistance = MathF.Min(lastMissileDistance,
                    missiles.Min(m => Vector3.Distance(m.Translation, tank.Translation)));
            }

            if (firstDamageFrame == -1 && tank.BodyModule.Health < maxHealth)
            {
                firstDamageFrame = frame;
                break;
            }
        }

        Assert.True(firstMissileFrame != -1, "No missile was ever spawned");
        Assert.True(firstDamageFrame != -1, "Missile never damaged the target");

        _output.WriteLine($"missile frames {firstMissileFrame}-{lastMissileFrame}, last distance {lastMissileDistance}, damage frame {firstDamageFrame}");

        // Damage must not land in the same frame the missile is launched far from the target.
        Assert.True(firstDamageFrame > firstMissileFrame,
            $"Damage landed on frame {firstDamageFrame}, the same frame the missile was launched (frame {firstMissileFrame})");

        // Damage coincides with detonation: the missile must have been alive until the frame
        // before damage, and it must have reached the target when it disappeared.
        Assert.True(firstDamageFrame - lastMissileFrame <= 2,
            $"Damage landed on frame {firstDamageFrame}, but the missile vanished on frame {lastMissileFrame}");

        Assert.True(lastMissileDistance < 60,
            $"Missile vanished while still {lastMissileDistance} units from the target, yet damage landed later");
    }

    [GameFact(SageGame.CncGenerals)]
    public void NukeCannonShell_FacesFrameDisplacement_ThroughArc()
    {
        using var game = _game = CreateGame();
        var scene = game.Scene3D;
        var china = scene.Players[0];
        var civilian = scene.Players[1];

        var launcher = Spawn("ChinaVehicleNukeLauncher", new Vector3(520, 600, 0), china);
        var target = Spawn("CarAsian1", new Vector3(760, 600, 0), civilian);
        target.BodyModule.SetMaxHealth(100000f, MaxHealthChangeType.FullyHeal);
        for (var i = 0; i < 30; i++)
        {
            game.Update([]);
        }

        launcher.CurrentWeapon!.SetTarget(new WeaponTarget(scene.GameObjects, target.Id));

        var previousPositions = new Dictionary<ObjectId, Vector3>();
        var minimumAlignment = 1.0f;
        var orientationSamples = 0;
        var pivotSamples = 0;
        var sawClimb = false;
        var sawDescent = false;
        for (var frame = 0; frame < 360; frame++)
        {
            game.Update([]);

            foreach (var shell in scene.GameObjects.Objects.Where(
                o => o.Definition.Name == "NukeCannonShell" && !o.IsEffectivelyDead))
            {
                ProjectileVisualAssertions.AssertLeadingEdgePivot(shell);
                pivotSamples++;
                if (previousPositions.TryGetValue(shell.Id, out var previousPosition))
                {
                    var displacement = shell.Translation - previousPosition;
                    if (displacement.LengthSquared() > 1e-6f)
                    {
                        var travelDirection = Vector3.Normalize(displacement);
                        var forward = Vector3.Normalize(shell.TransformMatrix.GetXVector());
                        minimumAlignment = MathF.Min(
                            minimumAlignment,
                            Vector3.Dot(forward, travelDirection));
                        orientationSamples++;
                        sawClimb |= travelDirection.Z > 0.05f;
                        sawDescent |= travelDirection.Z < -0.05f;
                    }
                }

                previousPositions[shell.Id] = shell.Translation;
            }
        }

        Assert.True(orientationSamples > 0, "No moving NukeCannonShell orientation was sampled");
        Assert.True(pivotSamples > 0, "No NukeCannonShell model pivot was sampled");
        Assert.True(sawClimb, "NukeCannonShell never climbed through its arc");
        Assert.True(sawDescent, "NukeCannonShell never descended through its arc");
        Assert.True(minimumAlignment > 0.999f,
            $"Shell forward axis diverged from travel direction: minimum dot {minimumAlignment}");
    }

    [GameFact(SageGame.CncGenerals)]
    public void TumblingProjectile_DoesNotOverrideLaunchOrientation()
    {
        using var game = _game = CreateGame();
        var scene = game.Scene3D;
        var gla = scene.Players[1];

        var rock = Spawn(
            "GLAAngryMobRockProjectileObject",
            new Vector3(560, 600, 40),
            gla);
        var launchRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.6f);
        rock.SetRotation(launchRotation);
        rock.SetWarhead(new WeaponTemplate());
        rock.CurrentWeapon!.SetTarget(new WeaponTarget(new Vector3(760, 600, 40)));
        rock.Speed = 4.0f;

        var projectileBehavior = Assert.IsType<BezierProjectileBehavior>(
            rock.FindBehavior<BezierProjectileBehavior>());
        Assert.False(projectileBehavior.UsesFlightPathOrientation);

        var initialForward = Vector3.Normalize(rock.TransformMatrix.GetXVector());
        for (var frame = 0; frame < 10; frame++)
        {
            game.Update([]);
        }

        Assert.True(Vector3.Distance(rock.Translation, new Vector3(560, 600, 40)) > 1.0f);
        var currentForward = Vector3.Normalize(rock.TransformMatrix.GetXVector());
        Assert.InRange(Vector3.Dot(initialForward, currentForward), 0.99999f, 1.00001f);
    }

    public void Dispose()
    {
        _game?.Dispose();
        Platform.Stop();
    }
}
