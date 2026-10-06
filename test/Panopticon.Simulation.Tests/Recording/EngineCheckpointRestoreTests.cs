using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Panopticon.Data.Sav;
using Panopticon.Logic;
using Panopticon.Logic.Orders;
using Panopticon.Mathematics;
using Panopticon.Network;
using Panopticon.Recording;
using Panopticon.Simulation;
using Panopticon.Tests.Data;
using Xunit;
using Xunit.Abstractions;

namespace Panopticon.Tests.Recording;

[Collection("SerialGameTests")]
public sealed class EngineCheckpointRestoreTests : IDisposable
{
    private readonly ITestOutputHelper _output;

    public EngineCheckpointRestoreTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [GameFact(SageGame.CncGenerals)]
    public void Restore_MatchesUninterruptedRun_PerObjectBytes()
    {
        using var gameA = BootGame();
        var hostA = new SimulationHost(gameA.GameEngine, new RtsSimulationDomainModel(gameA.GameEngine));
        hostA.Run(30);
        hostA.InjectOrders(35, [Order.CreateMoveOrder(1, new System.Numerics.Vector3(100, 0, 100))]);

        var checkpoint = WriteAndReadCheckpoint(hostA, "run-restore");

        using var gameB = BootGame();
        var hostB = new SimulationHost(gameB.GameEngine, new RtsSimulationDomainModel(gameB.GameEngine));
        EngineCheckpointRestorer.Restore(hostB, checkpoint);

        Assert.Equal(hostA.LogicFrame.Value, hostB.LogicFrame.Value);
        var (frame, _, pending) = hostB.CaptureHostState();
        Assert.Equal(hostA.LogicFrame.Value, frame);
        Assert.True(pending.ContainsKey(35), "restored host must carry the pending frame-35 order schedule");

        hostA.Run(10);
        hostB.Run(10);
        Assert.Equal(hostA.LogicFrame.Value, hostB.LogicFrame.Value);

        var objectsA = PersistedObjects(gameA);
        var objectsB = PersistedObjects(gameB);
        Assert.Equal(objectsA.Keys.OrderBy(k => k), objectsB.Keys.OrderBy(k => k));
        foreach (var (key, bytes) in objectsA)
        {
            Assert.Equal(bytes, objectsB[key]);
        }

        _output.WriteLine($"restored {objectsB.Count} objects matching at frame {hostB.LogicFrame}");
    }

