using System;
using System.IO;
using System.Linq;
using Panopticon.Logic;
using Panopticon.Logic.AI;
using Panopticon.Logic.AI.Hierarchy;
using Panopticon.Mathematics;
using Xunit;

namespace Panopticon.Tests.Logic.AI;

public sealed class AIDecisionPolicyFactoryTests : MockedGameTest
{
    [Fact]
    public void Create_RuleBased_ReturnsReferencePolicy()
    {
        Assert.IsType<RuleBasedAIDecisionPolicy>(AIDecisionPolicyFactory.Create(AIDecisionPolicyType.RuleBased));
    }

    [Fact]
    public void Create_NeuralNetwork_ReturnsNetworkPolicy()
    {
        var weights = new float[NeuralNetworkAIDecisionPolicy.WeightCount];

        Assert.IsType<NeuralNetworkAIDecisionPolicy>(
            AIDecisionPolicyFactory.Create(AIDecisionPolicyType.NeuralNetwork, weights));
    }

    [Fact]
    public void Create_NeuralNetworkWithoutWeights_Throws()
    {
        Assert.Throws<System.ArgumentException>(
            () => AIDecisionPolicyFactory.Create(AIDecisionPolicyType.NeuralNetwork));
    }

    [Fact]
    public void Create_NullPayload_ReturnsReferencePolicy()
    {
        Assert.IsType<RuleBasedAIDecisionPolicy>(AIDecisionPolicyFactory.Create(null));
    }

    [Fact]
    public void Create_PayloadWithNeuralWeights_ReturnsNetworkPolicy()
    {
        var payload = new AIDecisionPolicyPayload
        {
            Type = AIDecisionPolicyType.NeuralNetwork,
            NeuralNetworkWeights = new float[NeuralNetworkAIDecisionPolicy.WeightCount],
        };

        Assert.IsType<NeuralNetworkAIDecisionPolicy>(AIDecisionPolicyFactory.Create(payload));
    }

    [Fact]
    public void Create_PayloadNeuralWithoutWeights_ThrowsExplicitly()
    {
        var payload = new AIDecisionPolicyPayload { Type = AIDecisionPolicyType.NeuralNetwork };

        Assert.Throws<ArgumentException>(() => AIDecisionPolicyFactory.Create(payload));
    }

    [Fact]
    public void Create_PayloadNeuralWithWrongWeightCount_ThrowsExplicitly()
    {
        var payload = new AIDecisionPolicyPayload
        {
            Type = AIDecisionPolicyType.NeuralNetwork,
            NeuralNetworkWeights = new float[124],
        };

        Assert.Throws<ArgumentException>(() => AIDecisionPolicyFactory.Create(payload));
    }

    [Fact]
    public void NeuralWeights_AreClonedAtConstruction_NotAliased()
    {
        var weights = new float[NeuralNetworkAIDecisionPolicy.WeightCount];
        var first = new NeuralNetworkAIDecisionPolicy(weights);

        // Mutate the first output bias: logits[0] is exactly that bias, so an aliased
        // weights array would change the already-constructed policy's forward pass.
        weights[120] = 999f;
        var second = new NeuralNetworkAIDecisionPolicy(weights);

        Span<float> features = stackalloc float[NeuralNetworkAIDecisionPolicy.InputSize];
        Span<float> hidden = stackalloc float[NeuralNetworkAIDecisionPolicy.HiddenSize];
        Span<float> firstLogits = stackalloc float[NeuralNetworkAIDecisionPolicy.OutputSize];
        Span<float> secondLogits = stackalloc float[NeuralNetworkAIDecisionPolicy.OutputSize];
        features.Clear();
        features[0] = 1f;

        first.ForwardPass(features, hidden, firstLogits);
        second.ForwardPass(features, hidden, secondLogits);

        Assert.NotEqual(firstLogits[0], secondLogits[0]);
    }

