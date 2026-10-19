using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Panopticon.Logic.Object;
using Panopticon.Recording;
using Panopticon.Simulation;
using Xunit;

namespace Panopticon.Tests.Recording;

// WI-A5-21: regional T2 detail capture. Options route ring depth, window cap, cooldown, marked
// range, and region keys into TriggeredDetailCapture; detail is filtered through stable domain
// region keys and the manifest records trigger reason, budgets, region, and captured quantity.

[Collection("SerialGameTests")]
public class TriggeredDetailRegionalTests : MockedGameTest
{
    private static GameObject SpawnUnit(TestGame game, string name, Vector3 position, int ownerIndex = 0)
    {
        var definition = new ObjectDefinition(name);
        definition.KindOf.Set(ObjectKinds.Infantry, true);
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = 100f, InitialHealth = 100f },
                ModuleInheritanceMode.Default, inherited: false));
        var player = game.PlayerManager.GetPlayerByIndex((uint)ownerIndex);
        var obj = game.GameLogic.CreateObject(definition, player);
        obj.UpdateTransform(position, null);
        return obj;
    }

    private static (TestGame Game, SimulationHost Host) CreateHost()
    {
        var game = new TestGame(SageGame.CncGenerals);
        return (game, new SimulationHost(game.GameEngine, new RtsSimulationDomainModel(game.GameEngine)));
    }

    private static string ClassifyByX(WorldObjectState o) => o.Position.X < 100 ? "near" : "far";

    [Fact]
    public void Regional_FilterExcludesUnrelatedEntities()
    {
        var (game, host) = CreateHost();
        host.TickStarting += (_, e) =>
        {
            if (e.Frame == 2)
            {
                SpawnUnit(game, "A5T2NearA", new Vector3(50, 50, 0));
                SpawnUnit(game, "A5T2NearB", new Vector3(80, 50, 0));
                SpawnUnit(game, "A5T2FarA", new Vector3(250, 50, 0));
            }
        };

        var sampler = new WorldStateSampler(host, game);
        using var capture = new TriggeredDetailCapture(
            sampler,
            ringDepth: 10,
            regionClassifier: ClassifyByX,
            regionKeys: new[] { "near" });
        var t1 = new TieredSignatureRecorder(sampler, sampleInterval: 5);
        t1.ArmTrigger((frame, tree) => frame == 5 ? new[] { "world" } : null, capture);

        host.Run(8);

        var window = Assert.Single(capture.Complete());
        var addedNames = window.Frames.SelectMany(f => f.Added.Select(a => a.DefinitionName)).ToHashSet();
        Assert.Contains("A5T2NearA", addedNames);
        Assert.Contains("A5T2NearB", addedNames);
        Assert.DoesNotContain("A5T2FarA", addedNames);
        Assert.Equal(new[] { "near" }, window.RegionKeys);
    }

    [Fact]
    public void Cooldown_BoundsTripRate_AndMaxWindowsCaps()
    {
        var (game, host) = CreateHost();

        var sampler = new WorldStateSampler(host, game);
        using var capture = new TriggeredDetailCapture(
            sampler,
            ringDepth: 10,
            maxWindows: 2,
            cooldownFrames: 2);
        var t1 = new TieredSignatureRecorder(sampler, sampleInterval: 5);
        t1.ArmTrigger((frame, tree) => frame >= 3 ? new[] { "world" } : null, capture);

        host.Run(10);

        var windows = capture.Complete();
        Assert.Equal(2, windows.Count);
        Assert.Equal(3u, windows[0].TriggeredAtFrame);
        Assert.Equal(5u, windows[1].TriggeredAtFrame);
        Assert.Equal(2u, windows[0].CooldownFrames);
        Assert.Equal(2, windows[0].MaxWindows);
    }

    [Fact]
    public void MarkedWindow_RegionalProjection_RecordsReasonAndBudgets()
    {
        var (game, host) = CreateHost();
        host.TickStarting += (_, e) =>
        {
            if (e.Frame == 2)
            {
                SpawnUnit(game, "A5T2MarkNear", new Vector3(60, 50, 0));
                SpawnUnit(game, "A5T2MarkFar", new Vector3(260, 50, 0));
            }
        };

        var sampler = new WorldStateSampler(host, game);
        using var capture = TriggeredDetailCapture.Create(
            sampler,
            new TriggeredDetailCaptureOptions(
                RingDepth: 4,
                MaxWindows: 3,
                CooldownFrames: 9,
                MarkedStartFrame: 2,
                MarkedEndFrame: 5,
                RegionKeys: new[] { "near" }),
            ClassifyByX);

        host.Run(8);

        var window = Assert.Single(capture.Complete());
        Assert.True(window.Marked);
        Assert.Equal("marked", window.Reason);
        Assert.Equal(4, window.RingDepth);
        Assert.Equal(3, window.MaxWindows);
        Assert.Equal(9u, window.CooldownFrames);
        Assert.Equal(2u, window.StartFrame);
        Assert.Equal(5u, window.EndFrame);
        var addedNames = window.Frames.SelectMany(f => f.Added.Select(a => a.DefinitionName)).ToHashSet();
        Assert.Contains("A5T2MarkNear", addedNames);
        Assert.DoesNotContain("A5T2MarkFar", addedNames);
        Assert.Equal(1, window.CapturedEntityCount);
    }

    [Fact]
    public void Attach_RoutesOptionsIntoManifest()
    {
        var (game, host) = CreateHost();
        host.TickStarting += (_, e) =>
        {
            if (e.Frame == 2)
            {
                SpawnUnit(game, "A5T2AttachNear", new Vector3(50, 50, 0));
                SpawnUnit(game, "A5T2AttachFar", new Vector3(250, 50, 0));
            }
        };

        using var recording = RunRecording.Attach(
            host,
            game,
            seed: 77,
            detailOptions: new TriggeredDetailCaptureOptions(
                MarkedStartFrame: 1,
                MarkedEndFrame: 6,
                // RtsSimulationDomainModel buckets entities by 100-unit cells: cell:{x},{y}.
                RegionKeys: new[] { "cell:0,0" }));

        host.Run(8);

        var output = recording.Complete();
        var window = Assert.Single(output.TriggeredCaptures);
        Assert.True(window.Marked);
        Assert.Equal("marked", window.Reason);
        Assert.Equal(TriggeredDetailCapture.DefaultRingDepth, window.RingDepth);
        Assert.Equal(new[] { "cell:0,0" }, window.RegionKeys);
        Assert.Equal(1, window.CapturedEntityCount);
        var addedNames = window.Frames.SelectMany(f => f.Added.Select(a => a.DefinitionName)).ToHashSet();
        Assert.Contains("A5T2AttachNear", addedNames);
        Assert.DoesNotContain("A5T2AttachFar", addedNames);
    }
}
