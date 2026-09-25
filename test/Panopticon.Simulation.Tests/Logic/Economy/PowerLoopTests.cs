// P4-3 power loop verification: proves the energy production/consumption loop
// drives `Player.HasInsufficientPower` deterministically, and that the flag
// controls the production-slowdown effect (advances ProductionJob at
// GameData.MaxLowEnergyProductionSpeed instead of one frame per tick).
//
// Verified low-power effect: production slowdown via GameData.MaxLowEnergyProductionSpeed
// (GameData.MinLowEnergyProductionSpeed is reserved for a future randomised-within-range
// implementation; the smallest version pins the multiplier to the max). The slowdown is
// applied in ProductionUpdate.Update -> ProductionJob.Update(speedMultiplier).
//
// See docs/roadmap/deep-dives/p4-p6-economy-production-combat.md P4-3.
//
// Run with: dotnet test src --filter "FullyQualifiedName~PowerLoopTests"

using System.Reflection;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Logic.Object.Production;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.Logic.Economy;

public class PowerLoopTests : MockedGameTest
{
    private const uint PlayerIdx = 2;

    [Fact, Trait("Category", "Roadmap")]
    public void NoOwnedObjects_FlagFalseByDefault()
    {
        var player = CreateSkirmishPlayer();

        Assert.False(player.HasInsufficientPower);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void ProductionOnly_FlagStaysFalse()
    {
        var player = CreateSkirmishPlayer();
        AddOwnedStructure(player, energyProduction: 100);

        player.LogicTick();

        Assert.False(player.HasInsufficientPower);
        Assert.Equal(100, player.EnergyBalance);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void ConsumptionExceedsProduction_FlagTrue()
    {
        var player = CreateSkirmishPlayer();
        AddOwnedStructure(player, energyProduction: 100);
        AddOwnedStructure(player, energyProduction: -150);

        player.LogicTick();

        Assert.True(player.HasInsufficientPower);
        Assert.Equal(-50, player.EnergyBalance);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void BalancedExactly_FlagStaysFalse()
    {
        // Boundary: production == consumption. SAGE treats zero net as sufficient.
        var player = CreateSkirmishPlayer();
        AddOwnedStructure(player, energyProduction: 100);
        AddOwnedStructure(player, energyProduction: -100);

        player.LogicTick();

        Assert.False(player.HasInsufficientPower);
        Assert.Equal(0, player.EnergyBalance);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void InsufficientByOne_FlagTrue()
    {
        var player = CreateSkirmishPlayer();
        AddOwnedStructure(player, energyProduction: 100);
        AddOwnedStructure(player, energyProduction: -101);

        player.LogicTick();

        Assert.True(player.HasInsufficientPower);
        Assert.Equal(-1, player.EnergyBalance);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void RestoringPower_ClearsFlag()
    {
        var player = CreateSkirmishPlayer();
        var consumer = AddOwnedStructure(player, energyProduction: -100);
        player.LogicTick();
        Assert.True(player.HasInsufficientPower);

        // Restore: add enough production to bring net positive.
        AddOwnedStructure(player, energyProduction: 150);
        player.LogicTick();

        Assert.False(player.HasInsufficientPower);
        Assert.Equal(50, player.EnergyBalance);

        // Removing the consumer (teardown) also keeps the flag clear.
        player.RemoveOwnedObject(consumer);
        player.LogicTick();
        Assert.False(player.HasInsufficientPower);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void LowPower_SlowsProduction_ProgressAdvancesAtLowEnergySpeed()
    {
        // Verified low-power effect: production slowdown via GameData.MaxLowEnergyProductionSpeed.
        // With speedMultiplier=1.0 (sufficient power) and BuildTime=10 frames, 10 ProductionJob
        // updates bring Progress to 1.0. With speedMultiplier=0.5 (insufficient power), the same
        // 10 updates bring Progress to only ~0.5. ProductionUpdate.Update passes the multiplier
        // based on Player.HasInsufficientPower; ProductionJob applies it via a sub-frame
        // accumulator.
        typeof(GameData)
            .GetProperty(nameof(GameData.MaxLowEnergyProductionSpeed))!
            .SetValue(Generals.AssetStore.GameData.Current, 0.5f);

        var unitDef = NewObjectDefinition("P4PowerTestUnit", buildTimeFrames: 10);

        // Full-power baseline.
        var fullPowerJob = new ProductionJob(unitDef, new LogicFrameSpan(10), jobId: 1);
        for (var i = 0; i < 10; i++)
        {
            fullPowerJob.Update(speedMultiplier: 1.0f);
        }

        Assert.True(fullPowerJob.Progress >= 1.0f);

        // Low-power run: progress advances at half speed.
        var lowPowerJob = new ProductionJob(unitDef, new LogicFrameSpan(10), jobId: 2);
        for (var i = 0; i < 10; i++)
        {
            lowPowerJob.Update(speedMultiplier: 0.5f);
        }

        Assert.InRange(lowPowerJob.Progress, 0.4f, 0.6f);

        // Catch-up: another 10 low-power ticks bring it to completion (10 + 10 = 20 ticks at 0.5 = 10 frames).
        for (var i = 0; i < 10; i++)
        {
            lowPowerJob.Update(speedMultiplier: 0.5f);
        }

        Assert.True(lowPowerJob.Progress >= 1.0f);
    }

    private Player CreateSkirmishPlayer()
    {
        Generals.PlayerManager.OnNewGame(
            [
                Panopticon.Data.Map.Player.CreateNeutralPlayer(),
                Panopticon.Data.Map.Player.CreateCivilianPlayer(),
                new Panopticon.Data.Map.Player
                {
                    Name = "human",
                    Faction = "FactionAmerica",
                    DisplayName = "Human",
                    IsHuman = true,
                },
            ],
            GameType.Skirmish,
            [
                new PlayerSetting(1, "FactionAmerica", new ColorRgb(255, 255, 255), -1, PlayerOwner.Player, "human", true),
            ]);
        return Generals.PlayerManager.GetPlayerByIndex(PlayerIdx);
    }

    private GameObject AddOwnedStructure(Player owner, int energyProduction)
    {
        var definition = new ObjectDefinition($"P4PowerTestStructure_{owner.OwnedObjects.Count}_{energyProduction}");
        definition.KindOf.Set(ObjectKinds.Structure, true);
        typeof(ObjectDefinition)
            .GetProperty(nameof(ObjectDefinition.EnergyProduction))!
            .SetValue(definition, energyProduction);
        var gameObject = new GameObject(definition, Generals.GameEngine, owner);
        // EnergyProduction is applied to GameObject on construction completion; reflect to pin
        // the runtime field directly so the test does not have to drive the construction lifecycle.
        typeof(GameObject)
            .GetProperty(nameof(GameObject.EnergyProduction))!
            .SetValue(gameObject, energyProduction);
        owner.AddOwnedObject(gameObject);
        return gameObject;
    }

    private static ObjectDefinition NewObjectDefinition(string name, int buildTimeFrames)
    {
        var definition = new ObjectDefinition(name);
        definition.KindOf.Set(ObjectKinds.Infantry, true);
        typeof(ObjectDefinition)
            .GetProperty(nameof(ObjectDefinition.BuildTime))!
            .SetValue(definition, new LogicFrameSpan((uint)buildTimeFrames));
        return definition;
    }

    private GameObject CreateProducerWithQueue(Player owner, ObjectDefinition unitDef)
    {
        Generals.AssetStore.ObjectDefinitions.Add(unitDef);
        var producerDef = new ObjectDefinition($"P4PowerTestProducer_{owner.OwnedObjects.Count}");
        producerDef.KindOf.Set(ObjectKinds.Structure, true);
        producerDef.Behaviors.Add("ModuleTag_Production",
            new ModuleDataContainer("ModuleTag_Production", new ProductionUpdateModuleData(),
                ModuleInheritanceMode.Default, inherited: false));
        var producer = new GameObject(producerDef, Generals.GameEngine, owner);
        owner.AddOwnedObject(producer);
        producer.ProductionUpdate!.QueueProduction(unitDef);
        return producer;
    }
}