    [GameFact(SageGame.CncGenerals)]
    public void Restore_WithStateProjection_VerifiesDomainStateAndRejectsMismatch()
    {
        using var gameA = BootGame();
        var domainA = new ProjectedRtsDomain(new RtsSimulationDomainModel(gameA.GameEngine), gameA);
        var hostA = new SimulationHost(gameA.GameEngine, domainA);
        hostA.Run(50);

        var checkpoint = WriteAndReadCheckpoint(hostA, "run-projection");
        Assert.True(checkpoint.Sections.ContainsKey(EngineCheckpointData.DomainStateSection));
        Assert.False(checkpoint.Sections.ContainsKey(EngineCheckpointData.DomainRuntimeSection));

        using var gameB = BootGame();
        var domainB = new ProjectedRtsDomain(new RtsSimulationDomainModel(gameB.GameEngine), gameB);
        var hostB = new SimulationHost(gameB.GameEngine, domainB);
        EngineCheckpointRestorer.Restore(hostB, checkpoint);

        hostA.Run(1);
        hostB.Run(1);
        var objectsA = PersistedObjects(gameA);
        var objectsB = PersistedObjects(gameB);
        Assert.Equal(objectsA.Keys.OrderBy(k => k), objectsB.Keys.OrderBy(k => k));
        foreach (var (key, bytes) in objectsA)
        {
            Assert.Equal(bytes, objectsB[key]);
        }

        // A stale domain-state section (captured two frames later, so record keys differ
        // deterministically) must be rejected against the freshly restored state.
        hostA.Run(1);
        var staleSections = checkpoint.Sections.ToDictionary(kv => kv.Key, kv => kv.Value);
        staleSections[EngineCheckpointData.DomainStateSection] = EngineCheckpoint.CaptureDomainStateSection(hostA, domainA);
        var staleCheckpoint = new EngineCheckpointData(checkpoint.Header, staleSections);

        using var gameC = BootGame();
        var domainC = new ProjectedRtsDomain(new RtsSimulationDomainModel(gameC.GameEngine), gameC);
        var hostC = new SimulationHost(gameC.GameEngine, domainC);
        var ex = Record.Exception(() => EngineCheckpointRestorer.Restore(hostC, staleCheckpoint));
        Assert.IsType<InvalidDataException>(ex);
        Assert.Contains("domain-state", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Test double projecting engine objects into domain-state records. Record keys embed the
    /// frame so a stale section deterministically differs from freshly restored state; the
    /// transport-graph domain cannot serve here because its scenario objects use in-memory
    /// definitions that a restored game cannot resolve by name (WI-A1-24 scope).
    /// </summary>
    private sealed class ProjectedRtsDomain : ISimulationDomainModel, IDomainStateProjection
    {
        private readonly ISimulationDomainModel _inner;
        private readonly Game _game;

        public ProjectedRtsDomain(ISimulationDomainModel inner, Game game)
        {
            _inner = inner;
            _game = game;
        }

        public DomainStateSchema StateSchema { get; } = new(
            "rts-proof",
            1,
            [new DomainRecordKind("object", "world",
            [
                new DomainStateField("x", "world-units", DomainQuantityType.Real, DomainConservationRole.None, null),
                new DomainStateField("y", "world-units", DomainQuantityType.Real, DomainConservationRole.None, null),
                new DomainStateField("z", "world-units", DomainQuantityType.Real, DomainConservationRole.None, null),
            ], [])]);

        public DomainStateFrame CaptureState(uint frame)
        {
            var records = new List<DomainStateRecord>();
            foreach (var gameObject in _game.Scene3D.GameObjects.Objects)
            {
                if (gameObject == null)
                {
                    continue;
                }

                var translation = gameObject.Translation;
                records.Add(new DomainStateRecord
                {
                    Key = $"object:{frame}:{gameObject.Id.Index}",
                    Kind = "object",
                    Reals = [translation.X, translation.Y, translation.Z],
                });
            }

            records.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            var frameState = new DomainStateFrame { Frame = frame };
            frameState.Records.AddRange(records);
            return frameState;
        }

        public string DomainId => _inner.DomainId;

        public DomainFidelityPolicy Fidelity => _inner.Fidelity;

        public void Initialize(SimulationHost host) => _inner.Initialize(host);

        public void RunStarting() => _inner.RunStarting();

        public void RunComplete(in SimulationHostRunResult result) => _inner.RunComplete(in result);

        public IReadOnlyList<DomainEntitySummary> CaptureEntities() => _inner.CaptureEntities();

        public IReadOnlyList<uint> CapturePlayerMoneyVector() => _inner.CapturePlayerMoneyVector();

        public string TopologyKey(in DomainEntitySummary entity) => _inner.TopologyKey(in entity);

        public DomainTopologyDescription DescribeTopology() => _inner.DescribeTopology();

        public DomainMetricsSnapshot SampleMetrics() => _inner.SampleMetrics();

        public uint ResolveInteractionAuthority(in DomainInteraction interaction) => _inner.ResolveInteractionAuthority(in interaction);
    }

    private static EngineCheckpointData WriteAndReadCheckpoint(SimulationHost host, string runId)
    {
        var sections = EngineCheckpoint.CaptureSections(host);
        var header = new EngineCheckpointHeader(host.LogicFrame.Value, 0, runId);
        using var stream = new MemoryStream();
        EngineCheckpoint.Write(stream, header, sections);
        stream.Position = 0;
        return EngineCheckpoint.Read(stream);
    }

    private static Dictionary<uint, byte[]> PersistedObjects(Game game)
    {
        var result = new Dictionary<uint, byte[]>();
        foreach (var gameObject in game.Scene3D.GameObjects.Objects)
        {
            using var stream = new MemoryStream();
            using (var writer = new StateWriter(stream, game))
            {
                writer.PersistObject(gameObject, "Object");
            }

            result[gameObject.Id.Index] = stream.ToArray();
        }

        return result;
    }

    private Game BootGame()
    {
        var installation = InstalledFilesTestData.GetInstallation(SageGame.CncGenerals);
        Platform.Start();
        var game = new Game(installation, null, new Configuration
        {
            NoAudio = true,
            SkipStartingBuildings = true,
        }, null);
        var playerSettings = new PlayerSetting[]
        {
            new(0, "FactionAmerica", new ColorRgb(255, 0, 0), 0, PlayerOwner.Player, "USA"),
            new(1, "FactionChina", new ColorRgb(0, 255, 0), 0, PlayerOwner.EasyAi, "China"),
        };
        game.StartSkirmishOrMultiPlayerGame(
            "maps/alpine assault/alpine assault.map",
            new EchoConnection(),
            playerSettings,
            seed: 12345,
            isMultiPlayer: false);
        game.StartRun();
        game.VictorySystem.ConditionRule = new NeverDefeatedVictoryConditionRule();
        foreach (var player in game.Scene3D.Players)
        {
            player.DisableAI();
        }

        game.EnableFixedLogicTimestep();
        return game;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
