// WI-N6-01: movement progress tracking, blocker attribution through the ground-footprint gate,
// and the stall sampler's wait-for cycles. Detection only: the state machine counts frames
// without waypoint progress, records the blocking unit, and flags awaiting/no-route orders.
//
// Run with: dotnet test src --filter "FullyQualifiedName~MovementProgress"

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using Panopticon.Logic;
using Panopticon.Logic.Object;
using Panopticon.Navigation;
using Panopticon.Tools.TestHarness;
using Xunit;
using NavigationClass = Panopticon.Navigation.Navigation;

namespace Panopticon.Tests.Navigation;

// Shares the global test game with the footprint suite (units are created on Generals).
[Collection("GroundFootprint")]
public class MovementProgressTests : MockedGameTest
{
    [Fact, Trait("Category", "Roadmap")]
    public void RecordMoveStep_TracksProgressBlockedAndRecovery()
    {
        var progress = new MovementProgress();
        var blocker = new ObjectId(7);

        progress.RecordMoveStep(100.0f, blockedByUnit: false, blocker: default);
        Assert.Equal(0, progress.FramesWithoutProgress);
        Assert.Equal(NavigationStallReason.None, progress.Reason);

        progress.RecordMoveStep(90.0f, blockedByUnit: false, blocker: default);
        Assert.Equal(0, progress.FramesWithoutProgress);

        progress.RecordMoveStep(90.0f, blockedByUnit: true, blocker: blocker);
        Assert.Equal(1, progress.FramesWithoutProgress);
        Assert.Equal(NavigationStallReason.BlockedByUnit, progress.Reason);
        Assert.Equal(blocker, progress.BlockerId);

        progress.RecordMoveStep(90.0f, blockedByUnit: true, blocker: blocker);
        Assert.Equal(2, progress.FramesWithoutProgress);

        // The blocker moved aside: distance shrinks again, the stall clears.
        progress.RecordMoveStep(80.0f, blockedByUnit: false, blocker: default);
        Assert.Equal(0, progress.FramesWithoutProgress);
        Assert.Equal(NavigationStallReason.None, progress.Reason);
        Assert.Equal(default, progress.BlockerId);

        // Blocked without an identified blocker still counts frames, reason stays None.
        progress.RecordMoveStep(80.0f, blockedByUnit: true, blocker: default);
        Assert.Equal(1, progress.FramesWithoutProgress);
        Assert.Equal(NavigationStallReason.None, progress.Reason);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void RecordNoRouteAndAwaitingPath_SetReasonsUntilReset()
    {
        var progress = new MovementProgress();

        progress.RecordNoRoute();
        Assert.Equal(NavigationStallReason.NoRoute, progress.Reason);
        Assert.Equal(1, progress.FramesWithoutProgress);

        progress.Reset();
        Assert.Equal(NavigationStallReason.None, progress.Reason);
        Assert.Equal(0, progress.FramesWithoutProgress);

        progress.RecordAwaitingPath();
        Assert.Equal(NavigationStallReason.AwaitingPath, progress.Reason);
        Assert.Equal(1, progress.FramesWithoutProgress);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void GroundFootprintFits_ReportsTheBlockingUnitInBothDirections()
    {
        var manager = new PartitionCellManager(10, 20, 20);
        var first = NewUnit(1, new Vector3(10, 10, 0));
        var second = NewUnit(2, new Vector3(30, 10, 0));
        manager.OnObjectAdded(first);
        manager.OnObjectAdded(second);
        manager.Update();

        // Head-on: each unit's probe sweeps through the other's position.
        Assert.False(manager.GroundFootprintFits(first, new Vector2(40, 10), out var firstBlocker));
        Assert.Equal(second.Id, firstBlocker);

        Assert.False(manager.GroundFootprintFits(second, new Vector2(0, 10), out var secondBlocker));
        Assert.Equal(first.Id, secondBlocker);

        // A clear probe reports no blocker.
        Assert.True(manager.GroundFootprintFits(first, new Vector2(10, 15), out var clearBlocker));
        Assert.Equal(default, clearBlocker);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void StallSampler_ReportsMutuallyBlockedPairAndWaitForCycle()
    {
        var first = NewUnit(1, new Vector3(10, 10, 0), withAi: true);
        var second = NewUnit(2, new Vector3(30, 10, 0), withAi: true);
        first.AIUpdate.TargetPoints.Add(new Vector3(100, 10, 0));
        second.AIUpdate.TargetPoints.Add(new Vector3(0, 10, 0));
        // The first step establishes the distance baseline; the second is the blocked frame.
        first.AIUpdate.Progress.RecordMoveStep(50.0f, blockedByUnit: false, blocker: default);
        second.AIUpdate.Progress.RecordMoveStep(50.0f, blockedByUnit: false, blocker: default);
        first.AIUpdate.Progress.RecordMoveStep(50.0f, blockedByUnit: true, blocker: second.Id);
        second.AIUpdate.Progress.RecordMoveStep(50.0f, blockedByUnit: true, blocker: first.Id);

        var sampler = new NavigationStallSampler();
        sampler.Sample(1, new[] { first, second });

        var records = sampler.Records;
        Assert.Equal(2, records.Count);
        var firstRecord = records.Single(record => record.UnitId == first.Id.Index);
        var secondRecord = records.Single(record => record.UnitId == second.Id.Index);
        Assert.Equal(nameof(NavigationStallReason.BlockedByUnit), firstRecord.Reason);
        Assert.Equal(second.Id.Index, firstRecord.BlockerId);
        Assert.Equal(first.Id.Index, secondRecord.BlockerId);
        Assert.Equal(1, firstRecord.FramesWithoutProgress);
        Assert.Equal(new Vector3(100, 10, 0), firstRecord.OrderDestination);

        var cycle = Assert.Single(sampler.WaitForCycles);
        Assert.Equal(2, cycle.UnitIds.Count);

        // First unit progresses: the cycle breaks, and its worst record is retained.
        first.AIUpdate.Progress.RecordMoveStep(40.0f, blockedByUnit: false, blocker: default);
        sampler.Sample(2, new[] { first, second });
        Assert.Empty(sampler.WaitForCycles);
        var firstWorst = sampler.Records.Single(record => record.UnitId == first.Id.Index);
        Assert.Equal(1, firstWorst.FramesWithoutProgress);
        Assert.Equal(nameof(NavigationStallReason.BlockedByUnit), firstWorst.Reason);
    }

    private GameObject NewUnit(uint id, Vector3 position, bool withAi = false)
    {
        var definition = new ObjectDefinition($"MovementProgress_{id}");
        definition.KindOf.Set(ObjectKinds.Vehicle, true);
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = 100, InitialHealth = 100 },
                ModuleInheritanceMode.Default, inherited: false));
        if (withAi)
        {
            definition.Behaviors.Add("ModuleTag_AI",
                new ModuleDataContainer("ModuleTag_AI",
                    new AIUpdateModuleData(),
                    ModuleInheritanceMode.Default, inherited: false));
        }

        var gameObject = new GameObject(definition, Generals.GameEngine, new Panopticon.Logic.Player(1, null, default, Generals))
        {
            Id = new ObjectId(id),
        };
        gameObject.Geometry.Shapes[0].MajorRadius = 2;
        gameObject.Geometry.Shapes[0].MinorRadius = 2;
        gameObject.UpdateTransform(position);
        return gameObject;
    }
}

