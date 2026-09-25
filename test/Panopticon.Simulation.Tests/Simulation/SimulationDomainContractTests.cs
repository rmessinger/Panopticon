using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Recording;
using Panopticon.Simulation;
using Panopticon.Simulation.Transport;
using Xunit;

namespace Panopticon.Tests.Simulation;

/// <summary>
/// Decorates a domain model, recording lifecycle calls without changing behavior: proves the
/// host's documented callback order for every domain through identical caller code.
/// </summary>
public sealed class LifecycleRecordingDomain : ISimulationDomainModel
{
    public ISimulationDomainModel Inner { get; }

    public List<string> Calls { get; } = new();

    public string DomainId => Inner.DomainId;

    public DomainFidelityPolicy Fidelity => Inner.Fidelity;

    public LifecycleRecordingDomain(ISimulationDomainModel inner)
    {
        Inner = inner;
    }

    public void Initialize(SimulationHost host)
    {
        Calls.Add("initialize");
        Inner.Initialize(host);
    }

    public void RunStarting()
    {
        Calls.Add("run-starting");
        Inner.RunStarting();
    }

    public void RunComplete(in SimulationHostRunResult result)
    {
        Calls.Add("run-complete");
        Inner.RunComplete(in result);
    }

    public IReadOnlyList<DomainEntitySummary> CaptureEntities() => Inner.CaptureEntities();

    public IReadOnlyList<uint> CapturePlayerMoneyVector() => Inner.CapturePlayerMoneyVector();

    public string TopologyKey(in DomainEntitySummary entity) => Inner.TopologyKey(in entity);

    public DomainTopologyDescription DescribeTopology() => Inner.DescribeTopology();

    public DomainMetricsSnapshot SampleMetrics() => Inner.SampleMetrics();

    public uint ResolveInteractionAuthority(in DomainInteraction interaction) =>
        Inner.ResolveInteractionAuthority(in interaction);
}

/// <summary>
/// WI-A4-10: locks the A4-1 milestone. One caller path drives RTS and transport/graph domains
/// through the same host-facing API; each domain's own outputs (metrics, topology keys) are
/// asserted without comparing the domains to each other.
/// </summary>
public sealed class SimulationDomainContractTests : MockedGameTest
{
    private sealed record DomainCase(
        string DomainId,
        Func<TestGame> CreateGame,
        Func<TestGame, ISimulationDomainModel> CreateDomain,
        Func<ISimulationDomainModel, Func<SimulationHost, bool>> CreateStopPredicate);

    [Fact]
    public void RtsDomain_SatisfiesHostContract()
    {
        RunContractCase(new DomainCase(
            "rts",
            () =>
            {
                var game = new TestGame(SageGame.CncGenerals);
                var definition = new ObjectDefinition("A4ContractUnit");
                definition.KindOf.Set(ObjectKinds.Infantry, true);
                var obj = game.GameLogic.CreateObject(definition, game.PlayerManager.GetPlayerByIndex(0u));
                obj.UpdateTransform(new Vector3(50, 50, 0), null);
                return game;
            },
            game => new RtsSimulationDomainModel(game.GameEngine),
            _ => host => host.Frame >= 35));
    }

    [Fact]
    public void TransportGraphDomain_SatisfiesHostContract()
    {
        RunContractCase(new DomainCase(
            "transport-graph",
            () => new TestGame(SageGame.CncGenerals),
            game => TransportGraphDomainModel.CreateDefault(game.GameEngine),
            domain => host => ((TransportGraphDomainModel)domain).AllRoutesComplete));
    }

    private void RunContractCase(DomainCase testCase)
    {
        var game = testCase.CreateGame();
        var innerDomain = testCase.CreateDomain(game);
        var domain = new LifecycleRecordingDomain(innerDomain);
        var host = new SimulationHost(game.GameEngine, domain);
        var stopPredicate = testCase.CreateStopPredicate(innerDomain);

        using var recording = RunRecording.Attach(host, game, seed: 11u, scenarioId: $"a4-contract-{testCase.DomainId}", sampleInterval: 10);
        var result = host.Run(600, stopPredicate);
        var output = recording.Complete();

        // Lifecycle completion, in the documented order.
        Assert.True(result.StopPredicateTriggered);
        Assert.True(result.FramesCompleted > 0);
        Assert.Equal(["initialize", "run-starting", "run-complete"], domain.Calls);

        // The run output is portable and identifies its domain.
        Assert.Equal(testCase.DomainId, output.DomainId);
        var path = Path.Combine(Path.GetTempPath(), $"a4-contract-{testCase.DomainId}-{Guid.NewGuid():N}.json");
        try
        {
            output.SaveToFile(path);
            var reloaded = RunOutput.LoadFromFile(path);
            Assert.Equal(testCase.DomainId, reloaded!.DomainId);
        }
        finally
        {
            File.Delete(path);
        }

        // Each domain emits its documented metrics: every sample has rows.
        Assert.True(output.MetricSamples.Count > 0);
        Assert.All(output.MetricSamples, s =>
            Assert.True(s.Players.Count > 0 || s.DomainMetrics.Count > 0));

        // Topology keys: sampled trees carry domain bucket keys (non-root nodes with the
        // domain's stable key shape).
        Assert.True(output.SignatureTrees.Count > 0);
        Assert.All(output.SignatureTrees, t =>
        {
            var buckets = t.Nodes.Where(n => n.Level == 1).ToList();
            Assert.True(buckets.Count > 0);
            Assert.All(buckets, b => Assert.Contains(':', b.Key));
        });
    }

    [Fact]
    public void SimulationHost_NeverConstructsAConcreteDomainAdapter()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "Panopticon.Simulation", "SimulationHost.cs")))
        {
            dir = dir.Parent;
        }

        var hostSource = dir is null
            ? File.ReadAllText("src/Panopticon.Simulation/SimulationHost.cs")
            : File.ReadAllText(Path.Combine(dir.FullName, "src", "Panopticon.Simulation", "SimulationHost.cs"));

        Assert.DoesNotContain("RtsSimulationDomainModel", hostSource);
        Assert.DoesNotContain("TransportGraphDomainModel", hostSource);
    }
}
