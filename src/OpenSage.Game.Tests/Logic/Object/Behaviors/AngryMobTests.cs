using System;
using System.Linq;
using System.Numerics;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Mathematics;
using OpenSage.Network;
using OpenSage.Tests;
using OpenSage.Tests.Data;
using Xunit;

namespace OpenSage.Tests.Logic.Object.Behaviors;

// Angry Mob chain: the nexus (harmless controller weapon, ImmortalBody) spawns its member mix via
// SpawnBehavior (InitialBurst immediately, top-up on the SpawnReplaceDelay cadence), and
// MobMemberSlavedUpdate propagates the nexus's weapon target onto the members, whose weapons are
// the mob's damage output. Closes the validation-ground "horde composite" gap.

[Collection("SerialGameTests")]
public class AngryMobTests : IDisposable
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

    private int MobMemberCount(GameObject nexus)
    {
        return _game!.Scene3D.GameObjects.Objects.Count(o =>
            o.Definition.Name.StartsWith("GLAInfantryAngryMob", StringComparison.Ordinal)
            && o.Definition.Name != "GLAInfantryAngryMobNexus"
            && !o.IsEffectivelyDead
            && o.CreatedByObjectID == nexus.Id);
    }

    [GameFact(SageGame.CncGenerals)]
    public void Nexus_SpawnsInitialBurstMembers_AroundItself()
    {
        using var game = _game = CreateGame();
        var gla = game.Scene3D.Players[4];

        var nexus = Spawn("GLAInfantryAngryMobNexus", new Vector3(600, 600, 0), gla);

        Assert.NotNull(nexus.FindBehavior<SpawnBehavior>());
        Step(5);

        // InitialBurst = 5 of SpawnNumber = 10 appear immediately (no 30s trickle wait).
        Assert.True(MobMemberCount(nexus) >= 5,
            $"nexus spawned {MobMemberCount(nexus)} members, expected at least the InitialBurst of 5");
    }

    [GameFact(SageGame.CncGenerals)]
    public void NexusTarget_MembersDestroyIt()
    {
        using var game = _game = CreateGame();
        var scene = game.Scene3D;
        var gla = scene.Players[4];
        var civilian = scene.Players[1];

        var nexus = Spawn("GLAInfantryAngryMobNexus", new Vector3(600, 600, 0), gla);
        var car = Spawn("CarAsian1", new Vector3(600, 690, 0), civilian);
        car.BodyModule.SetMaxHealth(30);
        Step(30);

        Assert.True(MobMemberCount(nexus) >= 5);

        // The validation-ground attacker path: the (harmless) nexus weapon is commanded directly;
        // members receive the target through MobMemberSlavedUpdate.
        nexus.CurrentWeapon!.SetTarget(new WeaponTarget(scene.GameObjects, car.Id));

        Step(400);

        Assert.True(car.IsEffectivelyDead,
            $"mob members did not destroy the nexus's target (hp={car.BodyModule?.Health}, " +
            $"members={MobMemberCount(nexus)})");
    }

    public void Dispose()
    {
        _game?.Dispose();
        Platform.Stop();
    }
}