// Serialized with the other Navigation classes: these tests submit real async path requests.
[Collection("Navigation")]
public class MovementProgressAiUpdateTests : MockedGameTest
{
    [Fact, Trait("Category", "Roadmap")]
    public void RequestPathAsync_TracksAwaitingPathThenNoRoute()
    {
        var navigation = AsyncPathRequestTests.BuildCorpusNavigation();
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var fixture = PathEqualityCorpus.Load(CorpusFixturePath);
        var passable = fixture.Cases
            .Where(testCase => testCase.Reachable)
            .OrderByDescending(testCase => Math.Max(
                Math.Abs(testCase.StartX - testCase.EndX),
                Math.Abs(testCase.StartY - testCase.EndY)))
            .First();
        var start = AsyncPathRequestTests.NodeWorld(passable.StartX, passable.StartY);
        var end = AsyncPathRequestTests.NodeWorld(passable.EndX, passable.EndY);
        var unit = NewUnit(start, game);

        unit.AIUpdate.RequestPathAsync(end);
        Assert.Equal(NavigationStallReason.AwaitingPath, unit.AIUpdate.Progress.Reason);
        Assert.Empty(unit.AIUpdate.TargetPoints);

        Drain(navigation, () => unit.AIUpdate.TargetPoints.Count > 0);
        Assert.Equal(NavigationStallReason.None, unit.AIUpdate.Progress.Reason);
        Assert.True(unit.AIUpdate.TargetPoints.Count > 1);

        // An off-graph goal returns an empty completion: the unit records NoRoute.
        var offMap = start + new Vector3(1_000_000, 0, 0);
        unit.AIUpdate.RequestPathAsync(offMap);
        Assert.Equal(NavigationStallReason.AwaitingPath, unit.AIUpdate.Progress.Reason);

        Drain(navigation, () => unit.AIUpdate.Progress.Reason == NavigationStallReason.NoRoute);
        Assert.Equal(NavigationStallReason.NoRoute, unit.AIUpdate.Progress.Reason);
        Assert.Empty(unit.AIUpdate.TargetPoints);
    }

