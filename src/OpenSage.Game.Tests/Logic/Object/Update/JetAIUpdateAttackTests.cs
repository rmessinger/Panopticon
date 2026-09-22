using System;
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

// JetAIUpdate attack pursuit: a jet (no airfield) whose weapon target is out of range flies to
// it on the attack locomotor and delivers its weapon (AuroraBombWeapon spawns the AuroraBomb
// projectile). Closes the validation-ground "bomber" gap: the Aurora's attack is now commandable
// both directly (in range) and through the AttackObject order (out of range).

[Collection("SerialGameTests")]
public class JetAIUpdateAttackTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private Game? _game;

    public JetAIUpdateAttackTests(ITestOutputHelper output)
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

    [GameFact(SageGame.CncGenerals)]
    public void JetAttacksOutOfRangeTarget_ViaAttackObjectOrder()
    {
        using var game = _game = CreateGame();
        var scene = game.Scene3D;
        var usa = scene.Players[2];
        var civilian = scene.Players[1];

        var aurora = Spawn("AmericaJetAurora", new Vector3(600, 600, 0), usa);
        var car = Spawn("CarAsian1", new Vector3(1400, 600, 0), civilian);
        car.BodyModule.SetMaxHealth(30);
        Step(30);

        // Well outside the bomb's 300 range: only attack pursuit can close the distance.
        Assert.False(aurora.CurrentWeapon!.HasValidTarget);

        usa.SelectUnits(new[] { aurora });
        var order = new Order(scene.GetPlayerIndex(usa), OrderType.AttackObject);
        order.AddObjectIdArgument(car.Id);
        new OrderProcessor(game).Process([order]);

        Step(600);

        Assert.True(car.IsEffectivelyDead,
            "Aurora did not fly to and destroy the out-of-range target via an AttackObject order");
    }

    [GameFact(SageGame.CncGenerals)]
    public void JetAttacksInRangeTarget_ViaDirectWeaponCommand()
    {
        using var game = _game = CreateGame();
        var scene = game.Scene3D;
        var usa = scene.Players[2];
        var civilian = scene.Players[1];

        var aurora = Spawn("AmericaJetAurora", new Vector3(600, 600, 0), usa);
        var car = Spawn("CarAsian1", new Vector3(600, 680, 0), civilian);
        car.BodyModule.SetMaxHealth(30);
        Step(30);

        aurora.CurrentWeapon!.SetTarget(new WeaponTarget(scene.GameObjects, car.Id));

        Step(100);

        Assert.True(car.IsEffectivelyDead,
            "Aurora did not destroy the in-range target via a direct weapon command");
    }

    public void Dispose()
    {
        _game?.Dispose();
        Platform.Stop();
    }
}
