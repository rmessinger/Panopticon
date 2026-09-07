using System;
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

public sealed class NeuralNetworkAIDecisionPolicyTests : MockedGameTest
{
    [Fact]
    public void Constructor_WithWrongWeightCount_Throws()
    {
        Assert.Throws<System.ArgumentException>(() => new NeuralNetworkAIDecisionPolicy(new float[124]));
    }

    [Fact]
    public void ForwardPass_WithHandComputedWeights_ProducesExpectedLogits()
    {
        var weights = new float[NeuralNetworkAIDecisionPolicy.WeightCount];

        // Every hidden unit reads only feature 0: h_j = tanh(0.8).
        for (var j = 0; j < NeuralNetworkAIDecisionPolicy.HiddenSize; j++)
        {
            weights[j * NeuralNetworkAIDecisionPolicy.InputSize] = 0.8f;
        }

        // Output 2 (BuildNextStructure) reads every hidden unit at 0.25; all other weights are 0.
        var w2Offset = NeuralNetworkAIDecisionPolicy.InputSize * NeuralNetworkAIDecisionPolicy.HiddenSize
            + NeuralNetworkAIDecisionPolicy.HiddenSize;
        for (var j = 0; j < NeuralNetworkAIDecisionPolicy.HiddenSize; j++)
        {
            weights[w2Offset + 2 * NeuralNetworkAIDecisionPolicy.HiddenSize + j] = 0.25f;
        }

        var policy = new NeuralNetworkAIDecisionPolicy(weights);

        Span<float> features = stackalloc float[NeuralNetworkAIDecisionPolicy.InputSize];
        features.Clear();
        features[0] = 1f;

        Span<float> hidden = stackalloc float[NeuralNetworkAIDecisionPolicy.HiddenSize];
        Span<float> logits = stackalloc float[NeuralNetworkAIDecisionPolicy.OutputSize];
        policy.ForwardPass(features, hidden, logits);

        var expectedLogit2 = NeuralNetworkAIDecisionPolicy.HiddenSize * 0.25f * MathF.Tanh(0.8f);
        Assert.Equal(expectedLogit2, logits[2], 5);
        for (var k = 0; k < NeuralNetworkAIDecisionPolicy.OutputSize; k++)
        {
            if (k != 2)
            {
                Assert.Equal(0f, logits[k], 5);
            }
        }

        Assert.Equal(2, ArgMax(logits));
    }

    [Fact]
    public void DecideNextOrders_ZeroWeights_TiesResolveToNoneAndEmitNoOrders()
    {
        var player = CreateSkirmishAIPlayer(out _);
        var policy = new NeuralNetworkAIDecisionPolicy(new float[NeuralNetworkAIDecisionPolicy.WeightCount]);

        var orders = policy.DecideNextOrders(new AIDecisionContext(player, 3, 1));

        Assert.Empty(orders);
    }

    [Fact]
    public void EncodeState_OnControlledState_ProducesExpectedFeatures()
    {
        var player = CreateSkirmishAIPlayer(out _);
        player.BankAccount.Money = 19_999u;

        var policy = new NeuralNetworkAIDecisionPolicy(new float[NeuralNetworkAIDecisionPolicy.WeightCount]);

        Span<float> features = stackalloc float[NeuralNetworkAIDecisionPolicy.InputSize];
        policy.EncodeState(new AIDecisionContext(player, 3, 1), features);

        Assert.InRange(features[0], 0.99f, 1f);
        Assert.Equal(0f, features[1]);
        Assert.Equal(0f, features[2]);
        Assert.Equal(0f, features[3]);
        Assert.Equal(0f, features[4]);
        Assert.Equal(0f, features[5]);
        Assert.Equal(0f, features[6]);
        Assert.Equal(0f, features[7]);
        Assert.Equal(0f, features[8]);
    }

    [Fact]
    public void SkirmishAIPlayer_WithNetworkPolicySelectingSupplyBuild_EmitsBuildObjectOrder()
    {
        var testDefinition = new ObjectDefinition("NNTestSupplyCenter");
        testDefinition.KindOf.Set(ObjectKinds.Structure, true);
        Generals.AssetStore.ObjectDefinitions.Add(testDefinition);

        var buildInfo = CreateBuildListInfo(testDefinition.Name, isSupplyBuilding: true);
        Generals.AssetStore.AIData.Current.SkirmishBuildLists.Add(
            CreateBuildList("FactionGLA", [buildInfo]));

        var player = CreateSkirmishAIPlayer(out var aiPlayerIndex);

        // Zero network except an output bias on action 1 (BuildSupplyPriority): argmax is
        // decided by the bias regardless of state encoding.
        var weights = new float[NeuralNetworkAIDecisionPolicy.WeightCount];
        var b2Offset = NeuralNetworkAIDecisionPolicy.InputSize * NeuralNetworkAIDecisionPolicy.HiddenSize
            + NeuralNetworkAIDecisionPolicy.HiddenSize
            + NeuralNetworkAIDecisionPolicy.HiddenSize * NeuralNetworkAIDecisionPolicy.OutputSize;
        weights[b2Offset + 1] = 5f;

        var aiPlayer = new SkirmishAIPlayer(
            player,
            new NeuralNetworkAIDecisionPolicy(weights));

        for (var i = 0; i < (int)AIPlayer.DefaultDecisionCadenceFrames; i++)
        {
            aiPlayer.Update();
        }

        Assert.Contains(
            Generals.InjectedOrders,
            x => x.OrderType == OrderType.BuildObject && x.PlayerIndex == aiPlayerIndex);
    }

    private static int ArgMax(ReadOnlySpan<float> logits)
    {
        var best = 0;
        for (var k = 1; k < logits.Length; k++)
        {
            if (logits[k] > logits[best])
            {
                best = k;
            }
        }

        return best;
    }

    private Player CreateSkirmishAIPlayer(out int aiPlayerIndex)
    {
        var manager = new PlayerManager(Generals);
        RegisterPlayerTemplates();
        manager.OnNewGame(CreateMapPlayers(), GameType.Skirmish, CreatePlayerSettings());

        var player = manager.Players[3];
        aiPlayerIndex = 3;
        return player;
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
            BindingFlags.Instance | BindingFlags.NonPublic);
        setNameAndInstanceId!.Invoke(template, new object[] { "PlayerTemplate", name });
        return template;
    }

    private static AISkirmishBuildList CreateBuildList(string name, System.Collections.Generic.IReadOnlyList<BuildListInfo> structures)
    {
        var result = new AISkirmishBuildList();
        SetProperty(result, nameof(AISkirmishBuildList.Name), name);

        foreach (var structure in structures)
        {
            result.Structures.Add(structure);
        }

        return result;
    }

    private static BuildListInfo CreateBuildListInfo(string templateName, bool isSupplyBuilding)
    {
        var result = new BuildListInfo();
        SetProperty(result, nameof(BuildListInfo.TemplateName), templateName);
        SetProperty(result, nameof(BuildListInfo.IsSupplyBuilding), isSupplyBuilding);
        SetProperty(result, nameof(BuildListInfo.PriorityBuild), true);
        SetProperty(result, nameof(BuildListInfo.NumRebuilds), 1u);
        SetProperty(result, nameof(BuildListInfo.Location), new Vector3(10, 20, 0));

        return result;
    }

    private static void SetProperty(object instance, string propertyName, object value)
    {
        var property = instance.GetType().GetProperty(
            propertyName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        property!.SetValue(instance, value);
    }
}