    [Fact, Trait("Category", "Roadmap")]
    public void SetTargetPoint_EmptySyncPathRecordsNoRoute()
    {
        var navigation = AsyncPathRequestTests.BuildCorpusNavigation();
        var game = new TestGame(SageGame.CncGenerals, navigation);
        var fixture = PathEqualityCorpus.Load(CorpusFixturePath);
        var passable = fixture.Cases.First(testCase => testCase.Reachable);
        var start = AsyncPathRequestTests.NodeWorld(passable.StartX, passable.StartY);
        var unit = NewUnit(start, game);

        var offMap = start + new Vector3(1_000_000, 0, 0);
        unit.AIUpdate.SetTargetPoint(offMap);
        Assert.Equal(NavigationStallReason.NoRoute, unit.AIUpdate.Progress.Reason);

        // A follow-up good order clears the state.
        var end = AsyncPathRequestTests.NodeWorld(passable.EndX, passable.EndY);
        unit.AIUpdate.SetTargetPoint(end);
        Assert.Equal(NavigationStallReason.None, unit.AIUpdate.Progress.Reason);
        Assert.True(unit.AIUpdate.TargetPoints.Count > 0);
    }

    private static void Drain(NavigationClass navigation, Func<bool> done)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!done() && DateTime.UtcNow < deadline)
        {
            navigation.DrainPathCompletions();
            Thread.Sleep(10);
        }

        Assert.True(done(), "Async path request did not resolve within the deadline.");
    }

    private static string CorpusFixturePath =>
        Path.Combine(Environment.CurrentDirectory, "Navigation", "PathEqualityCorpus.json");

    private GameObject NewUnit(Vector3 position, TestGame game)
    {
        var definition = new ObjectDefinition($"MovementProgressAi_{_nextObjectId}");
        definition.KindOf.Set(ObjectKinds.Infantry, true);
        definition.Behaviors.Add("ModuleTag_Body",
            new ModuleDataContainer("ModuleTag_Body",
                new ActiveBodyModuleData { MaxHealth = 100, InitialHealth = 100 },
                ModuleInheritanceMode.Default, inherited: false));
        definition.Behaviors.Add("ModuleTag_AI",
            new ModuleDataContainer("ModuleTag_AI",
                new AIUpdateModuleData(),
                ModuleInheritanceMode.Default, inherited: false));
        var gameObject = new GameObject(definition, game.GameEngine, new Panopticon.Logic.Player(1, null, default, Generals))
        {
            Id = new ObjectId(_nextObjectId++),
        };
        gameObject.UpdateTransform(position);
        return gameObject;
    }

    private uint _nextObjectId = 1;
}
