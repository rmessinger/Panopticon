using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using OpenSage.Logic;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;
using OpenSage.Mathematics;
using Xunit;

namespace OpenSage.Tests.Logic.Production;

// WI-P5-03: Unit and upgrade production.
// Verifies units queued via ProductionUpdate.QueueProduction spawn, are owned by the
// producer's owner, and move to rally points. Upgrades queued via QueueUpgrade complete
// and apply their player/object effect. Lifecycle paths (cancel/refund, queue capacity,
// ownership) are covered.
//
// Run with: dotnet test src --filter "FullyQualifiedName~UnitUpgradeProductionTests"

public class UnitUpgradeProductionTests : MockedGameTest
{
    private const int PlayerIdx = 0;
    private const uint StartingMoney = 10_000u;
    private static readonly Vector3 RallyPoint = new(100, 100, 0);
    private const uint UnitBuildTimeFrames = 3;

    [Fact, Trait("Category", "Roadmap")]
    public void QueueProduction_AdvancesAndSpawnsUnit_OwnedByProducerOwner()
    {
        var player = GetPlayer();
        var (producer, unitDef) = CreateProducerAndUnitDef(player);

        producer.ProductionUpdate!.QueueProduction(unitDef);

        Assert.Single(producer.ProductionUpdate.ProductionQueue);

        AdvanceProductionToCompletion(producer);

        Assert.Empty(producer.ProductionUpdate.ProductionQueue);

        var producedUnit = Generals.GameLogic.Objects
            .FirstOrDefault(o => o.Definition == unitDef && o.Owner == player);
        Assert.NotNull(producedUnit);
        Assert.Same(player, producedUnit!.Owner);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void QueueProduction_SpawnedUnitHasRallyPointTarget()
    {
        var player = GetPlayer();
        var (producer, unitDef) = CreateProducerAndUnitDef(player);
        producer.RallyPoint = RallyPoint;

        producer.ProductionUpdate!.QueueProduction(unitDef);
        AdvanceProductionToCompletion(producer);

        var producedUnit = Generals.GameLogic.Objects
            .First(o => o.Definition == unitDef && o.Owner == player);

        Assert.NotEmpty(producedUnit.AIUpdate.TargetPoints);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void BeginUpgrade_Order_Path_DebitsCost()
    {
        var player = GetPlayer();
        var initialMoney = player.BankAccount.Money;
        var producer = CreateProducer(player);
        var upgrade = NewUpgradeTemplate("P5Production_DebitUpgrade", UpgradeType.Player, buildTimeFrames: 2);
        typeof(UpgradeTemplate)
            .GetProperty(nameof(UpgradeTemplate.BuildCost))!
            .SetValue(upgrade, 500f);
        Generals.AssetStore.Upgrades.Add(upgrade);

        var beginOrder = new Order(PlayerIdx, OrderType.BeginUpgrade);
        beginOrder.AddObjectIdArgument(producer.Id);
        beginOrder.AddIntegerArgument((int)upgrade.InstanceId);
        new OrderProcessor(Generals).Process([beginOrder]);

        Assert.Equal(initialMoney - 500u, player.BankAccount.Money);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CancelUpgrade_Order_Path_RefundsCost()
    {
        var player = GetPlayer();
        var initialMoney = player.BankAccount.Money;
        var producer = CreateProducer(player);
        var upgrade = NewUpgradeTemplate("P5Production_RefundUpgrade", UpgradeType.Player, buildTimeFrames: 5);
        typeof(UpgradeTemplate)
            .GetProperty(nameof(UpgradeTemplate.BuildCost))!
            .SetValue(upgrade, 500f);
        Generals.AssetStore.Upgrades.Add(upgrade);

        var beginOrder = new Order(PlayerIdx, OrderType.BeginUpgrade);
        beginOrder.AddObjectIdArgument(producer.Id);
        beginOrder.AddIntegerArgument((int)upgrade.InstanceId);
        new OrderProcessor(Generals).Process([beginOrder]);

        Assert.Equal(initialMoney - 500u, player.BankAccount.Money);

        MarkSelected(player, producer);
        var cancelOrder = new Order(PlayerIdx, OrderType.CancelUpgrade);
        cancelOrder.AddIntegerArgument((int)upgrade.InstanceId);
        new OrderProcessor(Generals).Process([cancelOrder]);

        Assert.Equal(initialMoney, player.BankAccount.Money);
        Assert.False(producer.ProductionUpdate!.IsProducing);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CanEnqueue_RespectsMaxQueueEntries()
    {
        var player = GetPlayer();
        var (producer, unitDef) = CreateProducerAndUnitDef(player, maxQueue: 2);

        Assert.True(producer.ProductionUpdate!.CanEnqueue());
        producer.ProductionUpdate.QueueProduction(unitDef);
        Assert.True(producer.ProductionUpdate.CanEnqueue());
        producer.ProductionUpdate.QueueProduction(unitDef);
        Assert.False(producer.ProductionUpdate.CanEnqueue());
    }

    [Fact, Trait("Category", "Roadmap")]
    public void QueueUpgrade_PlayerUpgrade_CompletesAndApplies()
    {
        var player = GetPlayer();
        var producer = CreateProducer(player);
        var upgrade = NewUpgradeTemplate("P5Production_PlayerUpgrade", UpgradeType.Player, buildTimeFrames: 2);
        Generals.AssetStore.Upgrades.Add(upgrade);

        producer.ProductionUpdate!.QueueUpgrade(upgrade);

        Assert.True(producer.ProductionUpdate.IsProducing);
        Assert.True(player.HasEnqueuedUpgrade(upgrade));

        AdvanceProductionToCompletion(producer);

        Assert.False(producer.ProductionUpdate.IsProducing);
        Assert.True(player.HasUpgrade(upgrade));
        Assert.Contains(upgrade, player.UpgradesCompleted);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void QueueUpgrade_ObjectUpgrade_CompletesAndApplies()
    {
        var player = GetPlayer();
        var producer = CreateProducer(player);
        var upgrade = NewUpgradeTemplate("P5Production_ObjectUpgrade", UpgradeType.Object, buildTimeFrames: 2);
        Generals.AssetStore.Upgrades.Add(upgrade);

        producer.ProductionUpdate!.QueueUpgrade(upgrade);

        Assert.True(producer.ProductionUpdate.IsProducing);

        AdvanceProductionToCompletion(producer);

        Assert.False(producer.ProductionUpdate.IsProducing);
        Assert.True(producer.HasUpgrade(upgrade));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CancelUpgrade_RemovesFromQueue()
    {
        var player = GetPlayer();
        var producer = CreateProducer(player);
        var upgrade = NewUpgradeTemplate("P5Production_CancelUpgrade", UpgradeType.Player);
        Generals.AssetStore.Upgrades.Add(upgrade);

        producer.ProductionUpdate!.QueueUpgrade(upgrade);
        Assert.True(producer.ProductionUpdate.IsProducing);

        producer.ProductionUpdate.CancelUpgrade(upgrade);

        Assert.False(producer.ProductionUpdate.IsProducing);
        Assert.Empty(producer.ProductionUpdate.ProductionQueue);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void BeginUpgrade_Order_Path_QueuesUpgradeThroughOrderProcessor()
    {
        var player = GetPlayer();
        var producer = CreateProducer(player);
        var upgrade = NewUpgradeTemplate("P5Production_OrderUpgrade", UpgradeType.Player, buildTimeFrames: 2);
        Generals.AssetStore.Upgrades.Add(upgrade);

        var order = new Order(PlayerIdx, OrderType.BeginUpgrade);
        order.AddObjectIdArgument(producer.Id);
        order.AddIntegerArgument((int)upgrade.InstanceId);
        new OrderProcessor(Generals).Process([order]);

        Assert.True(producer.ProductionUpdate!.IsProducing);
        Assert.True(player.HasEnqueuedUpgrade(upgrade));
    }

    private Player GetPlayer()
    {
        var player = Generals.PlayerManager.GetPlayerByIndex(PlayerIdx);
        player.BankAccount.Money = StartingMoney;
        return player;
    }

    private static void AdvanceProductionToCompletion(GameObject producer)
    {
        var productionUpdate = producer.ProductionUpdate!;
        var maxTicks = 100;
        while (productionUpdate.ProductionQueue.Count > 0 && maxTicks-- > 0)
        {
            productionUpdate.Update();
        }
    }

    private (GameObject producer, ObjectDefinition unitDef) CreateProducerAndUnitDef(
        Player owner, float buildCost = 0f, int maxQueue = 0)
    {
        var unitDef = new ObjectDefinition($"P5Unit_{owner.OwnedObjects.Count}");
        unitDef.KindOf.Set(ObjectKinds.CanAttack, true);
        typeof(ObjectDefinition)
            .GetProperty(nameof(ObjectDefinition.BuildTime))!
            .SetValue(unitDef, new LogicFrameSpan(UnitBuildTimeFrames));
        if (buildCost > 0)
        {
            typeof(ObjectDefinition)
                .GetProperty(nameof(ObjectDefinition.BuildCost))!
                .SetValue(unitDef, buildCost);
        }

        var unitAiData = new DozerAIUpdateModuleData();
        unitDef.Behaviors.Add("ModuleTag_UnitAI",
            new ModuleDataContainer("ModuleTag_UnitAI", unitAiData, ModuleInheritanceMode.Default, inherited: false));

        var producer = CreateProducer(owner, maxQueue);
        return (producer, unitDef);
    }

    private GameObject CreateProducer(Player owner, int maxQueue = 0)
    {
        var producerDef = new ObjectDefinition($"P5Producer_{owner.OwnedObjects.Count}");
        producerDef.KindOf.Set(ObjectKinds.Structure, true);

        var prodData = new ProductionUpdateModuleData();
        if (maxQueue > 0)
        {
            typeof(ProductionUpdateModuleData)
                .GetProperty(nameof(ProductionUpdateModuleData.MaxQueueEntries))!
                .SetValue(prodData, maxQueue);
        }
        producerDef.Behaviors.Add("ModuleTag_Production",
            new ModuleDataContainer("ModuleTag_Production", prodData, ModuleInheritanceMode.Default, inherited: false));

        var exitData = new DefaultProductionExitUpdateModuleData();
        typeof(DefaultProductionExitUpdateModuleData)
            .GetProperty(nameof(DefaultProductionExitUpdateModuleData.UnitCreatePoint))!
            .SetValue(exitData, new Vector3(10, 0, 0));
        typeof(DefaultProductionExitUpdateModuleData)
            .GetProperty(nameof(DefaultProductionExitUpdateModuleData.NaturalRallyPoint))!
            .SetValue(exitData, new Vector3(20, 0, 0));
        producerDef.Behaviors.Add("ModuleTag_Exit",
            new ModuleDataContainer("ModuleTag_Exit", exitData, ModuleInheritanceMode.Default, inherited: false));

        var producer = Generals.GameLogic.CreateObject(producerDef, owner);
        owner.AddOwnedObject(producer);
        return producer;
    }

    private static UpgradeTemplate NewUpgradeTemplate(string name, UpgradeType type, uint buildTimeFrames = 3)
    {
        var template = new UpgradeTemplate();
        typeof(BaseAsset)
            .GetMethod("SetNameAndInstanceId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(template, ["Upgrade", name]);
        typeof(UpgradeTemplate)
            .GetProperty(nameof(UpgradeTemplate.BuildTime))!
            .SetValue(template, new LogicFrameSpan(buildTimeFrames));
        typeof(UpgradeTemplate)
            .GetProperty(nameof(UpgradeTemplate.Type))!
            .SetValue(template, type);
        return template;
    }
    private static void MarkSelected(Player owner, params GameObject[] units)
    {
        var field = typeof(Player).GetField("_selectedUnits", BindingFlags.Instance | BindingFlags.NonPublic);
        var set = (HashSet<GameObject>)field!.GetValue(owner)!;
        set.UnionWith(units);
    }
}
