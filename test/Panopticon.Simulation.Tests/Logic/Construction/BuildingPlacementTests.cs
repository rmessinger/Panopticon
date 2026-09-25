using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Logic.Orders;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.Logic.Construction;

// WI-P5-01: Player and AI building placement.
// Verifies the shared OrderProcessor.BuildObject path for both human and AI players:
// cost debit, structure creation at the requested position, builder assignment via
// IBuilderAIUpdate, and construction progressing to completion (BuildProgress >= 1.0).
//
// Run with: dotnet test src --filter "FullyQualifiedName~BuildingPlacementTests"

public class BuildingPlacementTests : MockedGameTest
{
    private const int HumanPlayerIdx = 2;
    private const int AiPlayerIdx = 3;
    private const uint StartingMoney = 10_000u;
    private const float StructureCost = 500f;
    private static readonly Vector3 BuildPosition = new(50, 50, 0);

    [Fact, Trait("Category", "Roadmap")]
    public void BuildObject_HumanPlayer_DebitsCost_CreatesStructure_AssignsBuilder()
    {
        ConfigureTwoPlayerSkirmish();
        var player = Generals.PlayerManager.GetPlayerByIndex(HumanPlayerIdx);
        player.BankAccount.Money = StartingMoney;

        var structureDef = NewStructureDefinition("P5PlacementStructure_Human", StructureCost);
        Generals.AssetStore.ObjectDefinitions.Add(structureDef);

        var dozer = CreateDozer(player, "P5PlacementDozer_Human");

        var order = Order.CreateBuildObject(HumanPlayerIdx, (int)structureDef.InstanceId, BuildPosition, 0f);
        new OrderProcessor(Generals).Process([order]);

        Assert.Equal(StartingMoney - (uint)StructureCost, player.BankAccount.Money);

        var structure = Generals.GameLogic.Objects.SingleOrDefault(o => o.Definition == structureDef);
        Assert.NotNull(structure);
        Assert.Equal(BuildPosition with { Z = structure!.Transform.Translation.Z }, structure.Transform.Translation);
        Assert.Same(player, structure.Owner);

        var builder = dozer.AIUpdate as IBuilderAIUpdate;
        Assert.NotNull(builder);
        Assert.Same(structure, builder!.BuildTarget);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void BuildObject_AiPlayer_DebitsCost_CreatesStructure_AssignsBuilder()
    {
        ConfigureTwoPlayerSkirmish();
        var player = Generals.PlayerManager.GetPlayerByIndex(AiPlayerIdx);
        player.BankAccount.Money = StartingMoney;

        var structureDef = NewStructureDefinition("P5PlacementStructure_AI", StructureCost);
        Generals.AssetStore.ObjectDefinitions.Add(structureDef);

        var dozer = CreateDozer(player, "P5PlacementDozer_AI");

        var order = Order.CreateBuildObject(AiPlayerIdx, (int)structureDef.InstanceId, BuildPosition, 0f);
        new OrderProcessor(Generals).Process([order]);

        Assert.Equal(StartingMoney - (uint)StructureCost, player.BankAccount.Money);

        var structure = Generals.GameLogic.Objects.SingleOrDefault(o => o.Definition == structureDef);
        Assert.NotNull(structure);
        Assert.Same(player, structure!.Owner);

        var builder = dozer.AIUpdate as IBuilderAIUpdate;
        Assert.NotNull(builder);
        Assert.Same(structure, builder!.BuildTarget);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void BuildObject_BothPlayers_SameOrderPath_NoDirectConstructionShortcut()
    {
        ConfigureTwoPlayerSkirmish();
        var human = Generals.PlayerManager.GetPlayerByIndex(HumanPlayerIdx);
        var ai = Generals.PlayerManager.GetPlayerByIndex(AiPlayerIdx);
        human.BankAccount.Money = StartingMoney;
        ai.BankAccount.Money = StartingMoney;

        var humanDef = NewStructureDefinition("P5Both_Human", StructureCost);
        var aiDef = NewStructureDefinition("P5Both_AI", StructureCost);
        Generals.AssetStore.ObjectDefinitions.Add(humanDef);
        Generals.AssetStore.ObjectDefinitions.Add(aiDef);

        CreateDozer(human, "P5BothDozer_Human");
        CreateDozer(ai, "P5BothDozer_AI");

        new OrderProcessor(Generals).Process([
            Order.CreateBuildObject(HumanPlayerIdx, (int)humanDef.InstanceId, BuildPosition, 0f),
            Order.CreateBuildObject(AiPlayerIdx, (int)aiDef.InstanceId, BuildPosition with { X = 100 }, 0f),
        ]);

        Assert.Equal(StartingMoney - (uint)StructureCost, human.BankAccount.Money);
        Assert.Equal(StartingMoney - (uint)StructureCost, ai.BankAccount.Money);

        Assert.Single(Generals.GameLogic.Objects, o => o.Definition == humanDef);
        Assert.Single(Generals.GameLogic.Objects, o => o.Definition == aiDef);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void BuildObject_CancelBuild_RefundsCost()
    {
        ConfigureTwoPlayerSkirmish();
        var player = Generals.PlayerManager.GetPlayerByIndex(HumanPlayerIdx);
        player.BankAccount.Money = StartingMoney;

        var structureDef = NewStructureDefinition("P5Cancel_Structure", StructureCost);
        Generals.AssetStore.ObjectDefinitions.Add(structureDef);

        var dozer = CreateDozer(player, "P5Cancel_Dozer");

        new OrderProcessor(Generals).Process([
            Order.CreateBuildObject(HumanPlayerIdx, (int)structureDef.InstanceId, BuildPosition, 0f),
        ]);

        Assert.Equal(StartingMoney - (uint)StructureCost, player.BankAccount.Money);

        var structure = Generals.GameLogic.Objects.Single(o => o.Definition == structureDef);
        MarkSelected(player, structure);

        new OrderProcessor(Generals).Process([
            new Order(HumanPlayerIdx, OrderType.CancelBuild),
        ]);

        Assert.Equal(StartingMoney, player.BankAccount.Money);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void BuildObject_ConstructionAdvancesToCompletion()
    {
        ConfigureTwoPlayerSkirmish();
        var player = Generals.PlayerManager.GetPlayerByIndex(HumanPlayerIdx);
        player.BankAccount.Money = StartingMoney;

        var structureDef = NewStructureDefinition("P5Complete_Structure", StructureCost, buildTimeFrames: 3);
        Generals.AssetStore.ObjectDefinitions.Add(structureDef);

        CreateDozer(player, "P5Complete_Dozer");

        new OrderProcessor(Generals).Process([
            Order.CreateBuildObject(HumanPlayerIdx, (int)structureDef.InstanceId, BuildPosition, 0f),
        ]);

        var structure = Generals.GameLogic.Objects.Single(o => o.Definition == structureDef);

        Assert.True(structure.TestStatus(ObjectStatus.UnderConstruction));
        Assert.True(structure.BuildProgress < 1.0f);

        for (var i = 0; i < 3; i++)
        {
            structure.AdvanceConstruction();
        }

        Assert.True(structure.BuildProgress >= 1.0f);
        Assert.False(structure.TestStatus(ObjectStatus.UnderConstruction));
    }

    private void ConfigureTwoPlayerSkirmish()
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
                new Panopticon.Data.Map.Player
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

    private static ObjectDefinition NewStructureDefinition(string name, float buildCost, uint buildTimeFrames = 10)
    {
        var definition = new ObjectDefinition(name);
        definition.KindOf.Set(ObjectKinds.Structure, true);
        typeof(ObjectDefinition)
            .GetProperty(nameof(ObjectDefinition.BuildCost))!
            .SetValue(definition, buildCost);
        typeof(ObjectDefinition)
            .GetProperty(nameof(ObjectDefinition.BuildTime))!
            .SetValue(definition, new LogicFrameSpan(buildTimeFrames));

        var bodyData = new ActiveBodyModuleData
        {
            MaxHealth = 1000f,
            InitialHealth = 1000f,
        };
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body", bodyData, ModuleInheritanceMode.Default, inherited: false));

        return definition;
    }

    private GameObject CreateDozer(Player owner, string name)
    {
        var definition = new ObjectDefinition(name);
        definition.KindOf.Set(ObjectKinds.Dozer, true);
        definition.KindOf.Set(ObjectKinds.Vehicle, true);
        definition.Behaviors.Add("ModuleTag_DozerAI",
            new ModuleDataContainer("ModuleTag_DozerAI", new DozerAIUpdateModuleData(),
                ModuleInheritanceMode.Default, inherited: false));

        var dozer = Generals.GameLogic.CreateObject(definition, owner);
        dozer.UpdateTransform(new Vector3(40, 40, 0), Quaternion.Identity);
        return dozer;
    }

    private static void MarkSelected(Player owner, params GameObject[] units)
    {
        var field = typeof(Player).GetField("_selectedUnits", BindingFlags.Instance | BindingFlags.NonPublic);
        var set = (HashSet<GameObject>)field!.GetValue(owner)!;
        set.UnionWith(units);
    }
}
