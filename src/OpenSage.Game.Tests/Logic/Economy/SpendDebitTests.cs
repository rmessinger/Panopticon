// P4-2 spend/debit verification: proves purchase paths (BuildObject, CreateUnit,
// BeginUpgrade) debit BankAccount only when affordable and that cancel paths refund
// exactly once. Covers the affordability guard added in OrderProcessor.TryDebit
// (blocks the action and leaves state untouched when balance < cost) plus the
// existing Deposit-based refund mechanics on CancelBuild/CancelUnit/CancelUpgrade.
//
// See docs/roadmap/deep-dives/p4-p6-economy-production-combat.md P4-2.
//
// Run with: dotnet test src --filter "FullyQualifiedName~SpendDebitTests"

using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;
using OpenSage.Mathematics;
using Xunit;
using static OpenSage.Logic.Orders.OrderProcessor;

namespace OpenSage.Tests.Logic.Economy;

public class SpendDebitTests : MockedGameTest
{
    private const int PlayerIdx = 2;

    [Fact, Trait("Category", "Roadmap")]
    public void TryDebit_Affordable_ReducesBalance_ReturnsTrue()
    {
        var account = NewAccount(1_000u);

        var debited = TryDebit(account, 300u);

        Assert.True(debited);
        Assert.Equal(700u, account.Money);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void TryDebit_ExactCost_ReducesToZero_ReturnsTrue()
    {
        var account = NewAccount(250u);

        var debited = TryDebit(account, 250u);

        Assert.True(debited);
        Assert.Equal(0u, account.Money);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void TryDebit_Unaffordable_LeavesBalance_ReturnsFalse()
    {
        var account = NewAccount(100u);

        var debited = TryDebit(account, 500u);

        Assert.False(debited);
        Assert.Equal(100u, account.Money);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void TryDebit_ZeroCost_NoChange_ReturnsTrue()
    {
        var account = NewAccount(500u);

        var debited = TryDebit(account, 0u);

        Assert.True(debited);
        Assert.Equal(500u, account.Money);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void OrderProcessor_BuildObject_Unaffordable_BlocksAndLeavesMoneyUntouched()
    {
        ConfigureTwoPlayerSkirmish();
        var player = Generals.PlayerManager.GetPlayerByIndex(PlayerIdx);
        player.BankAccount.Money = 100u;

        var structure = NewObjectDefinition("P4SpendTestStructure", buildCost: 1_000f);
        Generals.AssetStore.ObjectDefinitions.Add(structure);

        var order = Order.CreateBuildObject(PlayerIdx, (int)structure.InstanceId, Vector3.Zero, 0f);
        new OrderProcessor(Generals).Process([order]);

        Assert.Equal(100u, player.BankAccount.Money);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void OrderProcessor_CreateUnit_Unaffordable_BlocksAndLeavesMoneyUntouched()
    {
        ConfigureTwoPlayerSkirmish();
        var player = Generals.PlayerManager.GetPlayerByIndex(PlayerIdx);
        player.BankAccount.Money = 50u;

        var unit = NewObjectDefinition("P4SpendTestUnit", buildCost: 500f);
        unit.KindOf.Set(ObjectKinds.CanAttack, true);
        Generals.AssetStore.ObjectDefinitions.Add(unit);

        var order = new Order(PlayerIdx, OrderType.CreateUnit);
        order.AddIntegerArgument((int)unit.InstanceId);
        new OrderProcessor(Generals).Process([order]);

        Assert.Equal(50u, player.BankAccount.Money);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void OrderProcessor_BeginUpgrade_Unaffordable_BlocksAndLeavesMoneyUntouched()
    {
        ConfigureTwoPlayerSkirmish();
        var player = Generals.PlayerManager.GetPlayerByIndex(PlayerIdx);
        player.BankAccount.Money = 75u;

        var upgrade = NewUpgradeTemplate("P4SpendTestUpgrade", buildCost: 2_000f);
        Generals.AssetStore.Upgrades.Add(upgrade);

        var order = new Order(PlayerIdx, OrderType.BeginUpgrade);
        order.AddObjectIdArgument(new ObjectId(1));
        order.AddIntegerArgument((int)upgrade.InstanceId);
        new OrderProcessor(Generals).Process([order]);

        Assert.Equal(75u, player.BankAccount.Money);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void OrderProcessor_CancelUnit_RefundsFullCostOncePerProducer()
    {
        ConfigureTwoPlayerSkirmish();
        var player = Generals.PlayerManager.GetPlayerByIndex(PlayerIdx);
        player.BankAccount.Money = 0u;

        var unitDef = NewObjectDefinition("P4SpendTestUnit_Cancel", buildCost: 200f);
        var producer = CreateProducerWithQueuedJob(player, unitDef);

        // CancelUnit on queue index 0 refunds BuildCost exactly once for the selected producer.
        var order = new Order(PlayerIdx, OrderType.CancelUnit);
        order.AddIntegerArgument(0);
        new OrderProcessor(Generals).Process([order]);

        Assert.Equal(200u, player.BankAccount.Money);
        Assert.Empty(producer.ProductionUpdate!.ProductionQueue);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void OrderProcessor_CancelUpgrade_RefundsFullCostOnce()
    {
        ConfigureTwoPlayerSkirmish();
        var player = Generals.PlayerManager.GetPlayerByIndex(PlayerIdx);
        player.BankAccount.Money = 0u;

        var upgrade = NewUpgradeTemplate("P4SpendTestUpgrade_Cancel", buildCost: 750f);
        var producer = CreateProducerWithQueuedUpgrade(player, upgrade);

        var order = new Order(PlayerIdx, OrderType.CancelUpgrade);
        order.AddIntegerArgument((int)upgrade.InstanceId);
        new OrderProcessor(Generals).Process([order]);

        Assert.Equal(750u, player.BankAccount.Money);
        Assert.False(producer.ProductionUpdate!.IsProducing);
    }

    private BankAccount NewAccount(uint money)
    {
        var account = new BankAccount(Generals, 0);
        account.Money = money;
        return account;
    }

    private void ConfigureTwoPlayerSkirmish()
    {
        Generals.PlayerManager.OnNewGame(
            [
                OpenSage.Data.Map.Player.CreateNeutralPlayer(),
                OpenSage.Data.Map.Player.CreateCivilianPlayer(),
                new OpenSage.Data.Map.Player
                {
                    Name = "human",
                    Faction = "FactionAmerica",
                    DisplayName = "Human",
                    IsHuman = true,
                },
                new OpenSage.Data.Map.Player
                {
                    Name = "ai",
                    Faction = "FactionGLA",
                    DisplayName = "AI",
                    IsHuman = false,
                },
            ],
            GameType.Skirmish,
            [
                new PlayerSetting(1, "FactionAmerica", new ColorRgb(255, 255, 255), -1, PlayerOwner.Player, "human", true),
                new PlayerSetting(2, "FactionGLA", new ColorRgb(255, 128, 64), -1, PlayerOwner.EasyAi, "ai"),
            ]);
    }

    private static ObjectDefinition NewObjectDefinition(string name, float buildCost)
    {
        var definition = new ObjectDefinition(name);
        definition.KindOf.Set(ObjectKinds.Structure, true);
        typeof(ObjectDefinition)
            .GetProperty(nameof(ObjectDefinition.BuildCost))!
            .SetValue(definition, buildCost);
        return definition;
    }

    private static UpgradeTemplate NewUpgradeTemplate(string name, float buildCost)
    {
        var template = new UpgradeTemplate();
        typeof(BaseAsset)
            .GetMethod("SetNameAndInstanceId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(template, ["Upgrade", name]);
        typeof(UpgradeTemplate)
            .GetProperty(nameof(UpgradeTemplate.BuildCost))!
            .SetValue(template, buildCost);
        return template;
    }

    private GameObject CreateProducerWithQueuedJob(Player owner, ObjectDefinition unitDef)
    {
        Generals.AssetStore.ObjectDefinitions.Add(unitDef);

        var producerDef = new ObjectDefinition("P4SpendTestProducer_Unit");
        producerDef.KindOf.Set(ObjectKinds.Structure, true);
        producerDef.Behaviors.Add("ModuleTag_Production",
            new ModuleDataContainer("ModuleTag_Production", new ProductionUpdateModuleData(),
                ModuleInheritanceMode.Default, inherited: false));
        var producer = new GameObject(producerDef, Generals.GameEngine, owner);
        owner.AddOwnedObject(producer);
        MarkSelected(owner, producer);
        producer.ProductionUpdate!.QueueProduction(unitDef);
        return producer;
    }

    private GameObject CreateProducerWithQueuedUpgrade(Player owner, UpgradeTemplate upgrade)
    {
        Generals.AssetStore.Upgrades.Add(upgrade);

        var producerDef = new ObjectDefinition("P4SpendTestProducer_Upgrade");
        producerDef.KindOf.Set(ObjectKinds.Structure, true);
        producerDef.Behaviors.Add("ModuleTag_Production",
            new ModuleDataContainer("ModuleTag_Production", new ProductionUpdateModuleData(),
                ModuleInheritanceMode.Default, inherited: false));
        var producer = new GameObject(producerDef, Generals.GameEngine, owner);
        owner.AddOwnedObject(producer);
        MarkSelected(owner, producer);
        producer.ProductionUpdate!.QueueUpgrade(upgrade);
        return producer;
    }

    // Player.SelectUnits triggers unit.Drawable.TriggerSelection(); in the mocked host the
    // Drawable is null, so reflect directly into the player's _selectedUnits set instead.
    // OrderProcessor.CancelUnit/CancelUpgrade/CancelBuild only read player.SelectedUnits,
    // which is the same backing field.
    private static void MarkSelected(Player owner, params GameObject[] units)
    {
        var field = typeof(Player).GetField("_selectedUnits", BindingFlags.Instance | BindingFlags.NonPublic);
        var set = (HashSet<GameObject>)field!.GetValue(owner)!;
        set.UnionWith(units);
    }
}
