using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Panopticon.Content;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.Logic.Production;

// WI-P5-02: Prerequisite and tech gating.
// Verifies Player.CanProduceObject rejects unmet object prerequisite chains and accepts
// them once the required faction build-tree prerequisite structure exists with
// BuildProgress >= 1.0. Money gating via GameObject.CanConstructUnit is tested
// separately from prerequisite gating.
//
// Run with: dotnet test src --filter "FullyQualifiedName~PrerequisiteTechGatingTests"

public class PrerequisiteTechGatingTests : MockedGameTest
{
    private const int PlayerIdx = 0;
    private const float GatedCost = 300f;
    private const uint StartingMoney = 10_000u;

    [Fact, Trait("Category", "Roadmap")]
    public void CanProduceObject_NoPrerequisites_ReturnsTrue()
    {
        var player = GetPlayerWithMoney(StartingMoney);
        var def = NewObjectDefinition("P5Gating_NoPrereqs");

        Assert.True(player.CanProduceObject(def));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CanProduceObject_UnmetPrerequisites_ReturnsFalse()
    {
        var player = GetPlayerWithMoney(StartingMoney);
        var prereqDef = NewObjectDefinition("P5Gating_PrereqA");
        var gatedDef = NewObjectDefinition("P5Gating_GatedByA", prereqDef);

        Assert.False(player.CanProduceObject(gatedDef));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CanProduceObject_PrerequisitesMet_ReturnsTrue()
    {
        var player = GetPlayerWithMoney(StartingMoney);
        var prereqDef = NewObjectDefinition("P5Gating_PrereqB");
        Generals.AssetStore.ObjectDefinitions.Add(prereqDef);
        var gatedDef = NewObjectDefinition("P5Gating_GatedByB", prereqDef);

        Assert.False(player.CanProduceObject(gatedDef));

        CreateCompletedBuilding(player, prereqDef);

        Assert.True(player.CanProduceObject(gatedDef));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CanProduceObject_PartiallyBuiltPrereq_ReturnsFalse()
    {
        var player = GetPlayerWithMoney(StartingMoney);
        var prereqDef = NewObjectDefinition("P5Gating_PrereqPartial");
        Generals.AssetStore.ObjectDefinitions.Add(prereqDef);
        var gatedDef = NewObjectDefinition("P5Gating_GatedByPartial", prereqDef);

        var building = Generals.GameLogic.CreateObject(prereqDef, player);
        player.AddOwnedObject(building);
        building.BuildProgress = 0.5f;

        Assert.False(player.CanProduceObject(gatedDef));

        building.BuildProgress = 1.0f;

        Assert.True(player.CanProduceObject(gatedDef));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CanProduceObject_MultiplePrerequisites_AllRequired()
    {
        var player = GetPlayerWithMoney(StartingMoney);
        var prereq1 = NewObjectDefinition("P5Gating_MultiPrereq1");
        var prereq2 = NewObjectDefinition("P5Gating_MultiPrereq2");
        Generals.AssetStore.ObjectDefinitions.Add(prereq1);
        Generals.AssetStore.ObjectDefinitions.Add(prereq2);

        var gatedDef = NewObjectDefinition("P5Gating_GatedByMulti");
        AddObjectPrerequisite(gatedDef, prereq1);
        AddObjectPrerequisite(gatedDef, prereq2);

        Assert.False(player.CanProduceObject(gatedDef));

        CreateCompletedBuilding(player, prereq1);

        Assert.False(player.CanProduceObject(gatedDef));

        CreateCompletedBuilding(player, prereq2);

        Assert.True(player.CanProduceObject(gatedDef));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CanProduceObject_OrPrerequisiteList_AnySatisfies()
    {
        var player = GetPlayerWithMoney(StartingMoney);
        var prereqA = NewObjectDefinition("P5Gating_OrA");
        var prereqB = NewObjectDefinition("P5Gating_OrB");
        Generals.AssetStore.ObjectDefinitions.Add(prereqA);
        Generals.AssetStore.ObjectDefinitions.Add(prereqB);

        var gatedDef = NewObjectDefinition("P5Gating_GatedByOr");
        typeof(ObjectDefinition)
            .GetProperty(nameof(ObjectDefinition.Prerequisites))!
#pragma warning disable CS8625
            .SetValue(gatedDef, new ObjectPrerequisites());
#pragma warning restore CS8625
        var orList = new ObjectPrerequisiteList
        {
            new LazyAssetReference<ObjectDefinition>(prereqA),
            new LazyAssetReference<ObjectDefinition>(prereqB),
        };
        gatedDef.Prerequisites.Objects.Add(orList);

        Assert.False(player.CanProduceObject(gatedDef));

        CreateCompletedBuilding(player, prereqB);

        Assert.True(player.CanProduceObject(gatedDef));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CanConstructUnit_PrerequisitesMet_ButNotEnoughMoney_ReturnsFalse()
    {
        var player = GetPlayerWithMoney(0u);
        var prereqDef = NewObjectDefinition("P5Gating_PrereqMoney");
        Generals.AssetStore.ObjectDefinitions.Add(prereqDef);
        var gatedDef = NewObjectDefinition("P5Gating_GatedByMoney", prereqDef, buildCost: GatedCost);

        CreateCompletedBuilding(player, prereqDef);

        Assert.True(player.CanProduceObject(gatedDef));

        var producer = CreateProducer(player);
        Assert.False(producer.CanConstructUnit(gatedDef));

        player.BankAccount.Money = (uint)GatedCost;
        Assert.True(producer.CanConstructUnit(gatedDef));
    }

    [Fact, Trait("Category", "Roadmap")]
    public void CanConstructUnit_EnoughMoney_ButPrerequisitesNotMet_ReturnsFalse()
    {
        var player = GetPlayerWithMoney(StartingMoney);
        var prereqDef = NewObjectDefinition("P5Gating_PrereqMoneyGate");
        Generals.AssetStore.ObjectDefinitions.Add(prereqDef);
        var gatedDef = NewObjectDefinition("P5Gating_GatedByPrereqMoneyGate", prereqDef, buildCost: GatedCost);

        Assert.False(player.CanProduceObject(gatedDef));

        var producer = CreateProducer(player);
        Assert.False(producer.CanConstructUnit(gatedDef));

        CreateCompletedBuilding(player, prereqDef);
        Assert.True(producer.CanConstructUnit(gatedDef));
    }

    private Player GetPlayerWithMoney(uint money)
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
        var producerDef = new ObjectDefinition("P5Gating_Producer_" + owner.OwnedObjects.Count);
        producerDef.KindOf.Set(ObjectKinds.Structure, true);
        producerDef.Behaviors.Add("ModuleTag_Production",
            new ModuleDataContainer("ModuleTag_Production", new ProductionUpdateModuleData(),
                ModuleInheritanceMode.Default, inherited: false));
        var producer = Generals.GameLogic.CreateObject(producerDef, owner);
        owner.AddOwnedObject(producer);
        return producer;
    }

    private static ObjectDefinition NewObjectDefinition(string name, ObjectDefinition? prerequisite = null, float buildCost = 0f)
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
            AddObjectPrerequisite(definition, prerequisite);
        }

        return definition;
    }

    private static void AddObjectPrerequisite(ObjectDefinition gated, ObjectDefinition prerequisite)
    {
        if (gated.Prerequisites == null)
        {
            typeof(ObjectDefinition)
                .GetProperty(nameof(ObjectDefinition.Prerequisites))!
#pragma warning disable CS8625
                .SetValue(gated, new ObjectPrerequisites());
#pragma warning restore CS8625
        }

        gated.Prerequisites!.Objects.Add(new ObjectPrerequisiteList
        {
            new LazyAssetReference<ObjectDefinition>(prerequisite),
        });
    }
}
