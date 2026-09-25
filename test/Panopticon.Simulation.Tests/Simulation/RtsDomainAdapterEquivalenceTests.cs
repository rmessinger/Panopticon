using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Recording;
using Panopticon.Simulation;
using Xunit;

namespace Panopticon.Tests.Simulation;

/// <summary>
/// WI-A4-08 equivalence: the RTS adapter reproduces the pre-seam recording projections
/// exactly (entity iteration, cell bucket keys, T0 metric rows, initiator authority).
/// </summary>
public sealed class RtsDomainAdapterEquivalenceTests : MockedGameTest
{
    private static GameObject SpawnUnit(TestGame game, string name, Vector3 position)
    {
        var definition = new ObjectDefinition(name);
        definition.KindOf.Set(ObjectKinds.Infantry, true);
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = 100f, InitialHealth = 80f },
                ModuleInheritanceMode.Default, inherited: false));
        var player = game.PlayerManager.GetPlayerByIndex(0u);
        var obj = game.GameLogic.CreateObject(definition, player);
        obj.UpdateTransform(position, null);
        return obj;
    }

    private static (TestGame Game, SimulationHost Host) CreateRtsHost()
    {
        var game = new TestGame(SageGame.CncGenerals);
        var host = new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine));
        return (game, host);
    }

    [Fact]
    public void CaptureEntities_MapsGameObjects_InIdOrder()
    {
        var (game, host) = CreateRtsHost();
        SpawnUnit(game, "A4EquivUnitA", new Vector3(50, 50, 0));
        SpawnUnit(game, "A4EquivUnitB", new Vector3(250, 130, 0));

        var entities = host.DomainModel.CaptureEntities();

        Assert.Equal(2, entities.Count);
        Assert.Equal(
            game.GameLogic.Objects.Select(o => o.Id.Index),
            entities.Select(e => e.EntityId));
        var first = entities[0];
        Assert.Equal("A4EquivUnitA", first.KindName);
        Assert.Equal(0, first.OwnerIndex);
        Assert.Equal(new Vector3(50, 50, 0), first.Position);
        Assert.Equal(80f, first.ScalarA);
        Assert.Equal(100f, first.ScalarB);
    }

    [Fact]
    public void TopologyKey_MatchesLegacyCellBucketKeys()
    {
        var (game, host) = CreateRtsHost();
        var position = new Vector3(250, 130, 0);

        var entity = new DomainEntitySummary(1, "U", 0, position, 1f, 1f);

        Assert.Equal(
            SignatureTreeBuilder.RtsCellKey(position, SignatureTreeBuilder.DefaultCellSize),
            host.DomainModel.TopologyKey(entity));
        Assert.Equal("cell:2,1", host.DomainModel.TopologyKey(entity));
    }

    [Fact]
    public void DomainKeyedSignatureTree_IsIdenticalToLegacyCellTree()
    {
        var (game, host) = CreateRtsHost();
        SpawnUnit(game, "A4TreeUnitA", new Vector3(50, 50, 0));
        SpawnUnit(game, "A4TreeUnitB", new Vector3(150, 50, 0));
        SpawnUnit(game, "A4TreeUnitC", new Vector3(1050, 2050, 0));

        var summary = new WorldStateSummary();
        foreach (var entity in host.DomainModel.CaptureEntities())
        {
            summary.Objects.Add(new WorldObjectState
            {
                ObjectId = entity.EntityId,
                DefinitionName = entity.KindName,
                OwnerIndex = entity.OwnerIndex,
                Position = entity.Position,
                Health = entity.ScalarA,
                MaxHealth = entity.ScalarB,
            });
        }

        var legacy = SignatureTreeBuilder.Build(7, summary);
        var domainKeyed = SignatureTreeBuilder.Build(7, summary, host.DomainModel);

        Assert.Equal(legacy.Nodes.Count, domainKeyed.Nodes.Count);
        for (var i = 0; i < legacy.Nodes.Count; i++)
        {
            Assert.Equal(legacy.Nodes[i].Key, domainKeyed.Nodes[i].Key);
            Assert.Equal(legacy.Nodes[i].Hash, domainKeyed.Nodes[i].Hash);
            Assert.Equal(legacy.Nodes[i].MemberCount, domainKeyed.Nodes[i].MemberCount);
        }
    }

    [Fact]
    public void SampleMetrics_MatchesDirectPlayerManagerReads()
    {
        var (game, host) = CreateRtsHost();

        var metrics = host.DomainModel.SampleMetrics();

        Assert.Equal(game.PlayerManager.Players.Count, metrics.Players.Count);
        foreach (var (row, player) in metrics.Players.Zip(game.PlayerManager.Players))
        {
            Assert.Equal(player.DisplayName, row.Player);
            Assert.Equal(player.BankAccount.Money, row.Money);
            Assert.Equal(player.OwnedObjects.Count, row.OwnedObjects);
        }
    }

    [Fact]
    public void ResolveInteractionAuthority_ReturnsInitiator()
    {
        var (game, host) = CreateRtsHost();

        var authority = host.DomainModel.ResolveInteractionAuthority(
            new DomainInteraction(11u, 22u, "cell:0,0"));

        Assert.Equal(11u, authority);
    }

    [Fact]
    public void RunRecording_ThroughDomain_RecordsRtsDomainIdAndPlayerRows()
    {
        var (game, host) = CreateRtsHost();

        using var recording = RunRecording.Attach(host, game, seed: 42u, scenarioId: "a4-equiv");
        host.Run(35);
        var output = recording.Complete();

        Assert.Equal("rts", output.DomainId);
        Assert.True(output.MetricSamples.Count > 0);
        foreach (var sample in output.MetricSamples)
        {
            Assert.Equal(game.PlayerManager.Players.Count, sample.Players.Count);
            Assert.Empty(sample.DomainMetrics);
        }

        Assert.Contains(output.SignatureTrees, t => t.Nodes.Count >= 1 && t.Nodes[0].Key == "world");
    }
}
