using System.Runtime.CompilerServices;
using OpenSage.Content;
using OpenSage.Gui.ControlBar;
using OpenSage.Logic;
using OpenSage.Logic.AI;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;
using OpenSage.Mathematics;
using Xunit;

namespace OpenSage.Tests.Logic.AI;

public sealed class SkirmishAIUnitProductionTests : MockedGameTest
{
    [Fact]
    public void DecisionTick_WithProducerAndAffordableArmyUnit_EmitsCreateUnitOrder()
    {
        var producerDefinition = new ObjectDefinition("AITestBarracks");
        producerDefinition.KindOf.Set(ObjectKinds.Structure, true);

        var unitDefinition = new ObjectDefinition("AITestRanger");
        unitDefinition.KindOf.Set(ObjectKinds.CanAttack, true);
        SetProperty(unitDefinition, nameof(ObjectDefinition.BuildCost), 50f);

        var commandSet = new CommandSet();
        var unitBuildButton = new CommandButton(
            CommandType.UnitBuild,
            new LazyAssetReference<ObjectDefinition>(unitDefinition));
        commandSet.Buttons[1] = new LazyAssetReference<CommandButton>(unitBuildButton);
        producerDefinition.CommandSet = new LazyAssetReference<CommandSet>(commandSet);

        Generals.AssetStore.ObjectDefinitions.Add(producerDefinition);
        Generals.AssetStore.ObjectDefinitions.Add(unitDefinition);
        RegisterPlayerTemplates();

        var manager = new PlayerManager(Generals);
        manager.OnNewGame(CreateMapPlayers(), GameType.Skirmish, CreatePlayerSettings());

        var aiPlayer = manager.Players[3];
        aiPlayer.BankAccount.Money = 1_000;

        var producer = CreateFakeGameObject(producerDefinition, aiPlayer, isStructure: true);
        aiPlayer.AddOwnedObject(producer);

        for (var i = 0; i < (int)AIPlayer.DefaultDecisionCadenceFrames; i++)
        {
            aiPlayer.LogicTick();
        }

        Assert.Contains(
            Generals.InjectedOrders,
            x => x.OrderType == OrderType.CreateUnit && x.PlayerIndex == 3);
    }

    [Fact]
    public void DecisionTick_WithUnaffordableUnit_DoesNotEmitCreateUnitOrder()
    {
        var producerDefinition = new ObjectDefinition("AITestFactory");
        producerDefinition.KindOf.Set(ObjectKinds.Structure, true);

        var expensiveUnit = new ObjectDefinition("AITestExpensiveUnit");
        expensiveUnit.KindOf.Set(ObjectKinds.CanAttack, true);
        SetProperty(expensiveUnit, nameof(ObjectDefinition.BuildCost), 10_000f);

        var commandSet = new CommandSet();
        var unitBuildButton = new CommandButton(
            CommandType.UnitBuild,
            new LazyAssetReference<ObjectDefinition>(expensiveUnit));
        commandSet.Buttons[1] = new LazyAssetReference<CommandButton>(unitBuildButton);
        producerDefinition.CommandSet = new LazyAssetReference<CommandSet>(commandSet);

        Generals.AssetStore.ObjectDefinitions.Add(producerDefinition);
        Generals.AssetStore.ObjectDefinitions.Add(expensiveUnit);
        RegisterPlayerTemplates();

        var manager = new PlayerManager(Generals);
        manager.OnNewGame(CreateMapPlayers(), GameType.Skirmish, CreatePlayerSettings());

        var aiPlayer = manager.Players[3];
        aiPlayer.BankAccount.Money = 0;

        var producer = CreateFakeGameObject(producerDefinition, aiPlayer, isStructure: true);
        aiPlayer.AddOwnedObject(producer);

        for (var i = 0; i < (int)AIPlayer.DefaultDecisionCadenceFrames; i++)
        {
            aiPlayer.LogicTick();
        }

        Assert.DoesNotContain(
            Generals.InjectedOrders,
            x => x.OrderType == OrderType.CreateUnit && x.PlayerIndex == 3);
        Assert.True(aiPlayer.BankAccount.Money >= 0);
    }

    private static GameObject CreateFakeGameObject(ObjectDefinition definition, Player owner, bool isStructure)
    {
        var gameObject = (GameObject)RuntimeHelpers.GetUninitializedObject(typeof(GameObject));

        SetField(gameObject, "_id", new ObjectId((uint)(1000 + owner.OwnedObjects.Count)));
        SetField(gameObject, "Definition", definition);
        SetProperty(gameObject, nameof(GameObject.IsStructure), isStructure);
        SetProperty(gameObject, nameof(GameObject.CanAttack), !isStructure);
        gameObject.Owner = owner;

        return gameObject;
    }

    private static void SetField<TInstance, TValue>(TInstance instance, string fieldName, TValue value)
    {
        var field = typeof(TInstance).GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(instance, value);
    }

    private static void SetProperty<TInstance, TValue>(TInstance instance, string propertyName, TValue value)
    {
        var property = typeof(TInstance).GetProperty(
            propertyName,
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(property);
        property!.SetValue(instance, value);
    }

    private static OpenSage.Data.Map.Player[] CreateMapPlayers() =>
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
    ];

    private static PlayerSetting[] CreatePlayerSettings() =>
    [
        new PlayerSetting(1, "FactionAmerica", new ColorRgb(255, 255, 255), 1, PlayerOwner.Player, "human", true),
        new PlayerSetting(2, "FactionGLA", new ColorRgb(255, 128, 64), 2, PlayerOwner.EasyAi, "ai"),
    ];

    private void RegisterPlayerTemplates()
    {
        Generals.AssetStore.PlayerTemplates.Add(CreatePlayerTemplate("FactionAmerica"));
        Generals.AssetStore.PlayerTemplates.Add(CreatePlayerTemplate("FactionGLA"));
    }

    private static PlayerTemplate CreatePlayerTemplate(string name)
    {
        var template = new PlayerTemplate();
        var setNameAndInstanceId = typeof(PlayerTemplate).GetMethod(
            "SetNameAndInstanceId",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        setNameAndInstanceId!.Invoke(template, new object[] { "PlayerTemplate", name });
        return template;
    }
}
