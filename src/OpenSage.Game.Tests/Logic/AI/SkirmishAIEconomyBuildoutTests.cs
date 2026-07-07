using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using OpenSage.Logic;
using OpenSage.Logic.AI;
using OpenSage.Logic.Map;
using OpenSage.Logic.Object;
using OpenSage.Logic.Orders;
using OpenSage.Mathematics;
using Xunit;

namespace OpenSage.Tests.Logic.AI;

public sealed class SkirmishAIEconomyBuildoutTests : MockedGameTest
{
    [Fact]
    public void DecisionTick_WithMatchingBuildList_EmitsBuildObjectOrder()
    {
        var testDefinition = new ObjectDefinition("AITestSupplyCenter");
        testDefinition.KindOf.Set(ObjectKinds.Structure, true);
        Generals.AssetStore.ObjectDefinitions.Add(testDefinition);

        var buildInfo = CreateBuildListInfo(
            templateName: testDefinition.Name,
            location: new Vector3(10, 20, 0),
            angle: 0.5f,
            isSupplyBuilding: true,
            priorityBuild: true);
        Generals.AssetStore.AIData.Current.SkirmishBuildLists.Add(
            CreateBuildList("FactionGLA", [buildInfo]));

        var manager = new PlayerManager(Generals);
        RegisterPlayerTemplates();
        manager.OnNewGame(CreateMapPlayers(), GameType.Skirmish, CreatePlayerSettings());

        for (var i = 0; i < (int)AIPlayer.DefaultDecisionCadenceFrames; i++)
        {
            manager.Players[3].LogicTick();
        }

        Assert.Contains(
            Generals.InjectedOrders,
            x => x.OrderType == OrderType.BuildObject && x.PlayerIndex == 3);
    }

    [Fact]
    public void DecisionTick_WithoutMatchingBuildList_DoesNotEmitOrders()
    {
        Generals.AssetStore.AIData.Current.SkirmishBuildLists.Add(
            CreateBuildList("FactionAmerica", new List<BuildListInfo>()));

        var manager = new PlayerManager(Generals);
        RegisterPlayerTemplates();
        manager.OnNewGame(CreateMapPlayers(), GameType.Skirmish, CreatePlayerSettings());

        for (var i = 0; i < (int)AIPlayer.DefaultDecisionCadenceFrames; i++)
        {
            manager.Players[3].LogicTick();
        }

        Assert.DoesNotContain(Generals.InjectedOrders, x => x.PlayerIndex == 3);
    }

    private static AISkirmishBuildList CreateBuildList(string name, IReadOnlyList<BuildListInfo> structures)
    {
        var result = new AISkirmishBuildList();
        SetProperty(result, nameof(AISkirmishBuildList.Name), name);

        foreach (var structure in structures)
        {
            result.Structures.Add(structure);
        }

        return result;
    }

    private static BuildListInfo CreateBuildListInfo(
        string templateName,
        Vector3 location,
        float angle,
        bool isSupplyBuilding,
        bool priorityBuild)
    {
        var result = new BuildListInfo();
        SetProperty(result, nameof(BuildListInfo.TemplateName), templateName);
        SetProperty(result, nameof(BuildListInfo.Location), location);
        SetProperty(result, nameof(BuildListInfo.Angle), angle);
        SetProperty(result, nameof(BuildListInfo.NumRebuilds), 1u);
        SetProperty(result, nameof(BuildListInfo.IsSupplyBuilding), isSupplyBuilding);
        SetProperty(result, nameof(BuildListInfo.PriorityBuild), priorityBuild);
        return result;
    }

    private static void SetProperty<TInstance, TValue>(TInstance instance, string propertyName, TValue value)
    {
        var property = typeof(TInstance).GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
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
