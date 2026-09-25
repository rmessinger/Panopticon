using System.Reflection;
using Panopticon.Content;
using Panopticon.Gui.ControlBar;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.Gui;

// WI-P5-04: Control-bar buildability binding.
//
// Investigation result: the Generals control bar binds buildability through
// GeneralsControlBar.ApplyCommandSet (src/Panopticon.Mods.Generals/Gui/GeneralsControlBar.cs:407-519):
//   - DozerConstruct buttons -> GameObject.CanConstructUnit(...) -> buttonControl.Enabled
//   - UnitBuild buttons      -> GameObject.CanConstructUnit(...) -> buttonControl.Enabled
//   - PlayerUpgrade/ObjectUpgrade -> GameObject.CanEnqueueUpgrade(...) -> buttonControl.Enabled
// Generals calls CanConstructUnit/CanEnqueueUpgrade directly (not CanPurchase, which the
// Bfme mod's AptControlBarSource uses instead). The binding already exists; these tests
// verify the underlying methods the control bar depends on reflect prereq + money state.
//
// Run with: dotnet test src --filter "FullyQualifiedName~GeneralsControlBarBuildabilityTests"

public class GeneralsControlBarBuildabilityTests : MockedGameTest
{
    private const int PlayerIdx = 0;

    [Fact, Trait("Category", "Roadmap")]
    public void CanConstructUnit_PrereqsMissingAndNoMoney_ReturnsFalse()
    {
        var player = GetPlayer(money: 0u);
        var prereqDef = NewStructureDef("P5Build_PrereqA");
        Generals.AssetStore.ObjectDefinitions.Add(prereqDef);
        var unitDef = NewStructureDef("P5Build_GatedA", prereqDef, buildCost: 300f);

        var producer = CreateProducer(player);

        Assert.False(producer.CanConstructUnit(unitDef));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CanConstructUnit_PrereqsMetButNoMoney_ReturnsFalse()
    {
        var player = GetPlayer(money: 0u);
        var prereqDef = NewStructureDef("P5Build_PrereqB");
        Generals.AssetStore.ObjectDefinitions.Add(prereqDef);
        var unitDef = NewStructureDef("P5Build_GatedB", prereqDef, buildCost: 300f);

        var producer = CreateProducer(player);
        CreateCompletedBuilding(player, prereqDef);

        Assert.False(producer.CanConstructUnit(unitDef));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CanConstructUnit_PrereqsMissingButHasMoney_ReturnsFalse()
    {
        var player = GetPlayer(money: 10_000u);
        var prereqDef = NewStructureDef("P5Build_PrereqC");
        Generals.AssetStore.ObjectDefinitions.Add(prereqDef);
        var unitDef = NewStructureDef("P5Build_GatedC", prereqDef, buildCost: 300f);

        var producer = CreateProducer(player);

        Assert.False(producer.CanConstructUnit(unitDef));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CanConstructUnit_PrereqsMetAndEnoughMoney_ReturnsTrue()
    {
        var player = GetPlayer(money: 10_000u);
        var prereqDef = NewStructureDef("P5Build_PrereqD");
        Generals.AssetStore.ObjectDefinitions.Add(prereqDef);
        var unitDef = NewStructureDef("P5Build_GatedD", prereqDef, buildCost: 300f);

        var producer = CreateProducer(player);
        CreateCompletedBuilding(player, prereqDef);

        Assert.True(producer.CanConstructUnit(unitDef));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CanEnqueueUpgrade_NoMoney_ReturnsFalse()
    {
        var player = GetPlayer(money: 0u);
        var producer = CreateProducer(player);
        var upgrade = NewUpgradeTemplate("P5Build_UpgradeNoMoney", UpgradeType.Object, buildCost: 500f);

        Assert.False(producer.CanEnqueueUpgrade(upgrade));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CanEnqueueUpgrade_AlreadyHasUpgrade_ReturnsFalse()
    {
        var player = GetPlayer(money: 10_000u);
        var producer = CreateProducer(player);
        var upgrade = NewUpgradeTemplate("P5Build_UpgradeAlready", UpgradeType.Object, buildCost: 500f);

        producer.Upgrade(upgrade);

        Assert.False(producer.CanEnqueueUpgrade(upgrade));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CanEnqueueUpgrade_Available_ReturnsTrue()
    {
        var player = GetPlayer(money: 10_000u);
        var producer = CreateProducer(player);
        var upgrade = NewUpgradeTemplate("P5Build_UpgradeOk", UpgradeType.Object, buildCost: 500f);

        Assert.True(producer.CanEnqueueUpgrade(upgrade));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CanPurchase_DispatchesToCanConstructUnit_ForObjectButtons()
    {
        var player = GetPlayer(money: 10_000u);
        var unitDef = NewStructureDef("P5Build_PurchaseUnit", buildCost: 100f);
        Generals.AssetStore.ObjectDefinitions.Add(unitDef);

        var producer = CreateProducer(player);
        var button = NewCommandButton(objectDef: unitDef);

        Assert.True(producer.CanPurchase(button));

        player.BankAccount.Money = 0u;
        Assert.False(producer.CanPurchase(button));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CanPurchase_DispatchesToCanEnqueueUpgrade_ForUpgradeButtons()
    {
        var player = GetPlayer(money: 10_000u);
        var producer = CreateProducer(player);
        var upgrade = NewUpgradeTemplate("P5Build_PurchaseUpgrade", UpgradeType.Object, buildCost: 200f);
        var button = NewCommandButton(upgrade: upgrade);

        Assert.True(producer.CanPurchase(button));

        player.BankAccount.Money = 0u;
        Assert.False(producer.CanPurchase(button));
    }

    private Player GetPlayer(uint money)
    {
        var player = Generals.PlayerManager.GetPlayerByIndex(PlayerIdx);
        player.BankAccount.Money = money;
        return player;
    }

    private void CreateCompletedBuilding(Player owner, ObjectDefinition definition)
    {
        var building = Generals.GameLogic.CreateObject(definition, owner);
        owner.AddOwnedObject(building);
        building.BuildProgress = 1.0f;
    }

    private GameObject CreateProducer(Player owner)
    {
        var producerDef = new ObjectDefinition($"P5Build_Producer_{owner.OwnedObjects.Count}");
        producerDef.KindOf.Set(ObjectKinds.Structure, true);
        producerDef.Behaviors.Add("ModuleTag_Production",
            new ModuleDataContainer("ModuleTag_Production", new ProductionUpdateModuleData(),
                ModuleInheritanceMode.Default, inherited: false));
        var producer = Generals.GameLogic.CreateObject(producerDef, owner);
        owner.AddOwnedObject(producer);
        return producer;
    }

    private static ObjectDefinition NewStructureDef(string name, ObjectDefinition? prerequisite = null, float buildCost = 0f)
    {
        var definition = new ObjectDefinition(name);
        definition.KindOf.Set(ObjectKinds.Structure, true);
        if (buildCost > 0f)
        {
            typeof(ObjectDefinition)
                .GetProperty(nameof(ObjectDefinition.BuildCost))!
                .SetValue(definition, buildCost);
        }

        if (prerequisite != null)
        {
            typeof(ObjectDefinition)
                .GetProperty(nameof(ObjectDefinition.Prerequisites))!
#pragma warning disable CS8625
                .SetValue(definition, new ObjectPrerequisites());
#pragma warning restore CS8625
            definition.Prerequisites.Objects.Add(new ObjectPrerequisiteList
            {
                new LazyAssetReference<ObjectDefinition>(prerequisite),
            });
        }

        return definition;
    }

    private static UpgradeTemplate NewUpgradeTemplate(string name, UpgradeType type, float buildCost)
    {
        var template = new UpgradeTemplate();
        typeof(BaseAsset)
            .GetMethod("SetNameAndInstanceId", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(template, ["Upgrade", name]);
        typeof(UpgradeTemplate)
            .GetProperty(nameof(UpgradeTemplate.Type))!
            .SetValue(template, type);
        typeof(UpgradeTemplate)
            .GetProperty(nameof(UpgradeTemplate.BuildCost))!
            .SetValue(template, buildCost);
        return template;
    }

    private static CommandButton NewCommandButton(ObjectDefinition? objectDef = null, UpgradeTemplate? upgrade = null)
    {
        var button = new CommandButton();
        if (objectDef != null)
        {
            typeof(CommandButton)
                .GetProperty(nameof(CommandButton.Object))!
                .SetValue(button, new LazyAssetReference<ObjectDefinition>(objectDef));
        }

        if (upgrade != null)
        {
            typeof(CommandButton)
                .GetProperty(nameof(CommandButton.Upgrade))!
                .SetValue(button, new LazyAssetReference<UpgradeTemplate>(upgrade));
        }

        return button;
    }
}
