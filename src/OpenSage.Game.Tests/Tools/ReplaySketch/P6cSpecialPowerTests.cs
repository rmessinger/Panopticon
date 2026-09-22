using System;
using System.Linq;
using System.Numerics;
using OpenSage.Data;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;
using OpenSage.Mathematics;
using OpenSage.Mods.Generals;
using OpenSage.Network;
using OpenSage.Tests;
using OpenSage.Tests.Data;
using Xunit;
using Xunit.Abstractions;

namespace OpenSage.Tests.Tools.ReplaySketch;

// P6c: observable-effect tests for the implemented special-power handlers. Each test dispatches a
// real engine order through OrderProcessor (with the caster selected, as the game does) and
// asserts the effect the original game produces: heal, ownership transfer, disable, hijack.
// Same-collection as the other Game-creating tests: parallel Game instances collide on static
// state (road texture atlas).

[Collection("SerialGameTests")]
public class P6cSpecialPowerTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private Game? _game;

    public P6cSpecialPowerTests(ITestOutputHelper output)
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

        // Match the validation-ground sandbox: no skirmish AI competing with the test's orders,
        // and deterministic fixed-timestep logic.
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

    private void Dispatch(Player caster, GameObject casterUnit, Order order)
    {
        caster.SelectUnits(new[] { casterUnit });
        new OrderProcessor(_game!).Process([order]);
    }

    [GameFact(SageGame.CncGenerals)]
    public void CleanupArea_HealsDamagedFriendly()
    {
        using var game = _game = CreateGame();
        var scene = game.Scene3D;
        var usa = scene.Players[2];

        var medic = Spawn("AmericaVehicleMedic", new Vector3(600, 600, 0), usa);
        var ranger = Spawn("AmericaInfantryRanger", new Vector3(600, 680, 0), usa);
        Step(10);

        ranger.BodyModule.InternalChangeHealth(-ranger.BodyModule.Health + 20f);
        var damaged = ranger.BodyModule.Health;
        Assert.InRange(damaged, 0f, 30f);

        Dispatch(usa, medic, Order.CreateSpecialPowerAtLocation(
            scene.GetPlayerIndex(usa), (int)SpecialPowerType.CleanupArea,
            ranger.Translation, SpecialPowerOrderFlags.NeedTargetPosition, ObjectId.Invalid));

        Assert.True(ranger.BodyModule.Health > damaged,
            $"CleanupArea did not heal the damaged friendly ({damaged} -> {ranger.BodyModule.Health})");
    }

    [GameFact(SageGame.CncGenerals)]
    public void BlackLotusCaptureBuilding_TransfersOwnership()
    {
        using var game = _game = CreateGame();
        var scene = game.Scene3D;
        var china = scene.Players[3];
        var civilian = scene.Players[1];

        var lotus = Spawn("ChinaInfantryBlackLotus", new Vector3(600, 600, 0), china);
        var plant = Spawn("AmericaPowerPlant", new Vector3(600, 700, 0), civilian);
        Step(10);

        Assert.NotEqual(china, plant.Owner);

        Dispatch(china, lotus, Order.CreateSpecialPowerAtObject(
            scene.GetPlayerIndex(china), (int)SpecialPowerType.BlackLotusCaptureBuilding,
            plant.Id, SpecialPowerOrderFlags.NeedTargetEnemyObject | SpecialPowerOrderFlags.NeedTargetNeutralObject,
            ObjectId.Invalid));

        Assert.Equal(china, plant.Owner);
    }

    [GameFact(SageGame.CncGenerals)]
    public void HackerDisableBuilding_DisablesTarget()
    {
        using var game = _game = CreateGame();
        var scene = game.Scene3D;
        var china = scene.Players[3];
        var civilian = scene.Players[1];

        var hacker = Spawn("ChinaInfantryHacker", new Vector3(600, 600, 0), china);
        var plant = Spawn("AmericaPowerPlant", new Vector3(600, 700, 0), civilian);
        Step(10);

        Assert.False(plant.IsDisabledByType(DisabledType.UserParalyzed));

        Dispatch(china, hacker, Order.CreateSpecialPowerAtObject(
            scene.GetPlayerIndex(china), (int)SpecialPowerType.HackerDisableBuilding,
            plant.Id, SpecialPowerOrderFlags.NeedTargetEnemyObject | SpecialPowerOrderFlags.NeedTargetNeutralObject,
            ObjectId.Invalid));

        Assert.True(plant.IsDisabledByType(DisabledType.UserParalyzed));
    }

    [GameFact(SageGame.CncGenerals)]
    public void HijackerUpdate_HijacksEnemyVehicleOnContact()
    {
        using var game = _game = CreateGame();
        var scene = game.Scene3D;
        var gla = scene.Players[4];
        var usa = scene.Players[2];

        var hijacker = Spawn("GLAInfantryHijacker", new Vector3(600, 600, 0), gla);
        var humvee = Spawn("AmericaVehicleHumvee", new Vector3(600, 630, 0), usa);
        Step(10);

        Assert.Equal(usa, humvee.Owner);
        Assert.False(hijacker.IsEffectivelyDead);

        // Walk the hijacker into the vehicle; the contact hijack fires within a few frames.
        var order = new Order(scene.GetPlayerIndex(gla), OrderType.MoveTo);
        order.AddPositionArgument(humvee.Translation);
        Dispatch(gla, hijacker, order);

        Step(300);

        Assert.Equal(gla, humvee.Owner);
        Assert.True(hijacker.IsEffectivelyDead);
    }

    public void Dispose()
    {
        _game?.Dispose();
        Platform.Stop();
    }
}