    [Fact]
    public void NeuralPayload_ReachesSkirmishAIPlayerThroughPlayerManager()
    {
        var manager = new PlayerManager(Generals);
        RegisterPlayerTemplates();
        var settings = CreatePlayerSettings(humanIsLocal: true);
        settings[1].AIPolicy = new AIDecisionPolicyPayload
        {
            Type = AIDecisionPolicyType.NeuralNetwork,
            NeuralNetworkWeights = new float[NeuralNetworkAIDecisionPolicy.WeightCount],
        };
        manager.OnNewGame(CreateMapPlayers(), GameType.Skirmish, settings);

        var aiPlayer = Assert.IsType<SkirmishAIPlayer>(manager.Players[3].AIPlayer);
        Assert.IsType<NeuralNetworkAIDecisionPolicy>(aiPlayer.DecisionPolicy);
    }

    [Fact]
    public void Create_Hierarchical_ComposesRuleBasedRanks()
    {
        var policy = Assert.IsType<HierarchicalAIDecisionPolicy>(
            AIDecisionPolicyFactory.Create(new AIDecisionPolicyPayload { Type = AIDecisionPolicyType.Hierarchical }));

        Assert.NotNull(policy.Hierarchy);
    }

    [Fact]
    public void HierarchicalSelection_WiresHierarchyOntoTheSkirmishHost()
    {
        var manager = new PlayerManager(Generals);
        RegisterPlayerTemplates();
        var settings = CreatePlayerSettings(humanIsLocal: true);
        settings[1].AIPolicy = new AIDecisionPolicyPayload { Type = AIDecisionPolicyType.Hierarchical };
        manager.OnNewGame(CreateMapPlayers(), GameType.Skirmish, settings);

        var aiPlayer = Assert.IsType<SkirmishAIPlayer>(manager.Players[3].AIPlayer);
        Assert.IsType<HierarchicalAIDecisionPolicy>(aiPlayer.DecisionPolicy);
        Assert.NotNull(aiPlayer.Hierarchy);
    }

    [Fact]
    public void DefaultSlotConfig_YieldsRuleBasedPolicyForSkirmishAI()
    {
        var manager = new PlayerManager(Generals);
        RegisterPlayerTemplates();
        manager.OnNewGame(CreateMapPlayers(), GameType.Skirmish, CreatePlayerSettings(humanIsLocal: true));

        var aiPlayer = Assert.IsType<SkirmishAIPlayer>(manager.Players[3].AIPlayer);
        Assert.IsType<RuleBasedAIDecisionPolicy>(aiPlayer.DecisionPolicy);
    }

    [Fact]
    public void Factory_IsOnlyNonTestConstructorOfPolicyImplementations()
    {
        var gameSourceRoot = FindGameSourceRoot();
        var offenders =
            Directory.GetFiles(gameSourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(path => !Path.GetFileName(path).Equals("AIDecisionPolicyFactory.cs"))
            .Select(path => new { Path = path, Content = File.ReadAllText(path) })
            .Where(file => file.Content.Contains("new RuleBasedAIDecisionPolicy(")
                || file.Content.Contains("new NeuralNetworkAIDecisionPolicy(")
                || file.Content.Contains("new HierarchicalAIDecisionPolicy("))
            .Select(file => Path.GetFileName(file.Path))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Policy implementations must only be constructed by AIDecisionPolicyFactory; found constructions in: {string.Join(", ", offenders)}");
    }

    private static string FindGameSourceRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Panopticon.Simulation");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return "src/Panopticon.Simulation";
    }

    private static Panopticon.Data.Map.Player[] CreateMapPlayers() =>
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
    ];

    private static PlayerSetting[] CreatePlayerSettings(bool humanIsLocal) =>
    [
        new PlayerSetting(1, "FactionAmerica", new ColorRgb(255, 255, 255), 1, PlayerOwner.Player, "human", humanIsLocal),
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
